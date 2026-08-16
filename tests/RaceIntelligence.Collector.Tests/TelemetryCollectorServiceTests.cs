using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RaceIntelligence.Collector.Buffering;
using RaceIntelligence.Collector.Live;
using RaceIntelligence.Collector.Tests.Support;
using RaceIntelligence.Collector.Upload;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;
using Shouldly;

namespace RaceIntelligence.Collector.Tests;

public class TelemetryCollectorServiceTests
{
    /// <summary>Matches the service's own end-of-session drain poll interval.</summary>
    private static readonly TimeSpan FlushPollInterval = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task Session_is_created_before_samples_are_buffered_a_lap_is_recorded_and_the_session_is_patched_on_end()
    {
        var timeout = TimeSpan.FromSeconds(20);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(timeout);

        var log = new List<string>();
        var session = SessionInfoFactory.Create();
        var sample0 = TelemetrySampleFactory.Create(session.SessionId, 0);
        var sample1 = TelemetrySampleFactory.Create(session.SessionId, 1);
        var lap = new LapInfo { SessionId = session.SessionId, LapNumber = 1, LapTime = TimeSpan.FromMinutes(2), IsValid = true };
        var now = DateTimeOffset.UtcNow;

        var events = new TelemetryEvent[]
        {
            new SessionStarted { OccurredAtUtc = now, Session = session },
            new TelemetrySampleReceived { OccurredAtUtc = now, Sample = sample0 },
            new TelemetrySampleReceived { OccurredAtUtc = now, Sample = sample1 },
            new LapCompleted { OccurredAtUtc = now, Lap = lap },
            new SessionEnded { OccurredAtUtc = now.AddMinutes(20), SessionId = session.SessionId },
        };

        var innerBuffer = new ChannelTelemetryBuffer(capacity: 10, BoundedChannelFullMode.Wait, NullLogger<ChannelTelemetryBuffer>.Instance);
        await using var buffer = new LoggingTelemetryBuffer(innerBuffer, log);
        var ingestClient = new RecordingIngestClient(log);
        var source = new ScriptedTelemetrySource(events);

        // TelemetryCollectorService waits (bounded) for the upload pipeline to drain before PATCHing
        // a session's end, exactly as TelemetryUploadService would in the real pipeline. Nothing in
        // this test starts that service, so drain the buffer directly to keep the test fast instead
        // of waiting out the collector's own flush timeout.
        using var drainCts = new CancellationTokenSource();
        var drainTask = Task.Run(async () =>
        {
            while (!drainCts.IsCancellationRequested)
            {
                while (innerBuffer.TryRead(out _))
                {
                }

                await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
            }
        }, CancellationToken.None);

        var service = new TelemetryCollectorService(
            source, buffer, ingestClient, new NullLiveOutbox(), new OpenBatchTracker(), TimeProvider.System, NullLogger<TelemetryCollectorService>.Instance);

        await service.StartAsync(cts.Token);
        try
        {
            await WaitUntilAsync(() => ingestClient.UpdatedSessions.Count > 0, cts.Token);
        }
        finally
        {
            await service.StopAsync(cts.Token);
            await drainCts.CancelAsync();
            await drainTask;
        }

        ingestClient.CreatedSessions.ShouldHaveSingleItem().SessionId.ShouldBe(session.SessionId);
        ingestClient.RecordedLaps.ShouldHaveSingleItem().Request.LapNumber.ShouldBe(1);
        var update = ingestClient.UpdatedSessions.ShouldHaveSingleItem();
        update.SessionId.ShouldBe(session.SessionId);
        update.Request.EndedAtUtc.ShouldBe(now.AddMinutes(20));

        // Ordering: the session must exist server-side before any sample or lap that references
        // it, and the end-of-session PATCH must be the very last thing sent for this session.
        int createIndex = log.IndexOf($"CreateSession:{session.SessionId}");
        int sample0Index = log.IndexOf("Sample:0");
        int sample1Index = log.IndexOf("Sample:1");
        int lapIndex = log.IndexOf($"RecordLap:{session.SessionId}:1");
        int updateIndex = log.IndexOf($"UpdateSession:{session.SessionId}");

        createIndex.ShouldBeGreaterThanOrEqualTo(0);
        createIndex.ShouldBeLessThan(sample0Index);
        createIndex.ShouldBeLessThan(sample1Index);
        createIndex.ShouldBeLessThan(lapIndex);
        updateIndex.ShouldBe(log.Count - 1);
    }

