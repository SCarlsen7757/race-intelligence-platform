using Microsoft.Extensions.Logging.Abstractions;
using RaceIntelligence.Collector.Live;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Collector.Tests.Support;
using RaceIntelligence.Live.Contracts.Publish;
using Shouldly;

namespace RaceIntelligence.Collector.Tests.Live;

/// <summary>
/// Covers the hand-off between the collect loop and the publishing socket. The behaviour that
/// matters is asymmetric on purpose: control messages queue because each one says something no
/// later message repeats, while data frames keep only the newest because a live value is worthless
/// the moment a fresher one exists.
/// </summary>
public sealed class LiveOutboxTests
{
    private static LiveOutbox CreateOutbox() => new(NullLogger<LiveOutbox>.Instance);

    private static SessionInfo Session(Guid? sessionId = null) => SessionInfoFactory.Create(sessionId);

    private static SessionStandings Standings(Guid sessionId, int driverCount) => new()
    {
        SessionId = sessionId,
        CapturedAtUtc = DateTimeOffset.UnixEpoch,
        Drivers = [.. Enumerable.Range(0, driverCount).Select(i => new DriverStanding
        {
            DisplayName = $"Driver {i}",
            SimDriverId = (i + 1).ToString(),
        })],
    };

    [Fact]
    public void A_session_announcement_is_delivered()
    {
        var outbox = CreateOutbox();
        var session = Session();

        outbox.PublishSessionStarted(session, "fingerprint", rosterSize: 3);

        var frame = outbox.TryRead().ShouldBeOfType<LiveSessionFrame>();
        frame.SessionId.ShouldBe(session.SessionId);
        frame.RosterFingerprint.ShouldBe("fingerprint");
        frame.RosterSize.ShouldBe(3);
    }

    /// <summary>
    /// The reason the two data streams keep one slot each rather than sharing a queue: a socket that
    /// stalls for two seconds must deliver the current state when it recovers, not two seconds of
    /// backlog describing where the cars used to be.
    /// </summary>
    [Fact]
    public void Only_the_newest_data_frame_of_each_kind_survives()
    {
        var outbox = CreateOutbox();
        var sessionId = Guid.NewGuid();

        outbox.PublishStandings(Standings(sessionId, driverCount: 1));
        outbox.PublishStandings(Standings(sessionId, driverCount: 2));
        outbox.PublishStandings(Standings(sessionId, driverCount: 3));

        outbox.TryRead().ShouldBeOfType<LiveStandingsFrame>().Drivers.Count.ShouldBe(3);
        outbox.TryRead().ShouldBeNull();

        outbox.DroppedFrames.Standings.ShouldBe(2);
    }

    [Fact]
    public void Self_frames_conflate_the_same_way()
    {
        var outbox = CreateOutbox();

        outbox.PublishSelf(TelemetrySampleFactory.Create(Guid.NewGuid(), sequenceNumber: 1), "4242");
        outbox.PublishSelf(TelemetrySampleFactory.Create(Guid.NewGuid(), sequenceNumber: 2), "4242");

        outbox.TryRead().ShouldBeOfType<LiveSelfFrame>().SequenceNumber.ShouldBe(2);
        outbox.TryRead().ShouldBeNull();
        outbox.DroppedFrames.Self.ShouldBe(1);
    }

    /// <summary>
    /// Goodbyes queue rather than replacing each other. Each names a different session, so
    /// conflating them would leave a finished room sitting in the dashboard until it timed out.
    /// </summary>
    [Fact]
    public void Goodbyes_queue_rather_than_replacing_each_other()
    {
        var outbox = CreateOutbox();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        outbox.PublishSessionEnded(first, reason: null);
        outbox.PublishSessionEnded(second, reason: null);

        outbox.TryRead().ShouldBeOfType<LiveGoodbye>().SessionId.ShouldBe(first);
        outbox.TryRead().ShouldBeOfType<LiveGoodbye>().SessionId.ShouldBe(second);
    }

