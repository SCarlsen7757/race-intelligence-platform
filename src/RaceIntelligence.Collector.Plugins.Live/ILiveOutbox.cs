using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Collector.Abstractions.Telemetry;
using RaceIntelligence.RaceRoom.Telemetry;

namespace RaceIntelligence.Collector.Plugins.Live;

/// <summary>
/// Where the collect loop hands live frames off to whatever is publishing them.
/// </summary>
/// <remarks>
/// <para>
/// The live counterpart of <see cref="Core.Buffering.ITelemetryBuffer"/>, and deliberately a
/// separate seam rather than a second consumer of it. The two have opposite policies under
/// pressure: the archive buffer must never lose a sample, because it feeds permanent storage, while
/// this one must never block the producer and should always drop the older frame, because a live
/// value is worthless the moment a newer one exists. Sharing one channel would force one policy on
/// both.
/// </para>
/// <para>
/// Every method takes canonical model types rather than wire DTOs, so the no-op implementation used
/// when publishing is switched off costs nothing at all — the mapping happens inside, on the far
/// side of the decision.
/// </para>
/// <para>
/// Implementations must be safe to call from the collect loop without ever blocking it.
/// </para>
/// </remarks>
public interface ILiveOutbox
{
    /// <summary>
    /// Whether anything is actually publishing. Lets a caller skip work that only exists to feed
    /// this outbox — the connector, for instance, need not read the simulator's driver array at all
    /// when nothing will be sent.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>Announces the session this collector has started publishing.</summary>
    /// <param name="session">The session that started.</param>
    /// <param name="rosterFingerprint">
    /// The fingerprint of the field, when known. Empty before the first standings snapshot arrives
    /// — a session is announced immediately rather than waiting for a roster, so the dashboard can
    /// list the client straight away.
    /// </param>
    /// <param name="rosterSize">How many cars went into the fingerprint.</param>
    void PublishSessionStarted(SessionInfo session, string rosterFingerprint, int rosterSize);

    /// <summary>Publishes the observed view of every car in the session. Conflated: only the newest survives.</summary>
    void PublishStandings(SessionStandings standings);

    /// <summary>
    /// Publishes the rich channels for the local car. Conflated: only the newest survives.
    /// </summary>
    /// <param name="sample">The sample to publish.</param>
    /// <param name="simDriverId">
    /// The local driver's simulator identity, which a <see cref="RaceRoomTelemetrySample"/> does not carry —
    /// it describes a car, and only the session knows whose.
    /// </param>
    void PublishSelf(RaceRoomTelemetrySample sample, string? simDriverId);

    /// <summary>
    /// Publishes the local car's stint channels — the tyres. Conflated: only the newest survives.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PublishSelf"/> because the caller decides how often to send it, and
    /// that is the whole point of the split: these are read over a stint, and sending them at the
    /// poll rate spends the high-rate channel's budget on the low-rate channel's data.
    /// </remarks>
    /// <param name="sample">The sample to take the tyre channels from.</param>
    /// <param name="simDriverId">The local driver's simulator identity, as on <see cref="PublishSelf"/>.</param>
    void PublishStint(RaceRoomTelemetrySample sample, string? simDriverId);

    /// <summary>
    /// Publishes the local car's simulator-specific document. Conflated: only the newest survives.
    /// </summary>
    /// <param name="sessionId">The session the values were captured in.</param>
    /// <param name="sample">The sample the slow channels were read from.</param>
    /// <param name="operatingWindows">The tyre and brake bands in force, one per corner.</param>
    /// <param name="capturedAtUtc">Capture time on this machine.</param>
    /// <param name="simDriverId">
    /// The local driver's simulator identity, carried for the same reason it is on
    /// <see cref="PublishSelf"/>: the document describes a car, and only the session knows whose.
    /// </param>
    void PublishSlowChannels(
        RaceRoomTelemetrySample sample,
        IReadOnlyList<OperatingWindow> operatingWindows,
        DateTimeOffset capturedAtUtc,
        string? simDriverId);

    /// <summary>Announces that this collector has stopped publishing a session, so the hub need not wait for a timeout.</summary>
    void PublishSessionEnded(Guid sessionId, string? reason);
}
