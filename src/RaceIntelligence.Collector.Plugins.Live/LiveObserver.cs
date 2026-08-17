using Microsoft.Extensions.Options;
using RaceIntelligence.Collector.Abstractions;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;
using RaceIntelligence.Live.Contracts;

namespace RaceIntelligence.Collector.Plugins.Live;

/// <summary>
/// Feeds the live outbox, and owns the session state the live wire needs but the canonical model
/// does not carry.
/// </summary>
/// <remarks>
/// <para>
/// Every method here returns without doing I/O. The outbox conflates into single slots rather than
/// queueing, so publishing is an interlocked exchange — it cannot block the collect loop and cannot
/// grow without bound, whatever the socket is doing.
/// </para>
/// <para>
/// Nothing here awaits, including the session methods that are allowed to. A hub that is down must
/// not slow the collector for the archive plugin's sake, and the outbox is the component whose whole
/// job is absorbing that.
/// </para>
/// </remarks>
public sealed class LiveObserver(ILiveOutbox outbox, IOptions<LiveOptions> options)
    : ISessionObserver, ISampleObserver, IStandingsObserver
{
    /// <summary>
    /// The current session's driver identity, carried so live frames for the local car can say whose
    /// they are. A <see cref="TelemetrySample"/> describes a car; only the session knows the driver
    /// in it.
    /// </summary>
    private string? _currentSimDriverId;

    /// <summary>The session as first announced, kept so a roster change can be re-announced against it.</summary>
    private SessionInfo? _currentSession;

    /// <summary>
    /// The roster fingerprint last announced to the hub. Tracked so the session is re-announced when
    /// the field changes materially — the hub matches clients into one room by roster overlap, and an
    /// announcement made before anybody had loaded in carries no roster at all.
    /// </summary>
    private string _announcedRosterFingerprint = string.Empty;

    /// <inheritdoc />
    public TimeSpan StandingsInterval => options.Value.StandingsInterval;

    /// <inheritdoc />
    public ValueTask OnSessionStartedAsync(SessionInfo session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        _currentSession = session;
        _currentSimDriverId = session.SimDriverId;
        _announcedRosterFingerprint = string.Empty;

        outbox.PublishSessionStarted(session, _announcedRosterFingerprint, rosterSize: 0);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Nothing to do: completed laps are archive business. The tower reads lap times from the
    /// standings snapshot instead, which has them for every car rather than only the local one.
    /// </summary>
    public ValueTask OnLapCompletedAsync(LapInfo lap, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// Announced immediately rather than after any drain. The archive waits for its tail samples to
    /// land before marking a session ended; the live view has no tail to wait for, and telling the
    /// hub now is what stops a finished session sitting in the dashboard until its room times out.
    /// </remarks>
    public ValueTask OnSessionEndedAsync(Guid sessionId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        outbox.PublishSessionEnded(sessionId, reason: null);

        _currentSession = null;
        _currentSimDriverId = null;
        _announcedRosterFingerprint = string.Empty;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void OnSample(TelemetrySample sample, CancellationToken cancellationToken) =>
        outbox.PublishSelf(sample, _currentSimDriverId);

    /// <summary>
    /// Nothing to unpark. The outbox drops rather than blocking, so this observer can never be
    /// holding the collect loop when shutdown begins.
    /// </summary>
    public void OnSampleStreamCompleted()
    {
    }

    /// <inheritdoc />
    public void OnStandings(SessionStandings standings)
    {
        ArgumentNullException.ThrowIfNull(standings);

        outbox.PublishStandings(standings);

        // Re-announce the session when the field has changed enough to move the fingerprint. The hub
        // decides which clients belong in one room from roster overlap, and the announcement made at
        // session start was necessarily empty — nobody had loaded in yet.
        string fingerprint = LiveRosterFingerprint.Compute(standings.Drivers);
        if (!string.Equals(fingerprint, _announcedRosterFingerprint, StringComparison.Ordinal)
            && _currentSession is { } session)
        {
            _announcedRosterFingerprint = fingerprint;
            outbox.PublishSessionStarted(session, fingerprint, standings.Drivers.Count);
        }
    }
}