    [Fact]
    public async Task Connection_state_changes_do_not_throw_and_are_not_forwarded_to_the_ingest_client_or_the_live_outbox()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var events = new TelemetryEvent[]
        {
            new ConnectionStateChanged { OccurredAtUtc = DateTimeOffset.UtcNow, State = ConnectionState.WaitingForSimulator, Reason = "not running" },
            new ConnectionStateChanged { OccurredAtUtc = DateTimeOffset.UtcNow, State = ConnectionState.Connected },
        };

        await using var buffer = new ChannelTelemetryBuffer(10, BoundedChannelFullMode.Wait, NullLogger<ChannelTelemetryBuffer>.Instance);
        var ingestClient = new RecordingIngestClient();
        var liveOutbox = new RecordingLiveOutbox();
        var source = new ScriptedTelemetrySource(events);
        var service = new TelemetryCollectorService(
            source, buffer, ingestClient, liveOutbox, new OpenBatchTracker(), TimeProvider.System, NullLogger<TelemetryCollectorService>.Instance);

        await service.StartAsync(cts.Token);
        try
        {
            // Wait on the source being fully consumed rather than on a fixed sleep: the scripted
            // source idles once it has yielded everything, so an empty ingest client at that point
            // is a real result and not just a race the test happened to win.
            await WaitUntilAsync(() => source.YieldedEventCount == events.Length, cts.Token);
        }
        finally
        {
            await service.StopAsync(cts.Token);
        }

        ingestClient.CreatedSessions.ShouldBeEmpty();
        ingestClient.UpdatedSessions.ShouldBeEmpty();
        ingestClient.RecordedLaps.ShouldBeEmpty();

        // Nor the live outbox: a connection-state change is collector bookkeeping, not something
        // the hub has any use for.
        liveOutbox.StartedSessions.ShouldBeEmpty();
        liveOutbox.EndedSessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_end_of_session_PATCH_waits_for_the_uploaders_open_batch_not_just_the_buffer()
    {
        // Samples leave the buffer the instant the uploader reads them, but are not uploaded until
        // the batch flushes. Watching buffer depth alone therefore declares the pipeline drained
        // while up to MaxBatchSize samples are still in flight, and the session gets marked ended
        // before its own tail telemetry has been accepted.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var session = SessionInfoFactory.Create();
        var now = DateTimeOffset.UtcNow;
        var events = new TelemetryEvent[]
        {
            new SessionStarted { OccurredAtUtc = now, Session = session },
            new SessionEnded { OccurredAtUtc = now.AddMinutes(5), SessionId = session.SessionId },
        };

        var timeProvider = new FakeTimeProvider(now);
        var openBatch = new OpenBatchTracker();
        openBatch.Set(5); // buffer is empty, but five samples sit unuploaded in the open batch.

        await using var buffer = new ChannelTelemetryBuffer(10, BoundedChannelFullMode.Wait, NullLogger<ChannelTelemetryBuffer>.Instance);
        var ingestClient = new RecordingIngestClient();
        var service = new TelemetryCollectorService(
            new ScriptedTelemetrySource(events), buffer, ingestClient, new NullLiveOutbox(), openBatch, timeProvider, NullLogger<TelemetryCollectorService>.Instance);

        await service.StartAsync(cts.Token);
        try
        {
            await WaitUntilAsync(() => ingestClient.CreatedSessions.Count > 0, cts.Token);

            // Well short of the 10s flush timeout, so nothing here can time the wait out: the PATCH
            // must still be held back purely because the open batch is non-empty.
            for (int i = 0; i < 20; i++)
            {
                timeProvider.Advance(FlushPollInterval);
                await Task.Delay(5, cts.Token);
            }

            ingestClient.UpdatedSessions.ShouldBeEmpty(
                "the session must not be marked ended while the uploader still holds unuploaded samples.");

            openBatch.Set(0);
            await AdvanceUntilAsync(timeProvider, () => ingestClient.UpdatedSessions.Count > 0, cts.Token);
        }
        finally
        {
            await service.StopAsync(cts.Token);
        }

        ingestClient.UpdatedSessions.ShouldHaveSingleItem().SessionId.ShouldBe(session.SessionId);
    }

