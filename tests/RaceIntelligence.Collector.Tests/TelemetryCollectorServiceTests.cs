using Microsoft.Extensions.Logging.Abstractions;
using RaceIntelligence.Collector.Abstractions;
using RaceIntelligence.Collector.TestSupport;
using RaceIntelligence.Collector.Tests.Support;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Collector.Abstractions.Telemetry;
using RaceIntelligence.RaceRoom.Telemetry;
using Shouldly;

namespace RaceIntelligence.Collector.Tests;

/// <summary>
/// The collect loop's own contract: it dispatches what the source reports to whichever plugins
/// consume it, and keeps those plugins from being able to affect each other.
/// </summary>
/// <remarks>
/// Nothing here knows what a plugin does with an event. Where the telemetry ends up is the plugins'
/// business and is tested in their own suites — this suite exists to pin the loop's guarantees, which
/// is what every plugin relies on.
/// </remarks>
public class TelemetryCollectorServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private static TelemetryCollectorService CreateService(ITelemetrySource source, params RecordingObserver[] observers) =>
        new(source,
            observers,
            observers,
            observers,
            observers,
            NullLogger<TelemetryCollectorService>.Instance);

    [Fact]
    public async Task Every_event_reaches_every_observer_that_consumes_it()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);

        var session = SessionInfoFactory.Create();
        var lap = new LapInfo { SessionId = session.SessionId, LapNumber = 1, LapTime = TimeSpan.FromMinutes(2), IsValid = true };
        var standings = new SessionStandings
        {
            SessionId = session.SessionId,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Drivers = [new DriverStanding { DisplayName = "A" }, new DriverStanding { DisplayName = "B" }],
        };

        var source = new ScriptedTelemetrySource(
        [
            new SessionStarted { Session = session, OccurredAtUtc = DateTimeOffset.UtcNow },
            new TelemetrySampleReceived { Sample = TelemetrySampleFactory.Create(session.SessionId, 0), OccurredAtUtc = DateTimeOffset.UtcNow },
            new StandingsUpdated { Standings = standings, OccurredAtUtc = DateTimeOffset.UtcNow },
            new LapCompleted { Lap = lap, OccurredAtUtc = DateTimeOffset.UtcNow },
            new SessionEnded { SessionId = session.SessionId, OccurredAtUtc = DateTimeOffset.UtcNow },
        ]);

        var first = new RecordingObserver("first");
        var second = new RecordingObserver("second");
        var service = CreateService(source, first, second);

        await service.StartAsync(cts.Token);
        await WaitForAsync(() => source.YieldedEventCount == 5, cts.Token);
        await service.StopAsync(cts.Token);

        string[] expected =
        [
            $"sessionStarted:{session.SessionId}",
            "sample:0",
            "standings:2",
            "lapCompleted:1",
            $"sessionEnded:{session.SessionId}",
        ];

        foreach (var observer in new[] { first, second })
        {
            observer.Calls.Where(call => call != "streamCompleted")
                .ShouldBe(expected, $"{observer.Name} should have seen every event, in order.");
        }
    }

    [Fact]
    public async Task An_observer_that_throws_does_not_stop_the_others_or_the_loop()
    {
        // The whole point of the plugin split: an ingest API outage must not take the live view down
        // with it, and a wedged hub must not stop archiving. Both are this test with different names.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);

        var session = SessionInfoFactory.Create();
        var source = new ScriptedTelemetrySource(
        [
            new SessionStarted { Session = session, OccurredAtUtc = DateTimeOffset.UtcNow },
            new TelemetrySampleReceived { Sample = TelemetrySampleFactory.Create(session.SessionId, 0), OccurredAtUtc = DateTimeOffset.UtcNow },
            new TelemetrySampleReceived { Sample = TelemetrySampleFactory.Create(session.SessionId, 1), OccurredAtUtc = DateTimeOffset.UtcNow },
        ]);

        var failing = new RecordingObserver("failing") { ThrowOn = "sessionStarted" };
        var healthy = new RecordingObserver("healthy");
        var service = CreateService(source, failing, healthy);

        await service.StartAsync(cts.Token);
        await WaitForAsync(() => source.YieldedEventCount == 3, cts.Token);
        await service.StopAsync(cts.Token);

        healthy.Calls.ShouldContain($"sessionStarted:{session.SessionId}");
        healthy.Calls.ShouldContain("sample:0");
        healthy.Calls.ShouldContain("sample:1", "a failure in another plugin must not interrupt the stream.");

        failing.Calls.ShouldContain("sample:0", "the failing plugin keeps receiving later events too.");
    }

    [Fact]
    public async Task A_sample_observer_that_throws_does_not_stop_the_sample_reaching_the_others()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);

        var session = SessionInfoFactory.Create();
        var source = new ScriptedTelemetrySource(
        [
            new TelemetrySampleReceived { Sample = TelemetrySampleFactory.Create(session.SessionId, 0), OccurredAtUtc = DateTimeOffset.UtcNow },
        ]);

        var failing = new RecordingObserver("failing") { ThrowOn = "sample" };
        var healthy = new RecordingObserver("healthy");
        var service = CreateService(source, failing, healthy);

        await service.StartAsync(cts.Token);
        await WaitForAsync(() => source.YieldedEventCount == 1, cts.Token);
        await service.StopAsync(cts.Token);

        healthy.Calls.ShouldContain("sample:0");
    }

    [Fact]
    public async Task Shutdown_completes_promptly_when_an_observer_is_parked_applying_backpressure()
    {
        // The archive buffer defaults to Wait, whose entire purpose is to fill up and park the
        // producer. A parked observer holds this loop's thread and so can never observe the
        // stoppingToken; only being told the stream is over releases it. Without that signal,
        // shutting down mid-race would wait out the host's whole ShutdownTimeout.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);

        var session = SessionInfoFactory.Create();
        var source = new ScriptedTelemetrySource(
        [
            new TelemetrySampleReceived { Sample = TelemetrySampleFactory.Create(session.SessionId, 0), OccurredAtUtc = DateTimeOffset.UtcNow },
        ]);

        var blocking = new RecordingObserver("blocking") { BlockInOnSample = true };
        var service = CreateService(source, blocking);

        await service.StartAsync(cts.Token);
        await WaitForAsync(() => blocking.Calls.Contains("sample:0"), cts.Token);

        await service.StopAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(10), cts.Token);

        blocking.StreamCompleted.ShouldBeTrue("the parked observer must be told the stream is over.");
    }

    [Fact]
    public async Task Connection_state_changes_are_logged_rather_than_dispatched()
    {
        // Connection state is the collector's own business. Forwarding it would make every plugin
        // implement a handler for something none of them acts on.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);

        var source = new ScriptedTelemetrySource(
        [
            new ConnectionStateChanged { State = ConnectionState.Connected, Reason = null, OccurredAtUtc = DateTimeOffset.UtcNow },
            new ConnectionStateChanged { State = ConnectionState.InSession, Reason = "a session started", OccurredAtUtc = DateTimeOffset.UtcNow },
        ]);

        var observer = new RecordingObserver("only");
        var service = CreateService(source, observer);

        await service.StartAsync(cts.Token);
        await WaitForAsync(() => source.YieldedEventCount == 2, cts.Token);
        await service.StopAsync(cts.Token);

        observer.Calls.Where(call => call != "streamCompleted").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_sample_stream_completing_is_reported_even_when_no_sample_ever_arrived()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);

        var source = new ScriptedTelemetrySource([]);
        var observer = new RecordingObserver("only");
        var service = CreateService(source, observer);

        await service.StartAsync(cts.Token);
        await service.StopAsync(cts.Token);

        observer.StreamCompleted.ShouldBeTrue();
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
        }
    }
}
