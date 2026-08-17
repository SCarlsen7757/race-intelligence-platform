using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;
using RaceIntelligence.Live.Contracts;
using RaceIntelligence.Live.Contracts.Mapping;
using RaceIntelligence.Live.Contracts.Publish;

namespace RaceIntelligence.Collector.Plugins.Live;

/// <summary>
/// The live hand-off: reliable for control messages, conflating for data.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of message travel this path and they need opposite treatment, which is why this is not
/// simply a bounded channel with <see cref="BoundedChannelFullMode.DropOldest"/>.
/// </para>
/// <para>
/// <b>Control messages</b> — a session starting or ending — are rare, and each one means something
/// no later message repeats. Dropping a session announcement would leave the hub with a stream of
/// standings for a session it has never heard of. They queue.
/// </para>
/// <para>
/// <b>Data frames</b> — standings and the local car's channels — are produced continuously and are
/// worthless the moment a newer one exists. Each kind keeps exactly one slot: a new frame overwrites
/// whatever is waiting. So a socket that stalls for two seconds does not deliver two seconds of
/// backlog when it recovers; it delivers the current state, which is the only thing a race engineer
/// wanted.
/// </para>
/// <para>
/// Nothing here blocks the caller. The collect loop is the same loop that drives the archive path,
/// and a slow or dead hub must never be able to interfere with permanent storage.
/// </para>
/// </remarks>
public sealed class LiveOutbox : ILiveOutbox
{
    /// <summary>
    /// How many control messages may queue before the oldest are dropped.
    /// </summary>
    /// <remarks>
    /// Generous relative to the real rate — a session start and end per session, so single digits
    /// per hour. It is a backstop against an unbounded queue while the hub is unreachable for a long
    /// time, not a limit anything is expected to reach.
    /// </remarks>
    private const int ControlCapacity = 64;

    private readonly Channel<LivePublisherMessage> _control =
        Channel.CreateBounded<LivePublisherMessage>(new BoundedChannelOptions(ControlCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });

    /// <summary>
    /// A one-slot wake signal. <see cref="BoundedChannelFullMode.DropWrite"/> makes
    /// <c>TryWrite</c> a safe no-op when a wake is already pending, which is what lets a publisher
    /// signal from the collect loop without ever throwing or blocking.
    /// </summary>
    private readonly Channel<byte> _wake =
        Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private readonly ILogger<LiveOutbox> _logger;

    private LiveStandingsFrame? _latestStandings;
    private LiveSelfFrame? _latestSelf;
    private LiveExtrasFrame? _latestExtras;
    private LiveSessionFrame? _currentSession;

    /// <summary>
    /// 1 when the current session still needs announcing on this connection. An int rather than a
    /// bool so it can be exchanged atomically — the collect loop sets it and the publisher clears
    /// it, on different threads.
    /// </summary>
    private int _sessionAnnouncementPending;

    private long _droppedStandings;
    private long _droppedSelf;

    public LiveOutbox(ILogger<LiveOutbox> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <summary>Data frames superseded before they could be sent — how far behind the socket is running.</summary>
    public (long Standings, long Self) DroppedFrames =>
        (Interlocked.Read(ref _droppedStandings), Interlocked.Read(ref _droppedSelf));

    /// <summary>
    /// The session currently being published, or <see langword="null"/> between sessions.
    /// </summary>
    /// <remarks>
    /// Sticky rather than consumed. A hub learns which session a client is publishing from the
    /// announcement carried on that socket, and a socket that drops takes the hub's knowledge of it
    /// with it — so a reconnect mid-race has to say it again. Delivering it once at session start is
    /// not enough; it has to outlive the connection that carried it. See
    /// <see cref="RequireSessionAnnouncement"/>.
    /// </remarks>
    public LiveSessionFrame? CurrentSession => Volatile.Read(ref _currentSession);

    /// <summary>
    /// Queues the current session to be announced again, if there is one.
    /// </summary>
    /// <remarks>
    /// Called by the publisher when a new connection is established. Re-announcing is expressed as
    /// a request to this outbox rather than as a send the publisher performs itself, so the
    /// announcement travels the one ordered path every other message takes — a publisher that sent
    /// it directly would race the queue and could deliver it after standings it is supposed to
    /// precede.
    /// </remarks>
    public void RequireSessionAnnouncement()
    {
        if (Volatile.Read(ref _currentSession) is not null)
        {
            Volatile.Write(ref _sessionAnnouncementPending, 1);
            Wake();
        }
    }

    /// <inheritdoc />
    public void PublishSessionStarted(SessionInfo session, string rosterFingerprint, int rosterSize)
    {
        ArgumentNullException.ThrowIfNull(session);

        var frame = new LiveSessionFrame(
            session.SessionId,
            session.GameVersion.Game.Key,
            session.TrackName ?? string.Empty,
            session.LayoutName ?? string.Empty,
            session.LayoutLengthMeters,
            (int)session.SessionType,
            // SessionInfo does not carry the simulator's session iteration; the hub keys rooms on
            // what it is given and confirms with the roster fingerprint, which is the stronger of
            // the two signals anyway.
            SessionIteration: 0,
            session.StartedAtUtc,
            session.PlayerName,
            session.SimDriverId,
            rosterFingerprint,
            rosterSize,
            session.SimSlotId);

        // Not queued: the session is a sticky slot, delivered by TryRead when an announcement is
        // outstanding. Queuing it as well would send it twice on the first connection — once from
        // the queue and once from the reconnect re-announce — and would leave two sources of truth
        // for which session is current.
        Volatile.Write(ref _currentSession, frame);
        Volatile.Write(ref _sessionAnnouncementPending, 1);
        Wake();
    }

    /// <inheritdoc />
    public void PublishStandings(SessionStandings standings)
    {
        ArgumentNullException.ThrowIfNull(standings);

        var frame = LiveStandingsContractMapper.ToFrame(standings);
        if (Interlocked.Exchange(ref _latestStandings, frame) is not null)
        {
            Interlocked.Increment(ref _droppedStandings);
        }

        Wake();
    }

    /// <inheritdoc />
    public void PublishSelf(TelemetrySample sample, string? simDriverId)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var frame = LiveStandingsContractMapper.ToSelfFrame(sample, simDriverId);
        if (Interlocked.Exchange(ref _latestSelf, frame) is not null)
        {
            Interlocked.Increment(ref _droppedSelf);
        }

        Wake();
    }