    /// <summary>
    /// The session announcement is a sticky slot rather than a queued message, so a session that
    /// starts and finishes before the publisher drains is never announced — only the one now
    /// running is.
    /// </summary>
    /// <remarks>
    /// A deliberate trade, and the cheaper side of it. The hub receives a goodbye for a session it
    /// was never told about, which it ignores; the alternative is announcing a session that ended
    /// before anyone could have watched it, and carrying two sources of truth for which session is
    /// current in order to do so.
    /// </remarks>
    [Fact]
    public void Only_the_session_now_running_is_announced()
    {
        var outbox = CreateOutbox();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        outbox.PublishSessionStarted(Session(first), "a", 1);
        outbox.PublishSessionEnded(first, reason: null);
        outbox.PublishSessionStarted(Session(second), "b", 2);

        outbox.TryRead().ShouldBeOfType<LiveGoodbye>().SessionId.ShouldBe(first);
        outbox.TryRead().ShouldBeOfType<LiveSessionFrame>().SessionId.ShouldBe(second);
        outbox.TryRead().ShouldBeNull();
    }

    /// <summary>
    /// A session announcement has to reach the hub before the data it describes, or the hub receives
    /// standings for a session it cannot place.
    /// </summary>
    [Fact]
    public void Control_is_delivered_ahead_of_data_regardless_of_publish_order()
    {
        var outbox = CreateOutbox();
        var sessionId = Guid.NewGuid();

        outbox.PublishSelf(TelemetrySampleFactory.Create(Guid.NewGuid(), sequenceNumber: 1), "4242");
        outbox.PublishStandings(Standings(sessionId, driverCount: 2));
        outbox.PublishSessionStarted(Session(sessionId), "a", 2);

        outbox.TryRead().ShouldBeOfType<LiveSessionFrame>();
    }

    /// <summary>
    /// Standings ahead of the local car's channels. Self frames arrive at ten times the rate, so
    /// preferring them would let a backed-up socket starve the timing tower entirely — whereas a
    /// self frame skipped now is replaced within milliseconds.
    /// </summary>
    [Fact]
    public void Standings_are_delivered_ahead_of_the_local_cars_channels()
    {
        var outbox = CreateOutbox();

        outbox.PublishSelf(TelemetrySampleFactory.Create(Guid.NewGuid(), sequenceNumber: 1), "4242");
        outbox.PublishStandings(Standings(Guid.NewGuid(), driverCount: 2));

        outbox.TryRead().ShouldBeOfType<LiveStandingsFrame>();
        outbox.TryRead().ShouldBeOfType<LiveSelfFrame>();
    }

    /// <summary>
    /// The session is over, so a snapshot still waiting describes a race that is no longer running.
    /// Letting it overtake the goodbye would show a finished session still unfolding.
    /// </summary>
    [Fact]
    public void Ending_a_session_discards_data_frames_still_waiting()
    {
        var outbox = CreateOutbox();
        var sessionId = Guid.NewGuid();

        outbox.PublishSessionStarted(Session(sessionId), "a", 2);
        outbox.TryRead().ShouldBeOfType<LiveSessionFrame>();

        outbox.PublishStandings(Standings(sessionId, driverCount: 2));
        outbox.PublishSelf(TelemetrySampleFactory.Create(Guid.NewGuid(), sequenceNumber: 1), "4242");
        outbox.PublishSessionEnded(sessionId, "session ended");

        outbox.TryRead().ShouldBeOfType<LiveGoodbye>();
        outbox.TryRead().ShouldBeNull();
    }

    /// <summary>
    /// The hub learns which session a client is publishing from the announcement carried on that
    /// socket, and a dropped socket takes that knowledge with it. The announcement therefore has to
    /// outlive the connection that carried it, not just the moment the session started.
    /// </summary>
    [Fact]
    public void The_current_session_stays_available_for_re_announcement_after_it_has_been_read()
    {
        var outbox = CreateOutbox();
        var sessionId = Guid.NewGuid();

        outbox.PublishSessionStarted(Session(sessionId), "a", 2);
        outbox.TryRead().ShouldBeOfType<LiveSessionFrame>();

        // Delivered once, and not repeated — a connection that already has it is not told twice.
        outbox.TryRead().ShouldBeNull();

        // But a new connection asks for it again, and gets it.
        outbox.RequireSessionAnnouncement();
        outbox.TryRead().ShouldBeOfType<LiveSessionFrame>().SessionId.ShouldBe(sessionId);
    }

