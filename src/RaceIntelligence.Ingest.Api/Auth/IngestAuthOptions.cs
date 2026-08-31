using System.ComponentModel.DataAnnotations;

namespace RaceIntelligence.Ingest.Api.Auth;

/// <summary>
/// Configuration for the ingest API's collector authentication, bound from the <c>Ingest</c>
/// configuration section.
/// </summary>
public sealed class IngestAuthOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Ingest";

    /// <summary>
    /// The keys a collector may present as <c>X-Api-Key</c>, one per collector, keyed by a label.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A map rather than a single string because there is more than one collector: one on the LAN
    /// beside the server, one on another driver's machine on another network. The label
    /// (<c>mark-gaming-pc</c>, <c>friend-gaming-pc</c>) is what appears in logs, and deleting one
    /// entry revokes that collector without touching the other — which a single shared secret
    /// cannot do, because revoking it cuts off everybody.
    /// </para>
    /// <para>
    /// Rotation and revocation both take effect on restart, since the digests are computed once at
    /// construction (see <see cref="CollectorKeyGate"/>). That is the right trade for a value that
    /// changes when a person is added or removed, not per request.
    /// </para>
    /// <para>
    /// <b>Required, and required to be non-empty.</b> An ingest API with no keys configured would
    /// reject every request with a 401 that looks exactly like a client presenting the wrong key,
    /// so the misconfiguration would be diagnosed on the collector rather than on the server that
    /// caused it. Failing to boot is the honest outcome, and it is the same call already made for
    /// <c>Ingest:GameKey</c> and the <c>raceintel</c> connection string.
    /// </para>
    /// </remarks>
    [Required(ErrorMessage = "Ingest:ApiKeys must configure at least one labelled collector key; an ingest API with no keys can accept nothing.")]
    [MinLength(1, ErrorMessage = "Ingest:ApiKeys must configure at least one labelled collector key; an ingest API with no keys can accept nothing.")]
    public Dictionary<string, string> ApiKeys { get; init; } = [];
}