    /// <inheritdoc />
    /// <remarks>
    /// No drop counter, unlike standings and self. Those count frames at a rate worth knowing you
    /// are behind on; extras arrive about once a second, so a superseded one says nothing a
    /// backed-up socket has not already said through the other two.
    /// </remarks>
    public void PublishExtras(Guid sessionId, string extrasJson, DateTimeOffset capturedAtUtc, string? simDriverId)
    {
        ArgumentNullException.ThrowIfNull(extrasJson);

        Interlocked.Exchange(
            ref _latestExtras,
            new LiveExtrasFrame(sessionId, simDriverId, capturedAtUtc, extrasJson));

        Wake();
    }

    /// <inheritdoc />
    public void PublishSessionEnded(Guid sessionId, string? reason)
    {
        // The session is over, so any data frame still waiting describes a session the hub is about
        // to be told has ended. Clearing them keeps the goodbye from being overtaken by a stale
        // snapshot of a race that is no longer running.
        Interlocked.Exchange(ref _latestStandings, null);
        Interlocked.Exchange(ref _latestSelf, null);
        Interlocked.Exchange(ref _latestExtras, null);

        // Stop re-announcing it on reconnect — but only if it is still the session being published.
        // The next session can start before the previous one's end is processed, and clearing that
        // one would leave the hub receiving standings for a session it was never told about.
        if (Volatile.Read(ref _currentSession) is { } current && current.SessionId == sessionId)
        {
            Interlocked.CompareExchange(ref _currentSession, null, current);
            Volatile.Write(ref _sessionAnnouncementPending, 0);
        }

        PublishControl(new LiveGoodbye(sessionId, reason));
    }

    /// <summary>
    /// Takes the next message to send, waiting until one exists.
    /// </summary>
    /// <remarks>
    /// Priority is control, then the session announcement, then standings, then the local car's
    /// channels, then extras. The session comes before any data because the hub cannot place
    /// standings for a session it has not been told about. Standings ahead of self because they
    /// arrive at a tenth of the rate: preferring the 60 Hz stream would let a backed-up socket
    /// starve the timing tower entirely, whereas a self frame skipped now is replaced within
    /// milliseconds.
    /// <para>
    /// Extras go <b>last</b>, below even the 60 Hz stream, and that is the one place this ladder
    /// stops being "slowest first". The reasoning changes because the two channels are not competing
    /// for the same thing: the focus stream's value is its continuity, and a damage reading is worth
    /// having within a second or two of the truth. Putting extras above self would let a once-a-
    /// second document interrupt the trace a race engineer is reading, to deliver a number that will
    /// look the same next second.
    /// </para>
    /// </remarks>
    public async ValueTask<LivePublisherMessage> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryRead() is { } message)
            {
                return message;
            }

            await _wake.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Takes the next message if one is ready, without waiting.</summary>
    public LivePublisherMessage? TryRead()
    {
        if (_control.Reader.TryRead(out var control))
        {
            return control;
        }

        if (Interlocked.Exchange(ref _sessionAnnouncementPending, 0) == 1
            && Volatile.Read(ref _currentSession) is { } session)
        {
            return session;
        }

        if (Interlocked.Exchange(ref _latestStandings, null) is { } standings)
        {
            return standings;
        }

        if (Interlocked.Exchange(ref _latestSelf, null) is { } self)
        {
            return self;
        }

        return Interlocked.Exchange(ref _latestExtras, null);
    }

    private void PublishControl(LivePublisherMessage message)
    {
        if (!_control.Writer.TryWrite(message))
        {
            // Only reachable if the channel has been completed, since DropOldest never refuses a
            // write. Nothing is publishing any more, so there is nowhere for this to go.
            _logger.LogDebug("Dropped a {MessageType}: the live outbox is closed.", message.GetType().Name);
            return;
        }

        Wake();
    }

    private void Wake() => _wake.Writer.TryWrite(0);
}
