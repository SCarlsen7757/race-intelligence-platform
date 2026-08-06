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

        // Deliberately long: every test except the frozen-frame one drives the tick counter
        // forward itself, and must never trip the stale-frame (game exited) check by accident.
        StaleFrameTimeout = TimeSpan.FromMinutes(5),
    };

    /// <summary>Advances the enumerator once and asserts the event produced is of the expected type.</summary>
    private static async Task<TEvent> NextAsync<TEvent>(IAsyncEnumerator<TelemetryEvent> enumerator)
        where TEvent : TelemetryEvent
    {
        bool moved = await enumerator.MoveNextAsync();
        moved.ShouldBeTrue("expected another TelemetryEvent but the stream ended -- the state machine likely hung, diverged, or the test's CancellationToken expired.");
        return enumerator.Current.ShouldBeOfType<TEvent>();
    }

    /// <summary>
    /// Advances until the next event of <typeparamref name="TEvent"/>, skipping over the steady
    /// stream of <see cref="TelemetrySampleReceived"/> a live poll loop produces in between. Any
    /// other event type is a divergence and fails the test.
    /// </summary>
    private static async Task<TEvent> NextSkippingSamplesAsync<TEvent>(IAsyncEnumerator<TelemetryEvent> enumerator)
        where TEvent : TelemetryEvent
    {
        while (await enumerator.MoveNextAsync())
        {
            if (enumerator.Current is TEvent match)
            {
                return match;
            }

            enumerator.Current.ShouldBeOfType<TelemetrySampleReceived>(
                $"only telemetry samples may appear before the expected {typeof(TEvent).Name}.");
        }

        throw new ShouldAssertException(
            $"the event stream ended before a {typeof(TEvent).Name} was produced -- the state machine likely hung or the test's CancellationToken expired.");
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

    [Fact]
    public async Task ACompletedLapsJumpAcrossASkippedPoll_EmitsOneLapCompletedPerLap()
    {
        // A missed poll (GC pause, stalled frame, descheduled process) advances completed_laps by
        // more than one. Emitting a single LapCompleted and absorbing the whole delta silently
        // deletes the laps in between -- and because raw data is permanent, they never come back.
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(100)
            .WithCompletedLaps(0)
            .Build()
            .ToBytes());

        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession
        await NextAsync<SessionStarted>(enumerator);
        await NextAsync<TelemetrySampleReceived>(enumerator);

        // Three laps completed between two observations, with only the newest one's timings
        // available in the snapshot (lap_time_previous_self always describes the most recent lap).
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(200)
            .WithCompletedLaps(3)
            .Configure((ref R3ESharedRaw raw) =>
            {
                raw.LapTimePreviousSelf = 91.5f;
                raw.PrevLapValid = 1;
            })
            .Build()
            .ToBytes());

        await NextAsync<TelemetrySampleReceived>(enumerator);

        var first = await NextAsync<LapCompleted>(enumerator);
        var second = await NextAsync<LapCompleted>(enumerator);
        var third = await NextAsync<LapCompleted>(enumerator);

        first.Lap.LapNumber.ShouldBe(1);
        second.Lap.LapNumber.ShouldBe(2);
        third.Lap.LapNumber.ShouldBe(3);

        // Only the newest lap may claim the snapshot's timings; copying them onto the skipped laps
        // would invent three identical 91.5 s laps that were never driven.
        first.Lap.LapTime.ShouldBeNull();
        second.Lap.LapTime.ShouldBeNull();
        third.Lap.LapTime.ShouldBe(TimeSpan.FromSeconds(91.5));
        third.Lap.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task TornFrame_IsDiscardedRatherThanPublishedAsASample()
    {
        // RaceRoom overwrites the shared memory block in place with no sequence lock, so a read can
        // straddle two frames and yield a snapshot that never existed. Such a sample would be
        // stored permanently, so the tick counter is re-read after the copy and a mismatch discards
        // the tick.
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected

        byte[] staleFrame = new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(100)
            .WithSpeed(10f)
            .Build()
            .ToBytes();

        byte[] freshFrame = new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(200)
            .WithSpeed(99f)
            .Build()
            .ToBytes();

        view.SetFrame(staleFrame);

        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession
        await NextAsync<SessionStarted>(enumerator);
        (await NextAsync<TelemetrySampleReceived>(enumerator)).Sample.Speed.ShouldBe(10f);

        // The next poll copies the stale frame, but the "game" swaps in the fresh one before the
        // consistency re-read lands -- the classic torn read.
        view.RewriteAfterNextRead(freshFrame);

        var next = await NextAsync<TelemetrySampleReceived>(enumerator);
        next.Sample.Speed.ShouldBe(
            99f,
            "the torn snapshot must be discarded; the next sample published must come from a whole frame.");
    }

    [Fact]
    public async Task FrozenTickCounterMidSession_EndsTheSessionAndReconnects()
    {
        // Regression test for the "game exited but the mapping outlives it" bug: RaceRoom's shared
        // memory section stays readable while this process holds a handle to it, so an exited game
        // looks like a live one whose last frame never changes. Nothing else in the state machine
        // catches that -- the frame is on-track, the session key is identical, and the tick counter
        // is equal rather than lower, so the tick-*regression* check never fires either. Without a
        // stale-frame timeout the source stays InSession forever, re-uploading a dead frame at
        // 60 Hz and never attempting a reconnect.
        var options = FastOptions with { StaleFrameTimeout = TimeSpan.FromMilliseconds(150) };

        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        int viewsOpened = 0;
        await using var source = new RaceRoomTelemetrySource(options, () =>
        {
            viewsOpened++;
            return view;
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected
        viewsOpened.ShouldBe(1);

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(1234)
            .Build()
            .ToBytes());

        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession
        var session = await NextAsync<SessionStarted>(enumerator);
        await NextAsync<TelemetrySampleReceived>(enumerator); // first sample

        // The "game" now exits: the frame is never updated again, so game_simulation_ticks stays
        // pinned at 1234 while the view happily keeps serving it.
        var sessionEnded = await NextSkippingSamplesAsync<SessionEnded>(enumerator);
        sessionEnded.SessionId.ShouldBe(session.Session.SessionId);

        var disconnected = await NextAsync<ConnectionStateChanged>(enumerator);
        disconnected.State.ShouldBe(ConnectionState.Disconnected);
        disconnected.Reason.ShouldNotBeNull().ShouldContain("tick counter");

        // ...and the source must go back around the reconnect loop rather than sitting dead.
        var reconnected = await NextAsync<ConnectionStateChanged>(enumerator);
        reconnected.State.ShouldBe(ConnectionState.Connected);
        viewsOpened.ShouldBe(2, "a stale-frame disconnect must be followed by a genuine reconnect attempt.");
    }
}