    [Fact]
    public async Task The_end_of_session_drain_wait_does_not_stall_the_poll_loop()
    {
        // The drain wait is bounded by a 10 second timeout. Awaiting it inline would freeze event
        // handling for that long, so a session started right after the previous one ended (restart
        // a race, jump straight into the next session) would be picked up seconds late.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        var firstSession = SessionInfoFactory.Create();
        var secondSession = SessionInfoFactory.Create();
        var now = DateTimeOffset.UtcNow;
        var events = new TelemetryEvent[]
        {
            new SessionStarted { OccurredAtUtc = now, Session = firstSession },
            new SessionEnded { OccurredAtUtc = now.AddMinutes(5), SessionId = firstSession.SessionId },
            new SessionStarted { OccurredAtUtc = now.AddMinutes(5), Session = secondSession },
        };

        var timeProvider = new FakeTimeProvider(now);
        var openBatch = new OpenBatchTracker();
        openBatch.Set(1); // keeps the first session's drain wait pending for the whole test.

        await using var buffer = new ChannelTelemetryBuffer(10, BoundedChannelFullMode.Wait, NullLogger<ChannelTelemetryBuffer>.Instance);
        var ingestClient = new RecordingIngestClient();
        var service = new TelemetryCollectorService(
            new ScriptedTelemetrySource(events), buffer, ingestClient, new NullLiveOutbox(), openBatch, timeProvider, NullLogger<TelemetryCollectorService>.Instance);

        await service.StartAsync(cts.Token);
        try
        {
            // The clock never moves, so the first session's drain wait cannot time out or clear --
            // yet the second session must still be created.
            await WaitUntilAsync(() => ingestClient.CreatedSessions.Count == 2, cts.Token);

            ingestClient.UpdatedSessions.ShouldBeEmpty();
            ingestClient.CreatedSessions[1].SessionId.ShouldBe(secondSession.SessionId);
        }
        finally
        {
            openBatch.Set(0);
            await service.StopAsync(cts.Token);
        }
    }

    [Fact]
    public async Task Shutdown_completes_promptly_even_with_a_full_buffer_and_no_reader()
    {
        // Regression test for the shutdown deadlock: BufferFullMode.Wait exists precisely to
        // produce a full buffer, and a full buffer parks this service inside a blocking TryWrite
        // that owns its thread -- so it can never observe its own stoppingToken. If nothing unparks
        // it, StopAsync waits forever (in production: until the host's ShutdownTimeout elapses).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var events = Enumerable.Range(0, 20)
            .Select(i => (TelemetryEvent)new TelemetrySampleReceived
            {
                OccurredAtUtc = now,
                Sample = TelemetrySampleFactory.Create(sessionId, i),
            })
            .ToArray();

        await using var buffer = new ChannelTelemetryBuffer(capacity: 2, BoundedChannelFullMode.Wait, NullLogger<ChannelTelemetryBuffer>.Instance);
        var service = new TelemetryCollectorService(
            new ScriptedTelemetrySource(events), buffer, new RecordingIngestClient(), new NullLiveOutbox(), new OpenBatchTracker(), TimeProvider.System,
            NullLogger<TelemetryCollectorService>.Instance);

        await service.StartAsync(cts.Token);

        // Nothing reads the buffer, so the producer is parked on the third write.
        await WaitUntilAsync(() => buffer.Metrics.CurrentDepth >= 2, cts.Token);

        var stopping = service.StopAsync(cts.Token);
        var completed = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(10), cts.Token));

        completed.ShouldBe(stopping, "shutdown must not block on a producer parked against a full buffer.");
        await stopping;
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
    /// Drives a <see cref="FakeTimeProvider"/> forward one drain-poll interval at a time until
    /// <paramref name="condition"/> holds — the fake clock only fires timers when advanced, so a
    /// loop waiting on it has to advance it rather than sleep against the wall clock.
    /// </summary>
    private static async Task AdvanceUntilAsync(FakeTimeProvider timeProvider, Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            timeProvider.Advance(FlushPollInterval);
            await Task.Delay(5, cancellationToken);
        }
    }
}
