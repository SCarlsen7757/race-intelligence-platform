using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RaceIntelligence.Collector.Buffering;
using RaceIntelligence.Collector.Tests.Support;
using RaceIntelligence.Collector.Upload;
using Shouldly;

namespace RaceIntelligence.Collector.Tests.Upload;

public class TelemetryUploadServiceTests
{
    private static ChannelTelemetryBuffer CreateBuffer() =>
        new(capacity: 1_000, BoundedChannelFullMode.Wait, NullLogger<ChannelTelemetryBuffer>.Instance);

    [Fact]
    public async Task A_batch_under_size_flushes_once_MaxBatchAge_elapses_on_the_fake_clock()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var collectorOptions = Options.Create(new CollectorOptions
        {
            Ingest = new IngestOptions
            {
                BaseUrl = "https://localhost/",
                ApiKey = "key",
                MaxBatchSize = 500,
                MaxBatchAge = TimeSpan.FromSeconds(2),
            },
        });

        await using var buffer = CreateBuffer();
        var ingestClient = new RecordingIngestClient();
        var service = new TelemetryUploadService(buffer, ingestClient, collectorOptions, new OpenBatchTracker(), timeProvider, NullLogger<TelemetryUploadService>.Instance);

        var sessionId = Guid.NewGuid();
        await service.StartAsync(cts.Token);
        try
        {
            buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 0));
            await WaitUntilAsync(() => buffer.Metrics.TotalRead > 0, cts.Token);

            // Safe without any sleep: one sample is far under MaxBatchSize, and the only other
            // trigger is batch age on a clock that does not move unless this test moves it.
            ingestClient.UploadedBatches.ShouldBeEmpty();

            // Advance in a loop rather than once: a single Advance is a no-op if the background
            // loop has not registered its age-timeout wait yet, which is the race the old
            // Task.Delay(250) was papering over.
            await AdvanceUntilAsync(timeProvider, TimeSpan.FromSeconds(2), () => ingestClient.UploadedBatches.Count > 0, cts.Token);
        }
        finally
        {
            await service.StopAsync(cts.Token);
        }

        var batch = ingestClient.UploadedBatches.ShouldHaveSingleItem();
        batch.SessionId.ShouldBe(sessionId);
        batch.Samples.ShouldHaveSingleItem().SequenceNumber.ShouldBe(0);
    }

    [Fact]
    public async Task A_batch_flushes_once_MaxBatchSize_is_reached_without_waiting_for_MaxBatchAge()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var collectorOptions = Options.Create(new CollectorOptions
        {
            Ingest = new IngestOptions
            {
                BaseUrl = "https://localhost/",
                ApiKey = "key",
                MaxBatchSize = 3,
                MaxBatchAge = TimeSpan.FromMinutes(10), // deliberately long: the clock is never advanced.
            },
        });

        await using var buffer = CreateBuffer();
        var ingestClient = new RecordingIngestClient();
        var service = new TelemetryUploadService(buffer, ingestClient, collectorOptions, new OpenBatchTracker(), timeProvider, NullLogger<TelemetryUploadService>.Instance);

        var sessionId = Guid.NewGuid();
        await service.StartAsync(cts.Token);
        try
        {
            buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 0));
            buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 1));
            buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 2));

            await WaitUntilAsync(() => ingestClient.UploadedBatches.Count > 0, cts.Token);
        }
        finally
        {
            await service.StopAsync(cts.Token);
        }

        var batch = ingestClient.UploadedBatches.ShouldHaveSingleItem();
        batch.SessionId.ShouldBe(sessionId);
        batch.Samples.Count.ShouldBe(3);
        batch.FirstSequenceNumber.ShouldBe(0);
        batch.LastSequenceNumber.ShouldBe(2);
    }

    [Fact]
    public async Task A_new_sessions_sample_flushes_the_previous_sessions_open_batch_immediately()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var collectorOptions = Options.Create(new CollectorOptions
        {
            Ingest = new IngestOptions
            {
                BaseUrl = "https://localhost/",
                ApiKey = "key",
                MaxBatchSize = 500,
                MaxBatchAge = TimeSpan.FromMinutes(10),
            },
        });

        await using var buffer = CreateBuffer();
        var ingestClient = new RecordingIngestClient();
        var service = new TelemetryUploadService(buffer, ingestClient, collectorOptions, new OpenBatchTracker(), timeProvider, NullLogger<TelemetryUploadService>.Instance);

        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();
        await service.StartAsync(cts.Token);
        try
        {
            buffer.TryWrite(TelemetrySampleFactory.Create(firstSessionId, 0));
            await WaitUntilAsync(() => buffer.Metrics.TotalRead > 0, cts.Token);

            buffer.TryWrite(TelemetrySampleFactory.Create(secondSessionId, 0));

            // The boundary flush is synchronous within the drain loop, so by the time a batch has
            // been uploaded at all, it must be the first session's — assert before stopping the
            // service, since StopAsync's own best-effort shutdown flush would also (correctly)
            // upload the second session's still-open one-sample batch and muddy this assertion.
            await WaitUntilAsync(() => ingestClient.UploadedBatches.Count > 0, cts.Token);
            var firstBatch = ingestClient.UploadedBatches.ShouldHaveSingleItem();
            firstBatch.SessionId.ShouldBe(firstSessionId);
            firstBatch.Samples.ShouldHaveSingleItem();
        }
        finally
        {
            await service.StopAsync(cts.Token);
        }
    }

    [Fact]
    public async Task A_batch_that_still_fails_after_resilience_is_logged_as_an_error_and_discarded()
    {
        // The one place the pipeline knowingly loses data. It was unreachable in tests because the
        // fake ingest client had no failure mode, so nothing verified that the loss is reported
        // rather than silent, that the samples are not re-queued, and that the service survives it.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var collectorOptions = Options.Create(new CollectorOptions
        {
            Ingest = new IngestOptions
            {
                BaseUrl = "https://localhost/",
                ApiKey = "key",
                MaxBatchSize = 2,
                MaxBatchAge = TimeSpan.FromMinutes(10),
            },
        });

        await using var buffer = CreateBuffer();
        var ingestClient = new RecordingIngestClient { FailUploadsWith = new HttpRequestException("ingest API is down") };
        var logger = new CapturingLogger<TelemetryUploadService>();
        var service = new TelemetryUploadService(buffer, ingestClient, collectorOptions, new OpenBatchTracker(), timeProvider, logger);

        var sessionId = Guid.NewGuid();
        await service.StartAsync(cts.Token);
        try
        {
            buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 0));
            buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 1));

            await WaitUntilAsync(() => ingestClient.FailedUploadAttempts.Count > 0, cts.Token);

            // Discarded, not re-queued: re-queuing would reorder these behind samples read after
            // them, and the buffer may already be full again by now.
            buffer.Metrics.CurrentDepth.ShouldBe(0);

            // ...and the service must still be running, so a later batch still uploads.
            ingestClient.FailUploadsWith = null;
            buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 2));
            buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 3));

            await WaitUntilAsync(() => ingestClient.UploadedBatches.Count > 0, cts.Token);
        }
        finally
        {
            await service.StopAsync(cts.Token);
        }

        ingestClient.FailedUploadAttempts.ShouldHaveSingleItem().Samples.Count.ShouldBe(2);
        ingestClient.UploadedBatches.ShouldHaveSingleItem().Samples.Select(s => s.SequenceNumber).ShouldBe([2L, 3L]);

        var error = logger.Entries.Where(entry => entry.Level == LogLevel.Error).ShouldHaveSingleItem();
        error.Exception.ShouldBeOfType<HttpRequestException>();
        error.Message.ShouldContain(sessionId.ToString());
    }

    [Fact]
    public async Task Shutdown_uploads_the_still_open_batch_instead_of_dropping_it()
    {
        // The final flush is the difference between "the last few seconds of a session are stored"
        // and "they are lost every single time the collector stops". Asserting it directly, rather
        // than arranging assertions to avoid it.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var collectorOptions = Options.Create(new CollectorOptions
        {
            Ingest = new IngestOptions
            {
                BaseUrl = "https://localhost/",
                ApiKey = "key",
                MaxBatchSize = 500, // never reached
                MaxBatchAge = TimeSpan.FromMinutes(10), // never reached: the clock is never advanced
            },
        });

        await using var buffer = CreateBuffer();
        var ingestClient = new RecordingIngestClient();
        var service = new TelemetryUploadService(
            buffer, ingestClient, collectorOptions, new OpenBatchTracker(), timeProvider, NullLogger<TelemetryUploadService>.Instance);

        var sessionId = Guid.NewGuid();
        await service.StartAsync(cts.Token);

        buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 0));
        buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 1));
        await WaitUntilAsync(() => buffer.Metrics.TotalRead == 2, cts.Token);

        ingestClient.UploadedBatches.ShouldBeEmpty("neither the size nor the age trigger has fired.");

        await service.StopAsync(cts.Token);

        var batch = ingestClient.UploadedBatches.ShouldHaveSingleItem();
        batch.SessionId.ShouldBe(sessionId);
        batch.Samples.Select(s => s.SequenceNumber).ShouldBe([0L, 1L]);
    }

    [Fact]
    public async Task A_shutdown_flush_that_fails_is_logged_rather_than_swallowed()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var collectorOptions = Options.Create(new CollectorOptions
        {
            Ingest = new IngestOptions
            {
                BaseUrl = "https://localhost/",
                ApiKey = "key",
                MaxBatchSize = 500,
                MaxBatchAge = TimeSpan.FromMinutes(10),
            },
        });

        await using var buffer = CreateBuffer();
        var ingestClient = new RecordingIngestClient { FailUploadsWith = new HttpRequestException("ingest API is down") };
        var logger = new CapturingLogger<TelemetryUploadService>();
        var service = new TelemetryUploadService(buffer, ingestClient, collectorOptions, new OpenBatchTracker(), timeProvider, logger);

        await service.StartAsync(cts.Token);
        buffer.TryWrite(TelemetrySampleFactory.Create(Guid.NewGuid(), 0));
        await WaitUntilAsync(() => buffer.Metrics.TotalRead == 1, cts.Token);

        await service.StopAsync(cts.Token);

        ingestClient.FailedUploadAttempts.ShouldHaveSingleItem();
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
        }
    }

    /// <summary>
    /// Steps a <see cref="FakeTimeProvider"/> forward until <paramref name="condition"/> holds. A
    /// single Advance is not enough on its own: it does nothing if the code under test has not
    /// registered its timer yet, and the fake clock never moves by itself.
    /// </summary>
    private static async Task AdvanceUntilAsync(FakeTimeProvider timeProvider, TimeSpan step, Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            timeProvider.Advance(step);
            await Task.Delay(5, cancellationToken);
        }
    }
}
