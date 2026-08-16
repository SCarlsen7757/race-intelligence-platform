using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Telemetry;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Drives the real state machine and asserts when <see cref="StandingsUpdated"/> is and is not
/// emitted. The gating matters more than the contents: standings read from a frozen menu frame
/// describe the moment the driver pressed escape, and republishing them would show a race still
/// unfolding while nothing is moving — the live-view form of the session-fragmentation bug that
/// <see cref="ConnectionState.SessionSuspended"/> exists to prevent.
/// </summary>
public class RaceRoomTelemetrySourceStandingsTests
{
    private static readonly RaceRoomConnectorOptions FastOptions = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(2),
        ReconnectDelay = TimeSpan.FromMilliseconds(5),
        StaleFrameTimeout = TimeSpan.FromMinutes(5),
        MaxSuspendDuration = TimeSpan.FromMinutes(5),
        SessionStartDebounce = TimeSpan.Zero,

        // Read the field on every poll, so a test does not have to wait out a rate limit to see
        // the next snapshot. The default rate is a bandwidth decision, not a behavioural one.
        StandingsInterval = TimeSpan.Zero,
    };

    private static R3EDriverData[] Field() =>
    [
        new R3EDriverDataBuilder().WithName("Kimi").WithUserId(4242).WithPlace(1, 1).WithLapProgress(3, 0.5f, 2).Build(),
        new R3EDriverDataBuilder().WithName("Rival").WithUserId(99).WithPlace(2, 2).WithLapProgress(3, 0.4f, 2).Build(),
    ];

    private static byte[] OnTrackFrame(int ticks, int completedLaps = 0) =>
        new R3ESharedRawBuilder()
            .InRaceSession("Test Track", "Test Layout")
            .WithTicks(ticks)
            .WithCompletedLaps(completedLaps)
            .Configure((ref R3ESharedRaw r) => r.VehicleInfo.UserId = 4242)
            .WithDrivers(Field())
            .BuildBytes();

    private static byte[] SuspendedFrame(int ticks, int completedLaps = 0) =>
        new R3ESharedRawBuilder()
            .InRaceSession("Test Track", "Test Layout")
            .InSessionMenu()
            .WithTicks(ticks)
            .WithCompletedLaps(completedLaps)
            .Configure((ref R3ESharedRaw r) => r.VehicleInfo.UserId = 4242)
            .WithDrivers(Field())
            .BuildBytes();

    /// <summary>Advances until the next event of the requested type, tolerating samples in between.</summary>
    private static async Task<TEvent> NextSkippingSamplesAsync<TEvent>(IAsyncEnumerator<TelemetryEvent> enumerator)
        where TEvent : TelemetryEvent
    {
        while (await enumerator.MoveNextAsync())
        {
            if (enumerator.Current is TEvent match)
            {
                return match;
            }
        }

        throw new ShouldAssertException(
            $"the event stream ended before a {typeof(TEvent).Name} was produced.");
    }

    [Fact]
    public async Task An_on_track_session_publishes_the_whole_field()
    {
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().BuildBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextSkippingSamplesAsync<ConnectionStateChanged>(enumerator);
        view.SetFrame(OnTrackFrame(ticks: 100));

        var standings = await NextSkippingSamplesAsync<StandingsUpdated>(enumerator);

        standings.Standings.Drivers.Count.ShouldBe(2);
        standings.Standings.Drivers.Select(d => d.DisplayName).ShouldBe(["Kimi", "Rival"]);
        standings.Standings.Drivers[0].Position.ShouldBe(1);

        // The local car is identified, which is what marks the row the publishing machine has rich
        // telemetry for.
        standings.Standings.LocalSimDriverId.ShouldBe("4242");
        standings.Standings.SessionId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task No_standings_are_published_while_the_session_is_suspended()
    {
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().BuildBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextSkippingSamplesAsync<ConnectionStateChanged>(enumerator);
        view.SetFrame(OnTrackFrame(ticks: 100));
        var firstStandings = await NextSkippingSamplesAsync<StandingsUpdated>(enumerator);
        var sessionId = firstStandings.Standings.SessionId;

        // The driver opens the in-session menu. The block keeps serving the same field forever.
        view.SetFrame(SuspendedFrame(ticks: 101));

        var suspended = await NextSkippingSamplesAsync<ConnectionStateChanged>(enumerator);
        suspended.State.ShouldBe(ConnectionState.SessionSuspended);

        // Back on track. Every event between the suspension and the resume must be asserted
        // individually — draining with a "skip until you find X" helper would discard exactly the
        // StandingsUpdated this test exists to catch and pass whether or not the bug is present.
        view.SetFrame(OnTrackFrame(ticks: 102));

        ConnectionStateChanged resumed;
        while (true)
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue("the stream ended before the session resumed.");

            enumerator.Current.ShouldNotBeOfType<StandingsUpdated>(
                "no standings may be published while the session is suspended — a frozen menu frame "
                + "describes the moment the driver pressed escape, not the moment it was read.");

            if (enumerator.Current is ConnectionStateChanged transition)
            {
                resumed = transition;
                break;
            }
        }

        resumed.State.ShouldBe(ConnectionState.InSession);

        // Same session, not a new one — standings must not re-key onto a fresh session id.
        var afterResume = await NextSkippingSamplesAsync<StandingsUpdated>(enumerator);
        afterResume.Standings.SessionId.ShouldBe(sessionId);
    }

    [Fact]
    public async Task Standings_can_be_switched_off_entirely()
    {
        var options = FastOptions with { StandingsInterval = Timeout.InfiniteTimeSpan };

        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().BuildBytes());
        await using var source = new RaceRoomTelemetrySource(options, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextSkippingSamplesAsync<ConnectionStateChanged>(enumerator);
        view.SetFrame(OnTrackFrame(ticks: 100));

        // Let the session actually start on the lap-zero frame before advancing the lap counter —
        // setting both frames back to back would have the session begin on lap 1 and never see it
        // complete.
        await NextSkippingSamplesAsync<SessionStarted>(enumerator);
        view.SetFrame(OnTrackFrame(ticks: 101, completedLaps: 1));

        // Samples keep flowing; a lap completing proves the state machine ran many ticks with a
        // populated field and never produced standings.
        await AssertNoStandingsBeforeLapCompletesAsync(enumerator);
    }

    [Fact]
    public async Task A_session_with_no_field_reported_publishes_nothing_rather_than_an_empty_tower()
    {
        var view = new FakeSharedMemoryView(new R3ESharedRawBuilder().InMenus().BuildBytes());
        await using var source = new RaceRoomTelemetrySource(FastOptions, () => view);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await using var enumerator = source.ReadAllAsync(cts.Token).GetAsyncEnumerator();

        await NextSkippingSamplesAsync<ConnectionStateChanged>(enumerator);

        // An on-track frame carrying no driver array at all — num_cars is zero.
        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Test Track", "Test Layout")
            .WithTicks(100)
            .BuildBytes());

        await NextSkippingSamplesAsync<SessionStarted>(enumerator);

        view.SetFrame(new R3ESharedRawBuilder()
            .InRaceSession("Test Track", "Test Layout")
            .WithTicks(101)
            .WithCompletedLaps(1)
            .BuildBytes());

        await AssertNoStandingsBeforeLapCompletesAsync(enumerator);
    }

    /// <summary>
    /// Drains events until a lap completes, failing on any <see cref="StandingsUpdated"/> along the
    /// way. A completed lap is the marker for "the state machine has run a good many ticks", which
    /// is what makes the absence of standings meaningful rather than merely early.
    /// </summary>
    private static async Task AssertNoStandingsBeforeLapCompletesAsync(IAsyncEnumerator<TelemetryEvent> enumerator)
    {
        while (await enumerator.MoveNextAsync())
        {
            enumerator.Current.ShouldNotBeOfType<StandingsUpdated>();
            if (enumerator.Current is LapCompleted)
            {
                return;
            }
        }

        throw new ShouldAssertException("the event stream ended before a lap completed.");
    }
}
