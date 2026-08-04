using MessagePack;

namespace RaceIntelligence.Ingest.Contracts.Telemetry;

/// <summary>
/// The single <see cref="MessagePackSerializerOptions"/> instance every producer/consumer of the
/// telemetry batch wire format must serialize and deserialize with.
/// </summary>
/// <remarks>
/// <para>
/// Both the collector (writing <see cref="TelemetryBatchRequest"/>) and the ingest API (reading
/// it) reference this constant rather than each independently constructing options, so the wire
/// format can never silently drift between the two ends (e.g. one side enabling LZ4 block
/// compression the other doesn't expect).
/// </para>
/// <para>
/// <b>Why <see cref="MessagePackSerializerOptions.Standard"/> and not a source-generated
/// resolver:</b> MessagePack v3 ships a Roslyn source generator (via the transitive
/// <c>MessagePackAnalyzer</c> package) that can emit AOT-safe formatters at compile time, but it
/// requires opting a specific generated resolver into the pipeline, and this project's build does
/// not currently produce one it can discover under a stable name. <c>Standard</c>'s
/// <c>DynamicObjectResolver</c> fallback (Reflection.Emit at runtime) already handles every
/// <c>[MessagePackObject]</c>/<c>[Key]</c>-attributed type in this project correctly, and neither
/// the collector nor this API is deployed as Native AOT/trimmed today. Revisit this if that
/// changes.
/// </para>
/// </remarks>
public static class TelemetryMessagePackOptions
{
    /// <summary>The shared serializer options for <see cref="TelemetryBatchRequest"/> and its members.</summary>
    public static readonly MessagePackSerializerOptions Default = MessagePackSerializerOptions.Standard;
}
