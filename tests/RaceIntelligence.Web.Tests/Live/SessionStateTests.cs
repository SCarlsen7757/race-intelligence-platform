using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Live.Contracts.View;
using RaceIntelligence.Web.Live;
using RaceIntelligence.Web.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Live;

/// <summary>
/// Covers what the hub says about a session rather than about the cars in it: the lap length a
/// dashboard derives average speeds from, and the mandatory pit window a strategist reads.
/// </summary>
/// <remarks>
/// Both share one message and one discipline — it is sent when it <i>changes</i>, and answered in
/// full to anyone who asks. Standings arrive ten times a second and this changes twice in a race, so
/// getting that wrong turns a low-rate message into the second-busiest one on the wire; getting the
/// other half wrong leaves a viewer who joined mid-race never told the window is open.
/// </remarks>
public sealed class SessionStateTests
{
    /// <summary>Announces a room and returns everything a test needs to drive it.</summary>
    private static (LiveHubFixture Hub, LiveRoom Room, LivePublisherIdentity Identity, Guid SessionId) Room()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "77");

        return (hub, room, identity, Guid.NewGuid());
    }

    /// <summary>Every session-state message a viewer has been handed, oldest first.</summary>
    private static List<SessionStateMessage> SessionStatesFor(LiveViewer viewer)
    {
        var received = new List<SessionStateMessage>();

        while (viewer.Queue.TryRead() is { } message)
        {
            if (message is SessionStateMessage state)
            {
                received.Add(state);
            }
        }

        return received;
    }

    /// <summary>
    /// The lap length is the whole reason an average speed is computable in the browser. It is
    /// announced with the session, so it must be known without waiting for a standings frame — a room
    /// whose publisher went quiet before sending one would otherwise never carry it.
    /// </summary>
    [Fact]
    public void The_layout_length_is_known_from_the_session_announcement_alone()
    {
        var (_, room, _, _) = Room();

        room.SessionState().LayoutLengthMeters.ShouldBe(7004f);
    }

    /// <summary>
    /// A lap does not stop being 7004 metres long when somebody closes their game. Losing the length
    /// on a disconnect would blank every average speed in the tower mid-race.
    /// </summary>
    [Fact]
    public void The_layout_length_survives_the_publisher_that_reported_it_disconnecting()
    {
        var (hub, room, identity, sessionId) = Room();
        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(sessionId));

        hub.Rooms.RemovePublisher(identity.ClientId);

        room.SessionState().LayoutLengthMeters.ShouldBe(7004f);
    }

    /// <summary>
    /// The property that keeps this a low-rate message. Ten standings frames a second must produce
    /// one session-state message, not ten.
    /// </summary>
    [Fact]
    public void An_unchanged_session_state_is_not_re_sent()
    {
        var (hub, room, identity, sessionId) = Room();
        var viewer = hub.AddViewer(room.RoomId);

        for (int i = 0; i < 10; i++)
        {
            hub.Rooms.ApplyStandings(
                identity.ClientId,
                LiveDtoFactory.StandingsFrameWithWindow(sessionId, PitWindowStatus.Closed));
        }

        // One, for the window arriving — not one per frame. The announcement's lap length changed
        // the state before this viewer existed, which is exactly why a viewer is answered from
        // `SessionState()` on subscribing rather than from the broadcast alone.
        SessionStatesFor(viewer).ShouldHaveSingleItem();
    }

    /// <summary>The change everybody in the session is waiting for has to reach them.</summary>
    [Fact]
    public void A_window_opening_is_broadcast()
    {
        var (hub, room, identity, sessionId) = Room();
        hub.Rooms.ApplyStandings(
            identity.ClientId,
            LiveDtoFactory.StandingsFrameWithWindow(sessionId, PitWindowStatus.Closed));

        var viewer = hub.AddViewer(room.RoomId);

        hub.Rooms.ApplyStandings(
            identity.ClientId,
            LiveDtoFactory.StandingsFrameWithWindow(sessionId, PitWindowStatus.Open));

        var window = SessionStatesFor(viewer).ShouldHaveSingleItem().PitWindow.ShouldNotBeNull();
        window.Status.ShouldBe(PitWindowStatusView.Open);
        window.Start.ShouldBe(12);
        window.End.ShouldBe(20);
        window.Unit.ShouldBe(PitWindowUnitView.Laps);
    }

    /// <summary>
    /// The case a change-only broadcast cannot serve on its own: the window opened ten minutes
    /// before this viewer arrived, so there is no change left for them to be told about.
    /// </summary>
    [Fact]
    public void A_viewer_joining_after_the_window_opened_is_still_told_it_is_open()
    {
        var (hub, room, identity, sessionId) = Room();
        hub.Rooms.ApplyStandings(
            identity.ClientId,
            LiveDtoFactory.StandingsFrameWithWindow(sessionId, PitWindowStatus.Open));

        room.SessionState().PitWindow.ShouldNotBeNull().Status.ShouldBe(PitWindowStatusView.Open);
    }

    /// <summary>
    /// Every practice session, and most races, have no mandatory stop. Those must produce no window
    /// at all rather than one the dashboard has to know to hide.
    /// </summary>
    [Theory]
    [InlineData(PitWindowStatus.Unavailable)]
    [InlineData(PitWindowStatus.Disabled)]
    public void A_session_with_no_mandatory_stop_carries_no_window(PitWindowStatus status)
    {
        var (hub, room, identity, sessionId) = Room();

        hub.Rooms.ApplyStandings(
            identity.ClientId,
            LiveDtoFactory.StandingsFrameWithWindow(sessionId, status));

        room.SessionState().PitWindow.ShouldBeNull();
    }

    /// <summary>
    /// A viewer watching one room must never be handed another's session state — the banner shows no
    /// room id, so nothing on screen would give the mistake away.
    /// </summary>
    [Fact]
    public void A_viewer_watching_another_room_is_not_offered_this_ones_state()
    {
        var (hub, room, identity, sessionId) = Room();
        var elsewhere = hub.AddViewer("some-other-room");

        hub.Rooms.ApplyStandings(
            identity.ClientId,
            LiveDtoFactory.StandingsFrameWithWindow(sessionId, PitWindowStatus.Open));

        room.RoomId.ShouldNotBe("some-other-room");
        SessionStatesFor(elsewhere).ShouldBeEmpty();
    }
}
