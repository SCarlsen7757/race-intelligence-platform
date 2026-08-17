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
    /// <summary>How often this observer wants a snapshot.</summary>
    /// <remarks>
    /// Declared by the observer rather than imposed by the collector, because reading the
    /// simulator's whole driver array is the single most expensive thing the connector does. The
    /// connector reads at the shortest interval any observer asks for, and — when nothing implements
    /// this interface — does not read the array at all rather than reading it and discarding the
    /// result. That is what makes an archive-only collector pay nothing for a feature it is not
    /// using.
    /// </remarks>
    TimeSpan StandingsInterval { get; }

    /// <summary>
    /// A standings snapshot has been read. Always a whole snapshot, never a delta: positions and
    /// gaps are only internally consistent as a set.
    /// </summary>
    void OnStandings(SessionStandings standings);
}
