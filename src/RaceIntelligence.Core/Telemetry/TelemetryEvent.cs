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

    /// <summary>The source encountered an error it did not anticipate and is not currently connected.</summary>
    /// <remarks>
    /// Distinct from <see cref="WaitingForSimulator"/>, which names a diagnosed cause ("the game is
    /// not running"); this one means the source does not know what went wrong. It is <b>not</b>
    /// terminal — a source is expected to keep retrying, because an unrecognised error is not the
    /// same as a permanent one, and the alternative is losing a session to a fault that would have
    /// cleared on the next attempt.
    /// </remarks>
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

/// <summary>
/// Raised when the local car's simulator-specific channels are re-published, at their own slow
/// cadence.
/// </summary>
/// <remarks>
/// <para>
/// The same document a <see cref="TelemetrySample"/> already carries on
/// <see cref="TelemetrySample.Extras"/>, emitted separately so a consumer that wants damage or
/// push-to-pass can have it without being handed sixty JSON documents a second to find it in. The
/// archive path reads it off the sample, where it belongs to a row; anything watching it live reads
/// it here, where it arrives about once a second.
/// </para>
/// <para>
/// Not emitted while <see cref="ConnectionState.SessionSuspended"/>, for the same reason samples and
/// standings are not: a paused simulator republishes the same frame indefinitely.
/// </para>
/// </remarks>
public sealed record ExtrasUpdated : TelemetryEvent
{
    /// <summary>The session the values were captured in.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>
    /// The connector's raw JSON document, sentinels untranslated. See <c>IExtrasObserver</c> — a
    /// consumer rendering RaceRoom's <c>-1</c> as zero reports the opposite of the truth.
    /// </summary>
    public required string ExtrasJson { get; init; }
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
