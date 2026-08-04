using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Telemetry;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Drives the real <see cref="RaceRoomTelemetrySource"/> through a scripted sequence of raw
/// snapshots via <see cref="FakeSharedMemoryView"/> and asserts the exact emitted
/// <see cref="TelemetryEvent"/> sequence. These are the most important behavioral tests in this
/// project: the layout/mapper tests only prove individual fields translate correctly, but the
/// state machine is what turns a stream of polls into session/lap/connection lifecycle events.
/// </summary>
public class RaceRoomTelemetrySourceStateMachineTests
{
    // Kept short so tests run fast; a timed-out CancellationToken (not these) is what protects
    // against a genuinely hung state machine.
    private static readonly RaceRoomConnectorOptions FastOptions = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(2),
        ReconnectDelay = TimeSpan.FromMilliseconds(5),
    };

    /// <summary>Advances the enumerator once and asserts the event produced is of the expected type.</summary>
    private static async Task<TEvent> NextAsync<TEvent>(IAsyncEnumerator<TelemetryEvent> enumerator)
        where TEvent : TelemetryEvent
    {
        bool moved = await enumerator.MoveNextAsync();
        moved.ShouldBeTrue("expected another TelemetryEvent but the stream ended -- the state machine likely hung, diverged, or the test's CancellationToken expired.");
        return enumerator.Current.ShouldBeOfType<TEvent>();
    }

    [Fact]
    public async Task FullSessionLifecycle_EmitsExpectedEventSequence()
    {
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        // 1. Disconnected -> Connected (the fake's version header is valid, sim is "running").
        var connected = await NextAsync<ConnectionStateChanged>(enumerator);
        connected.State.ShouldBe(ConnectionState.Connected);

        // 2. Leaving menus into a race session -> InSession, SessionStarted, first sample.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Test Track", "Test Layout")
            .WithTicks(100)
            .WithSimulationTime(2.5)
            .WithCompletedLaps(0)
            .Build()
            .ToBytes());

        var toInSession = await NextAsync<ConnectionStateChanged>(enumerator);
        toInSession.State.ShouldBe(ConnectionState.InSession);

        var sessionStarted = await NextAsync<SessionStarted>(enumerator);
        sessionStarted.Session.TrackName.ShouldBe("Test Track");
        sessionStarted.Session.LayoutName.ShouldBe("Test Layout");
        Guid sessionId = sessionStarted.Session.SessionId;

        var firstSample = await NextAsync<TelemetrySampleReceived>(enumerator);
        firstSample.Sample.SessionId.ShouldBe(sessionId);
        firstSample.Sample.SequenceNumber.ShouldBe(0L);

        // 3. Several ticks with advancing game_simulation_ticks -> one TelemetrySampleReceived
        // each, with a monotonically increasing sequence number.
        for (int i = 1; i <= 3; i++)
        {
            view.SetFrame(new R3ESharedRawBuilder()
                .InRaceSession("Test Track", "Test Layout")
                .WithTicks(100 + (i * 10))
                .WithSimulationTime(2.5 + (i * 0.1))
                .WithCompletedLaps(0)
                .Build()
                .ToBytes());

            var sample = await NextAsync<TelemetrySampleReceived>(enumerator);
            sample.Sample.SessionId.ShouldBe(sessionId);
            sample.Sample.SequenceNumber.ShouldBe((long)i);
        }

        // 4. completed_laps advances -> TelemetrySampleReceived followed by LapCompleted.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Test Track", "Test Layout")
            .WithTicks(200)
            .WithSimulationTime(3.0)
            .WithCompletedLaps(1)
            .Build()
            .ToBytes());

        var sampleWithLap = await NextAsync<TelemetrySampleReceived>(enumerator);
        sampleWithLap.Sample.SequenceNumber.ShouldBe(4L);

        var lapCompleted = await NextAsync<LapCompleted>(enumerator);
        lapCompleted.Lap.SessionId.ShouldBe(sessionId);
        lapCompleted.Lap.LapNumber.ShouldBe(1);

        // 5. Back to menus (checkered) -> SessionEnded, then back to Connected.
        view.SetFrame(new R3ESharedRawBuilder().InMenus().Build().ToBytes());

        var sessionEnded = await NextAsync<SessionEnded>(enumerator);
        sessionEnded.SessionId.ShouldBe(sessionId);

        var backToConnected = await NextAsync<ConnectionStateChanged>(enumerator);
        backToConnected.State.ShouldBe(ConnectionState.Connected);
    }

    [Fact]
    public async Task InSessionRestart_SameKeyButTicksRegress_EndsThenStartsNewSessionWithResetSequence()
    {
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track A", "Layout A")
            .WithTicks(500)
            .Build()
            .ToBytes());

        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession
        var firstSession = await NextAsync<SessionStarted>(enumerator);
        Guid firstSessionId = firstSession.Session.SessionId;

        var firstSample = await NextAsync<TelemetrySampleReceived>(enumerator);
        firstSample.Sample.SequenceNumber.ShouldBe(0L);

        // Advance one more tick so the sequence number is off zero before the restart.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track A", "Layout A")
            .WithTicks(600)
            .Build()
            .ToBytes());

        var secondSample = await NextAsync<TelemetrySampleReceived>(enumerator);
        secondSample.Sample.SequenceNumber.ShouldBe(1L);

        // Same (track, layout, session type) tuple, but game_simulation_ticks regresses -- this is
        // what an in-game "restart session" looks like, since everything else stays identical.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track A", "Layout A")
            .WithTicks(0)
            .Build()
            .ToBytes());

        var sessionEnded = await NextAsync<SessionEnded>(enumerator);
        sessionEnded.SessionId.ShouldBe(firstSessionId);

        var toConnected = await NextAsync<ConnectionStateChanged>(enumerator);
        toConnected.State.ShouldBe(ConnectionState.Connected);

        var toInSessionAgain = await NextAsync<ConnectionStateChanged>(enumerator);
        toInSessionAgain.State.ShouldBe(ConnectionState.InSession);

        var newSession = await NextAsync<SessionStarted>(enumerator);
        newSession.Session.SessionId.ShouldNotBe(firstSessionId, "a restarted session must be assigned a brand new SessionId.");

        var newSample = await NextAsync<TelemetrySampleReceived>(enumerator);
        newSample.Sample.SessionId.ShouldBe(newSession.Session.SessionId);
        newSample.Sample.SequenceNumber.ShouldBe(0L, "sequence numbers must reset to the same base for the new session.");
    }

    [Fact]
    public async Task ViewBecomesInvalidMidSession_EmitsSessionEndedThenDisconnected()
    {
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(10)
            .Build()
            .ToBytes());

        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession
        var session = await NextAsync<SessionStarted>(enumerator);
        await NextAsync<TelemetrySampleReceived>(enumerator); // first sample

        // Simulate the game process disappearing mid-session.
        view.Invalidate();

        var sessionEnded = await NextAsync<SessionEnded>(enumerator);
        sessionEnded.SessionId.ShouldBe(session.Session.SessionId);

        var disconnected = await NextAsync<ConnectionStateChanged>(enumerator);
        disconnected.State.ShouldBe(ConnectionState.Disconnected);
        disconnected.Reason.ShouldNotBeNull();
    }
}
