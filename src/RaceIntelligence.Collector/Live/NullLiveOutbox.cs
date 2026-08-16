using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Collector.Live;

/// <summary>
/// The outbox used when live publishing is switched off: every call is a no-op.
/// </summary>
/// <remarks>
/// A null object rather than a nullable dependency, so <see cref="TelemetryCollectorService"/> has
/// one code path regardless of configuration — the alternative is a null check beside every publish
/// call, which is exactly the kind of thing that gets added in three places and forgotten in a
/// fourth.
/// <para>
/// It costs nothing measurable. The interface takes canonical model types, so no wire DTO is ever
/// built for a message nobody will send, and callers that want to skip upstream work entirely
/// (reading the simulator's driver array, for one) test <see cref="IsEnabled"/> first.
/// </para>
/// </remarks>
public sealed class NullLiveOutbox : ILiveOutbox
{
    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public void PublishSessionStarted(SessionInfo session, string rosterFingerprint, int rosterSize)
    {
    }

    /// <inheritdoc />
    public void PublishStandings(SessionStandings standings)
    {
    }

    /// <inheritdoc />
    public void PublishSelf(TelemetrySample sample, string? simDriverId)
    {
    }

    /// <inheritdoc />
    public void PublishSessionEnded(Guid sessionId, string? reason)
    {
    }
}
