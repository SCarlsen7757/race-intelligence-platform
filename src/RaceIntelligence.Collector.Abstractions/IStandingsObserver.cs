using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Collector.Abstractions;

/// <summary>Consumes the observed view of every car in the session.</summary>
/// <remarks>
/// <para>
/// Standings are scoring-granularity and cover the whole field, including cars nobody is collecting
/// from. Nothing archives them today — they exist to be published live — but the interface is not
/// live-specific, and a plugin that wanted to record them could.
/// </para>
/// <para>
/// <b>Runs on the collect loop and must return immediately</b>, for the same reason as
/// <see cref="ISampleObserver.OnSample"/>. The rate is lower — a snapshot every 100 ms rather than
/// every 16 — but a snapshot carries the entire field, so it is the larger of the two payloads.
/// </para>
/// </remarks>
public interface IStandingsObserver
{
    /// <summary>
    /// A standings snapshot has been read. Always a whole snapshot, never a delta: positions and
    /// gaps are only internally consistent as a set.
    /// </summary>
    void OnStandings(SessionStandings standings);
}
