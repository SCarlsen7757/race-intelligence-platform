using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using RaceIntelligence.Collector.Buffering;
using RaceIntelligence.Collector.Tests.Support;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;
using Shouldly;

namespace RaceIntelligence.Collector.Tests;

public class TelemetryCollectorServiceTests
{
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

        // TelemetryCollectorService waits (bounded) for the buffer to drain before PATCHing a
        // session's end, exactly as TelemetryUploadService would in the real pipeline. Nothing in
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

        var service = new TelemetryCollectorService(source, buffer, ingestClient, NullLogger<TelemetryCollectorService>.Instance);

        await service.StartAsync(cts.Token);
        try
        {
            while (ingestClient.UpdatedSessions.Count == 0 && !cts.IsCancellationRequested)
            {
                await Task.Delay(20, cts.Token);
            }
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
    public async Task Connection_state_changes_do_not_throw_and_do_not_call_the_ingest_client()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var events = new TelemetryEvent[]
        {
            new ConnectionStateChanged { OccurredAtUtc = DateTimeOffset.UtcNow, State = ConnectionState.WaitingForSimulator, Reason = "not running" },
            new ConnectionStateChanged { OccurredAtUtc = DateTimeOffset.UtcNow, State = ConnectionState.Connected },
        };

        await using var buffer = new ChannelTelemetryBuffer(10, BoundedChannelFullMode.Wait, NullLogger<ChannelTelemetryBuffer>.Instance);
        var ingestClient = new RecordingIngestClient();
        var source = new ScriptedTelemetrySource(events);
        var service = new TelemetryCollectorService(source, buffer, ingestClient, NullLogger<TelemetryCollectorService>.Instance);

        await service.StartAsync(cts.Token);
        await Task.Delay(200, cts.Token);
        await service.StopAsync(cts.Token);

        ingestClient.CreatedSessions.ShouldBeEmpty();
        ingestClient.UpdatedSessions.ShouldBeEmpty();
        ingestClient.RecordedLaps.ShouldBeEmpty();
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
            new ScriptedTelemetrySource(events), buffer, new RecordingIngestClient(), NullLogger<TelemetryCollectorService>.Instance);

        await service.StartAsync(cts.Token);

        // Nothing reads the buffer, so the producer is parked on the third write.
        while (buffer.Metrics.CurrentDepth < 2)
        {
            await Task.Delay(10, cts.Token);
        }

        var stopping = service.StopAsync(cts.Token);
        var completed = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(10), cts.Token));

        completed.ShouldBe(stopping, "shutdown must not block on a producer parked against a full buffer.");
        await stopping;
    }
}
