using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Collector.Abstractions;

/// <summary>
/// Consumes the collector's session bookkeeping: a session starting, a lap being completed, a
/// session ending.
/// </summary>
/// <remarks>
/// <para>
/// These are the only events rare enough to be awaited. A session begins and ends once, and a lap
/// arrives at most every minute or so, so a plugin may do real work — an HTTP call — inline. The
/// high-rate channels (<see cref="ISampleObserver"/>, <see cref="IStandingsObserver"/>,
/// <see cref="IExtrasObserver"/>) are synchronous and must never block, precisely because they are
/// not rare.
/// </para>
/// <para>
/// Even here, "may be awaited" is not "may take as long as it likes". The collect loop is stalled
/// for the duration, so a plugin whose work is genuinely slow — waiting for an upload queue to
/// drain, for instance — should start it and return, keeping the wait off the loop. What a plugin
/// must not do is assume another plugin's success: every observer is invoked and awaited
/// independently, and one throwing is logged without affecting the others.
/// </para>
/// </remarks>
public interface ISessionObserver
{
    /// <summary>A session has started. The same instance is given to every observer; treat it as read-only.</summary>
    ValueTask OnSessionStartedAsync(SessionInfo session, CancellationToken cancellationToken);

    /// <summary>A lap has been completed and scored.</summary>
    ValueTask OnLapCompletedAsync(LapInfo lap, CancellationToken cancellationToken);

    /// <summary>A session has ended.</summary>
    ValueTask OnSessionEndedAsync(Guid sessionId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken);
}
