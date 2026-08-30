using Microsoft.Extensions.Options;
using RaceIntelligence.Collector.Abstractions;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Collector.Abstractions.Telemetry;
using RaceIntelligence.RaceRoom.Telemetry;
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
public sealed class LiveObserver(ILiveOutbox outbox, IOptions<LiveOptions> options, TimeProvider timeProvider)
    : ISessionObserver, ISampleObserver, IStandingsObserver, ISlowChannelObserver
{
    /// <summary>
    /// The current session's driver identity, carried so live frames for the local car can say whose
    /// they are. A <see cref="RaceRoomTelemetrySample"/> describes a car; only the session knows the driver
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

    /// <summary>
    /// When the stint frame last went out, so its rate is measured against the clock rather than
    /// counted in samples.
    /// </summary>
    /// <remarks>
    /// By elapsed time, not every Nth sample, for the reason the dashboard's tyre rings used to give
    /// before the wire took the job over: the poll rate is not a constant, and a machine under load
    /// reports fewer frames a second, so a fixed count would silently stretch the interval whenever
    /// the game got busy. Starts at <see cref="DateTimeOffset.MinValue"/> so the first sample of a
    /// session publishes immediately.
    /// </remarks>
    private DateTimeOffset _lastStintAtUtc = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public TimeSpan StandingsInterval => options.Value.StandingsInterval;

    /// <inheritdoc />
    public TimeSpan SlowChannelInterval => options.Value.SlowChannelInterval;

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
    /// <remarks>
    /// Two frames come out of one sample, at two rates. The self frame goes every time; the stint
    /// frame — the tyre channels — goes at <see cref="LiveOptions.StintInterval"/>, because a tyre
    /// is read over a stint and sixty samples a second of one is a flat line sent expensively.
    /// <para>
    /// The decimation is here rather than in the outbox because this is where the clock already is,
    /// and rather than at the far end because the far end is across a network: a consumer thinning
    /// the stream after it arrives has already paid for every byte it discards.
    /// </para>
    /// </remarks>
    public void OnSample(RaceRoomTelemetrySample sample, CancellationToken cancellationToken)
    {
        outbox.PublishSelf(sample, _currentSimDriverId);

        var now = timeProvider.GetUtcNow();
        if (now - _lastStintAtUtc < options.Value.StintInterval)
        {
            return;
        }

        _lastStintAtUtc = now;
        outbox.PublishStint(sample, _currentSimDriverId);
    }

    /// <summary>
    /// Nothing to unpark. The outbox drops rather than blocking, so this observer can never be
    /// holding the collect loop when shutdown begins.
    /// </summary>
    public void OnSampleStreamCompleted()
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// The capture time is taken here rather than carried on the event, because this is the last
    /// point that is still on the publishing machine's own clock — the same clock every other frame
    /// this collector sends is stamped with, which is what makes a hub's latency readout mean
    /// anything.
    /// </remarks>
    public void OnSlowChannels(RaceRoomTelemetrySample sample, IReadOnlyList<OperatingWindow> operatingWindows) =>
        outbox.PublishSlowChannels(sample, operatingWindows, timeProvider.GetUtcNow(), _currentSimDriverId);

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
