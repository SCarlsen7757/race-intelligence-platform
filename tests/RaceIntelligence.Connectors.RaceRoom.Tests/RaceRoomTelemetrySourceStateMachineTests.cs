using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Collector.Abstractions.Telemetry;
using RaceIntelligence.RaceRoom.Telemetry;
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
        MaxSuspendDuration = TimeSpan.FromMinutes(5),

        // Start on the first qualifying frame. Tests here script frames one at a time and would
        // otherwise spend the debounce window on every session start; the debounce has its own
        // test below.
        SessionStartDebounce = TimeSpan.Zero,

        // Off, because these tests assert the exact event sequence and extras are not part of the
        // session lifecycle they describe. Weakening the assertions to tolerate a third event type
        // would cost the divergence detection that makes this suite worth having; the extras
        // channel has its own suite in RaceRoomTelemetrySourceExtrasTests.
        SlowChannelInterval = Timeout.InfiniteTimeSpan,
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
        lapCompleted.Lap.IsValid.ShouldBeTrue(
            "a clean on-track lap must be reported as valid -- this went unnoticed while the builder defaulted prev_lap_valid to -1 (N/A).");

        // 5. Back to menus -> the session is suspended, not ended. A menu frame cannot be told
        // apart from a mid-session pause, so the session stays open until something unambiguous
        // happens.
        view.SetFrame(new R3ESharedRawBuilder().InMenus().Build().ToBytes());

        var suspended = await NextAsync<ConnectionStateChanged>(enumerator);
        suspended.State.ShouldBe(ConnectionState.SessionSuspended);

        // 6. Loading a different session is unambiguous, so the previous one is closed out here.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Other Track", "Other Layout")
            .WithTicks(1000)
            .Build()
            .ToBytes());

        var sessionEnded = await NextAsync<SessionEnded>(enumerator);
        sessionEnded.SessionId.ShouldBe(sessionId);

        var backToConnected = await NextAsync<ConnectionStateChanged>(enumerator);
        backToConnected.State.ShouldBe(ConnectionState.Connected);
    }

    [Fact]
    public async Task InSessionMenu_SuspendsAndResumesTheSameSession()
    {
        // The bug this guards: RaceRoom sets game_in_menus for its in-session (ESC) menu, not only
        // for the main menu. Treating that as a session boundary ended the session and minted a new
        // id every time the driver paused, so one qualifying session was stored as six -- and every
        // lap completed while the menu was open was dropped, because restarting re-based the lap
        // counter against the game's current completed_laps.
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(100)
            .WithCompletedLaps(2)
            .Build()
            .ToBytes());

        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession
        var started = await NextAsync<SessionStarted>(enumerator);
        Guid sessionId = started.Session.SessionId;

        var firstSample = await NextAsync<TelemetrySampleReceived>(enumerator);
        firstSample.Sample.SequenceNumber.ShouldBe(0L);

        // The driver presses ESC. Everything else about the frame still describes the session.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .InSessionMenu()
            .WithTicks(100)
            .WithCompletedLaps(2)
            .Build()
            .ToBytes());

        var suspended = await NextAsync<ConnectionStateChanged>(enumerator);
        suspended.State.ShouldBe(ConnectionState.SessionSuspended);
        suspended.Reason.ShouldNotBeNull();

        // Back on track, with the game having credited a lap while the menu was open.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(160)
            .WithCompletedLaps(3)
            .WithPreviousLap(88.25f, prevLapValid: 1)
            .Build()
            .ToBytes());

        var resumed = await NextAsync<ConnectionStateChanged>(enumerator);
        resumed.State.ShouldBe(
            ConnectionState.InSession,
            "leaving a menu must resume the suspended session, not start a new one.");

        var afterResume = await NextAsync<TelemetrySampleReceived>(enumerator);
        afterResume.Sample.SessionId.ShouldBe(sessionId, "the resumed session must keep its id.");
        afterResume.Sample.SequenceNumber.ShouldBe(1L, "sequence numbering must continue across a suspension, not restart.");

        var lap = await NextAsync<LapCompleted>(enumerator);
        lap.Lap.SessionId.ShouldBe(sessionId);
        lap.Lap.LapNumber.ShouldBe(3, "a lap completed while the menu was open must still be reported on resume.");
        lap.Lap.LapTime.ShouldBe(TimeSpan.FromSeconds(88.25));
    }

    [Fact]
    public async Task Replay_SuspendsAndResumesTheSameSession()
    {
        // A replay is not a stale frame, it is a convincing lie: the block keeps publishing lap
        // times, pedal inputs and positions for the car being replayed, and nothing about the frame
        // says it already happened. Collected, it files replay laps in the archive as real ones and
        // puts a race engineer in front of a timing tower for a race that is already over.
        //
        // It is suspended rather than ended because RaceRoom can start a replay from the in-session
        // menu and return to the same session afterwards -- the same reason a menu suspends.
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(100)
            .WithCompletedLaps(2)
            .Build()
            .ToBytes());

        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession
        var started = await NextAsync<SessionStarted>(enumerator);
        Guid sessionId = started.Session.SessionId;

        var firstSample = await NextAsync<TelemetrySampleReceived>(enumerator);
        firstSample.Sample.SequenceNumber.ShouldBe(0L);

        // The driver watches a replay. The frame still looks like an on-track race session in every
        // other respect -- no menu, a real session type, a green phase.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .InReplay()
            .WithTicks(120)
            .WithCompletedLaps(2)
            .Build()
            .ToBytes());

        var suspended = await NextAsync<ConnectionStateChanged>(enumerator);
        suspended.State.ShouldBe(
            ConnectionState.SessionSuspended,
            "a replay must suspend the session rather than being collected as live driving.");
        suspended.Reason.ShouldNotBeNull();

        // Back to driving, with the game having credited a lap while the replay was open.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(160)
            .WithCompletedLaps(3)
            .WithPreviousLap(91.5f, prevLapValid: 1)
            .Build()
            .ToBytes());

        var resumed = await NextAsync<ConnectionStateChanged>(enumerator);
        resumed.State.ShouldBe(
            ConnectionState.InSession,
            "leaving a replay must resume the suspended session, not start a new one.");

        var afterResume = await NextAsync<TelemetrySampleReceived>(enumerator);
        afterResume.Sample.SessionId.ShouldBe(sessionId, "the resumed session must keep its id.");
        afterResume.Sample.SequenceNumber.ShouldBe(
            1L,
            "no sample may be published while a replay is running, so numbering continues from the last live frame.");

        var lap = await NextAsync<LapCompleted>(enumerator);
        lap.Lap.SessionId.ShouldBe(sessionId);
        lap.Lap.LapNumber.ShouldBe(3, "a lap completed while the replay was open must still be reported on resume.");
        lap.Lap.LapTime.ShouldBe(TimeSpan.FromSeconds(91.5));
    }

    [Fact]
    public async Task NoSamplesArePublishedWhileSuspended()
    {
        // A paused simulator republishes the same frame forever. Sampling it at the poll rate would
        // store thousands of identical rows describing a stationary car and, worse, make a pause
        // look like a stint of perfectly consistent laps to anything reading the telemetry back.
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(100)
            .Build()
            .ToBytes());

        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession
        await NextAsync<SessionStarted>(enumerator);
        await NextAsync<TelemetrySampleReceived>(enumerator);

        // game_paused rather than a menu: the other way a frame stops describing a moving car.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .Paused()
            .WithTicks(100)
            .Build()
            .ToBytes());

        var suspended = await NextAsync<ConnectionStateChanged>(enumerator);
        suspended.State.ShouldBe(ConnectionState.SessionSuspended);

        // Let the poll loop run over the frozen frame many times. The very next event must be the
        // resume, with nothing sampled in between.
        await Task.Delay(TimeSpan.FromMilliseconds(60), cts.Token);

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(220)
            .Build()
            .ToBytes());

        var next = await NextAsync<ConnectionStateChanged>(enumerator);
        next.State.ShouldBe(
            ConnectionState.InSession,
            "no TelemetrySampleReceived may be emitted between suspending and resuming.");
    }

    [Fact]
    public async Task RestartFromTheInSessionMenu_EndsTheSessionRatherThanResuming()
    {
        // Restarting from the ESC menu returns to the same track, layout, type and iteration -- the
        // session key is identical, so only the re-based tick counter distinguishes it from an
        // ordinary resume. Getting this wrong would silently append a second run's telemetry to the
        // first run's session.
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(900)
            .Build()
            .ToBytes());

        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession
        var started = await NextAsync<SessionStarted>(enumerator);
        await NextAsync<TelemetrySampleReceived>(enumerator);

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .InSessionMenu()
            .WithTicks(900)
            .Build()
            .ToBytes());

        await NextAsync<ConnectionStateChanged>(enumerator); // -> SessionSuspended

        // Resume, but with the tick counter re-based -- the driver chose "restart session".
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(0)
            .Build()
            .ToBytes());

        var ended = await NextAsync<SessionEnded>(enumerator);
        ended.SessionId.ShouldBe(started.Session.SessionId);

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected
        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession

        var restarted = await NextAsync<SessionStarted>(enumerator);
        restarted.Session.SessionId.ShouldNotBe(started.Session.SessionId);
    }

    [Fact]
    public async Task SessionIterationChange_EndsTheSessionEvenThoughEverythingElseMatches()
    {
        // Two qualifying sessions at the same track differ in nothing but session_iteration. Before
        // it joined the key, the only thing separating them was the trip through the menus in
        // between -- which is exactly the coupling that made a pause look like a new session.
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithSessionType(R3ESessionType.Qualify)
            .WithSessionIteration(1)
            .WithTicks(100)
            .Build()
            .ToBytes());

        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession
        var first = await NextAsync<SessionStarted>(enumerator);
        await NextAsync<TelemetrySampleReceived>(enumerator);

        // Same track, same layout, same type, higher tick count -- only the iteration moves on.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithSessionType(R3ESessionType.Qualify)
            .WithSessionIteration(2)
            .WithTicks(200)
            .Build()
            .ToBytes());

        var ended = await NextAsync<SessionEnded>(enumerator);
        ended.SessionId.ShouldBe(first.Session.SessionId);

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected
        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession

        var second = await NextAsync<SessionStarted>(enumerator);
        second.Session.SessionId.ShouldNotBe(first.Session.SessionId);
    }

    [Fact]
    public async Task ASessionKeyThatFlickersForLessThanTheDebounce_NeverStartsASession()
    {
        // The game publishes the next session's type while it is still loading, which produced real
        // database rows lasting a fraction of a second and holding a handful of samples.
        var options = FastOptions with { SessionStartDebounce = TimeSpan.FromMilliseconds(200) };

        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(options, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected

        // A brief glimpse of a session that is still loading...
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Flicker Track", "Flicker Layout")
            .WithTicks(10)
            .Build()
            .ToBytes());

        await Task.Delay(TimeSpan.FromMilliseconds(40), cts.Token);

        // ...gone again before the debounce elapses.
        view.SetFrame(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await Task.Delay(TimeSpan.FromMilliseconds(40), cts.Token);

        // The session that actually loads is the one that must be recorded.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Real Track", "Real Layout")
            .WithTicks(500)
            .Build()
            .ToBytes());

        var toInSession = await NextAsync<ConnectionStateChanged>(enumerator);
        toInSession.State.ShouldBe(ConnectionState.InSession);

        var started = await NextAsync<SessionStarted>(enumerator);
        started.Session.TrackName.ShouldBe(
            "Real Track",
            "the half-loaded session must never have been started -- only the one whose key settled.");
    }

    [Fact]
    public async Task ASuspensionOutlastingMaxSuspendDuration_EndsTheSession()
    {
        // Suspension keeps a session open across a pause, so it needs its own upper bound: quitting
        // to the main menu and walking away must not leave a session open indefinitely. This is
        // also the only liveness check that can run while suspended, since a paused game freezes
        // the tick counter that StaleFrameTimeout watches.
        var options = FastOptions with { MaxSuspendDuration = TimeSpan.FromMilliseconds(150) };

        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        await using var source = new RaceRoomTelemetrySource(options, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextAsync<ConnectionStateChanged>(enumerator); // -> Connected

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Track", "Layout")
            .WithTicks(100)
            .Build()
            .ToBytes());

        await NextAsync<ConnectionStateChanged>(enumerator); // -> InSession
        var started = await NextAsync<SessionStarted>(enumerator);
        await NextAsync<TelemetrySampleReceived>(enumerator);

        view.SetFrame(new R3ESharedRawBuilder().InMenus().Build().ToBytes());

        var suspended = await NextAsync<ConnectionStateChanged>(enumerator);
        suspended.State.ShouldBe(ConnectionState.SessionSuspended);

        var ended = await NextAsync<SessionEnded>(enumerator);
        ended.SessionId.ShouldBe(started.Session.SessionId);

        var connected = await NextAsync<ConnectionStateChanged>(enumerator);
        connected.State.ShouldBe(ConnectionState.Connected);
        connected.Reason.ShouldNotBeNull().ShouldContain("suspended");
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

    /// <summary>
    /// An unrecognised failure to open the block must not end the run. Faulted is reached from
    /// TryConnect's catch-all, which means "we do not know what this was" rather than "this cannot
    /// get better", and the errors that land there in practice — a transient permissions or mapping
    /// error while the game is starting — clear on their own. When Faulted was terminal the loop
    /// spun at the full poll rate producing nothing, so a fault a retry would have cleared instead
    /// cost the whole session and needed the collector restarted by hand.
    /// </summary>
    [Fact]
    public async Task UnexpectedOpenFailure_FaultsButKeepsRetrying()
    {
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().Build().ToBytes());
        int viewsOpened = 0;

        // Fails once with an exception TryConnect does not recognise, then behaves.
        await using var source = new RaceRoomTelemetrySource(FastOptions, () =>
        {
            viewsOpened++;
            return viewsOpened == 1
                ? throw new InvalidOperationException("something nobody anticipated")
                : view;
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        var faulted = await NextAsync<ConnectionStateChanged>(enumerator);
        faulted.State.ShouldBe(ConnectionState.Faulted);
        faulted.Reason.ShouldNotBeNull().ShouldContain("something nobody anticipated");

        // The point of the test: the next tick retries instead of sitting dead forever.
        var connected = await NextAsync<ConnectionStateChanged>(enumerator);
        connected.State.ShouldBe(ConnectionState.Connected);
        viewsOpened.ShouldBe(2, "a faulted source must attempt to reopen the shared memory block.");
    }
}
