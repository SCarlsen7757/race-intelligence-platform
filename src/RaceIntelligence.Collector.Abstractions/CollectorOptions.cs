using System.ComponentModel.DataAnnotations;

namespace RaceIntelligence.Collector.Abstractions;

/// <summary>
/// The collector's own configuration, bound from the <c>Collector</c> section — everything that
/// belongs to reading the simulator rather than to any one destination.
/// </summary>
/// <remarks>
/// Each plugin binds its own block underneath this one (<c>Collector:Ingest</c>,
/// <c>Collector:Live</c>) and validates it itself, so adding a destination adds a block rather than
/// a property here. This type lives in the abstractions assembly because a plugin legitimately needs
/// to see it — a publishing rate faster than the poll rate is a misconfiguration only the plugin can
/// recognise, and it cannot recognise it without knowing the poll rate.
/// </remarks>
public sealed class CollectorOptions
{
    /// <summary>The configuration section this type binds to.</summary>
    public const string SectionName = "Collector";

    /// <summary>How often the connector polls the simulator's telemetry API. Default: 60 Hz.</summary>
    /// <remarks>
    /// Owned by the collector rather than by a plugin because it is the rate at which the simulator
    /// is read at all. Every plugin's rate is derived from it and none may exceed it: no destination
    /// can be sent data more often than it is captured.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00.001", "00:00:01")]
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1.0 / 60.0);
}