    /// <summary>
    /// Between sessions there is nothing to announce, and a reconnect must not resurrect the
    /// session that just finished.
    /// </summary>
    [Fact]
    public void A_reconnect_between_sessions_announces_nothing()
    {
        var outbox = CreateOutbox();
        var sessionId = Guid.NewGuid();

        outbox.PublishSessionStarted(Session(sessionId), "a", 2);
        outbox.PublishSessionEnded(sessionId, reason: null);
        outbox.TryRead().ShouldBeOfType<LiveGoodbye>();

        outbox.RequireSessionAnnouncement();
        outbox.TryRead().ShouldBeNull();
    }

    [Fact]
    public void Ending_a_session_stops_it_being_re_announced()
    {
        var outbox = CreateOutbox();
        var sessionId = Guid.NewGuid();

        outbox.PublishSessionStarted(Session(sessionId), "a", 2);
        outbox.PublishSessionEnded(sessionId, reason: null);

        outbox.CurrentSession.ShouldBeNull();
    }

    /// <summary>
    /// The next session can start before the previous one's end has been processed. Clearing the
    /// wrong one would leave the hub receiving standings for a session it was never told about —
    /// exactly the failure the sticky announcement exists to prevent.
    /// </summary>
    [Fact]
    public void Ending_an_older_session_does_not_clear_the_one_now_running()
    {
        var outbox = CreateOutbox();
        var previous = Guid.NewGuid();
        var current = Guid.NewGuid();

        outbox.PublishSessionStarted(Session(previous), "a", 2);
        outbox.PublishSessionStarted(Session(current), "b", 2);
        outbox.PublishSessionEnded(previous, reason: null);

        outbox.CurrentSession.ShouldNotBeNull();
        outbox.CurrentSession!.SessionId.ShouldBe(current);
    }

    [Fact]
    public async Task ReadAsync_waits_until_something_is_published()
    {
        var outbox = CreateOutbox();
        var cancellationToken = TestContext.Current.CancellationToken;

        var pending = outbox.ReadAsync(cancellationToken);
        pending.IsCompleted.ShouldBeFalse("nothing has been published yet.");

        outbox.PublishSessionStarted(Session(), "a", 1);

        (await pending).ShouldBeOfType<LiveSessionFrame>();
    }

    [Fact]
    public async Task ReadAsync_wakes_for_a_conflated_data_frame_too()
    {
        var outbox = CreateOutbox();
        var cancellationToken = TestContext.Current.CancellationToken;

        var pending = outbox.ReadAsync(cancellationToken);
        outbox.PublishStandings(Standings(Guid.NewGuid(), driverCount: 2));

        (await pending).ShouldBeOfType<LiveStandingsFrame>();
    }

    /// <summary>
    /// The one absolute requirement: the collect loop that feeds this also feeds permanent storage,
    /// so a dead or slow hub must never be able to park it.
    /// </summary>
    [Fact]
    public void Publishing_never_blocks_even_when_nothing_is_reading()
    {
        var outbox = CreateOutbox();
        var sessionId = Guid.NewGuid();

        // Far more than the control channel's capacity, with nothing draining it.
        for (int i = 0; i < 10_000; i++)
        {
            outbox.PublishSessionStarted(Session(sessionId), "a", 1);
            outbox.PublishStandings(Standings(sessionId, driverCount: 2));
            outbox.PublishSelf(TelemetrySampleFactory.Create(Guid.NewGuid(), sequenceNumber: i), "4242");
        }

        // Reaching here at all is the assertion; it returned rather than parking the caller.
        outbox.TryRead().ShouldNotBeNull();
    }
}
