using MessagePack;

namespace RaceIntelligence.Live.Contracts.Publish;

/// <summary>
/// The single <see cref="MessagePackSerializerOptions"/> instance every producer and consumer of
/// the live publishing wire format must use.
/// </summary>
/// <remarks>
/// <para>
/// Shared for the same reason <c>TelemetryMessagePackOptions</c> is: with the collector encoding
/// and the hub decoding, options constructed independently at each end can drift into a format
/// mismatch that only shows up as a decode failure in production.
/// </para>
/// <para>
/// <b><see cref="MessagePackSecurity.UntrustedData"/>:</b> the publishing endpoint is reachable by
/// anyone holding the publish key, so its bytes are attacker-influenced. The
/// <see cref="MessagePackSerializerOptions.Standard"/> default caps recursion at
/// <see cref="int.MaxValue"/> and hashes map keys in a collision-prone way, which turns a small
/// hand-written payload into a stack overflow or a quadratic-time hash flood. Encoding is
/// unaffected, so the collector pays nothing for it.
/// </para>
/// <para>
/// <b>No LZ4 compression.</b> Live frames are small and latency-sensitive, and the WebSocket layer
/// can negotiate <c>permessage-deflate</c> if bandwidth ever becomes the binding constraint —
/// compressing twice would spend CPU on both ends for nothing.
/// </para>
/// </remarks>
public static class LiveMessagePackOptions
{
    /// <summary>The shared serializer options for <see cref="LivePublisherMessage"/> and its members.</summary>
    public static readonly MessagePackSerializerOptions Default =
        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);
}
