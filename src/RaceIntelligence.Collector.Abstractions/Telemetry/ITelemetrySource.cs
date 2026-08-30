using RaceIntelligence.RaceRoom.Telemetry;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;

namespace RaceIntelligence.Collector.Abstractions.Telemetry;

/// <summary>
/// A live source of telemetry from a single simulator connection — implemented once per
/// simulator by that simulator's connector.
/// </summary>
/// <remarks>
/// Everything is interleaved into one ordered <see cref="TelemetryEvent"/> stream rather than a
/// sample stream plus separate session-started/ended callbacks, because separate channels race:
/// samples can still arrive after "session ended" fires and the consumer cannot tell which session
/// they belong to. One stream makes ordering a guarantee of the interface — after observing
/// <see cref="SessionEnded"/>, no further <see cref="TelemetrySampleReceived"/> for that session
/// can follow.
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
