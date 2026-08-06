using System.Threading.Channels;
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
            IngestBaseUrl = "https://localhost/",
            ApiKey = "key",
            MaxBatchSize = 500,
            MaxBatchAge = TimeSpan.FromSeconds(2),
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

            // Give the background loop real wall-clock time to open the batch and register its
            // age-timeout wait against the fake clock before we advance it.
            await Task.Delay(250, cts.Token);

            // Well under MaxBatchSize and MaxBatchAge: nothing should have uploaded yet.
            ingestClient.UploadedBatches.ShouldBeEmpty();

            timeProvider.Advance(TimeSpan.FromSeconds(2));

            await WaitUntilAsync(() => ingestClient.UploadedBatches.Count > 0, cts.Token);
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
            IngestBaseUrl = "https://localhost/",
            ApiKey = "key",
            MaxBatchSize = 3,
            MaxBatchAge = TimeSpan.FromMinutes(10), // deliberately long: the clock is never advanced.
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
            IngestBaseUrl = "https://localhost/",
            ApiKey = "key",
            MaxBatchSize = 500,
            MaxBatchAge = TimeSpan.FromMinutes(10),
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

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
        }
    }
}
