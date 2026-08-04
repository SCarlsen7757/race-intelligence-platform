using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;

namespace RaceIntelligence.Core.Telemetry;

/// <summary>
/// A live source of telemetry from a single simulator connection — implemented once per
/// simulator by that simulator's connector.
/// </summary>
/// <remarks>
/// <para>
/// Telemetry is exposed as a single ordered stream of <see cref="TelemetryEvent"/> rather than a
/// sample stream plus separate "session started"/"session ended" callbacks. A callback-based
/// design admits a race: samples could keep arriving on one channel after "session ended" fired
/// on another, and a consumer would have no principled way to decide whether a given sample
/// belongs to the session that just ended or a new one. Interleaving everything into one ordered
/// <see cref="IAsyncEnumerable{T}"/> makes ordering a guarantee of the interface itself — a
/// consumer that has observed <see cref="SessionEnded"/> for a session can be certain no further
/// <see cref="TelemetrySampleReceived"/> for that session will follow it.
/// </para>
/// </remarks>
public interface ITelemetrySource : IAsyncDisposable
{
    /// <summary>
    /// The simulator/API/connector version currently in effect, or <see langword="null"/> before
    /// the first successful connection — the simulator itself reports its telemetry API version,
    /// so this is not known until a connection is established.
    /// </summary>
    GameVersionIdentity? Version { get; }

    /// <summary>The set of telemetry capabilities this source can currently provide.</summary>
    SimCapabilities Capabilities { get; }

    /// <summary>The current connection state.</summary>
    ConnectionState State { get; }

    /// <summary>
    /// Reads every event this source produces, in order, until <paramref name="cancellationToken"/>
    /// is cancelled or the source is disposed.
    /// </summary>
    IAsyncEnumerable<TelemetryEvent> ReadAllAsync(CancellationToken cancellationToken = default);
}
