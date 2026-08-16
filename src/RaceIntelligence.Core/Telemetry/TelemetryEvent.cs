using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Core.Telemetry;

/// <summary>The connectivity state of an <see cref="ITelemetrySource"/>.</summary>
public enum ConnectionState
{
    /// <summary>No connection has been attempted, or a previous connection was cleanly closed.</summary>
    Disconnected,

    /// <summary>The source is actively trying to reach the simulator but has not yet succeeded.</summary>
    WaitingForSimulator,

    /// <summary>Connected to the simulator, but no session is currently active (e.g. sim is at the main menu).</summary>
    Connected,

    /// <summary>Connected and an on-track session is active.</summary>
    InSession,

    /// <summary>
    /// A session is still active but the simulator is not currently producing on-track frames —
    /// the driver is in an in-session menu, or the game is paused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="Connected"/>, which means no session exists at all. Suspension is
    /// deliberately <b>not</b> a session boundary: the same session resumes afterwards, keeping its
    /// id, its sequence numbering and its lap count. Treating a menu visit as the end of a session
    /// fragmented one real session into many, and silently dropped any lap completed while the menu
    /// was open.
    /// </para>
    /// <para>
    /// No <see cref="TelemetrySampleReceived"/> is emitted while suspended. A paused simulator
    /// republishes the same frame indefinitely, so sampling it would store thousands of identical
    /// rows describing a car that is not moving.
    /// </para>
    /// </remarks>
    SessionSuspended,

    /// <summary>The source encountered an unrecoverable error and stopped producing events.</summary>
    Faulted,
}

/// <summary>
/// Base type for every event an <see cref="ITelemetrySource"/> can emit.
/// </summary>
public abstract record TelemetryEvent
{
    /// <summary>UTC time the event occurred, as observed by the source/connector.</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

/// <summary>Raised when a new session begins.</summary>
public sealed record SessionStarted : TelemetryEvent
{
    public required SessionInfo Session { get; init; }
}

/// <summary>Raised for every telemetry sample captured during a session.</summary>
public sealed record TelemetrySampleReceived : TelemetryEvent
{
    public required TelemetrySample Sample { get; init; }
}

/// <summary>Raised when the simulator's view of the whole field is re-read.</summary>
/// <remarks>
/// Emitted at its own cadence, independent of <see cref="TelemetrySampleReceived"/>: the field's
/// scoring data changes far more slowly than a car's control inputs, and re-reading it is
/// comparatively expensive. Like samples, it is not emitted while
/// <see cref="ConnectionState.SessionSuspended"/> — a frozen frame's standings describe the moment
/// the driver opened the menu, not the moment they are read.
/// </remarks>
public sealed record StandingsUpdated : TelemetryEvent
{
    public required SessionStandings Standings { get; init; }
}

/// <summary>Raised when a lap completes.</summary>
public sealed record LapCompleted : TelemetryEvent
{
    public required LapInfo Lap { get; init; }
}

/// <summary>Raised when a session ends.</summary>
public sealed record SessionEnded : TelemetryEvent
{
    public required Guid SessionId { get; init; }
}

/// <summary>Raised whenever the source's <see cref="ConnectionState"/> changes.</summary>
public sealed record ConnectionStateChanged : TelemetryEvent
{
    public required ConnectionState State { get; init; }

    /// <summary>An optional human-readable explanation (e.g. an error message when transitioning to <see cref="Telemetry.ConnectionState.Faulted"/>).</summary>
    public string? Reason { get; init; }
}
