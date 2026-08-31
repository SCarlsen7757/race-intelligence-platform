using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace RaceIntelligence.Ingest.Api.Auth;

/// <summary>
/// Checks a presented <c>X-Api-Key</c> against the configured set of per-collector keys, and
/// resolves it to the label that key was configured under.
/// </summary>
/// <remarks>
/// <para>
/// A singleton rather than logic inside <see cref="ApiKeyFilter"/> because the filter is attached
/// in two places — the <c>/api/v1/sessions</c> group and, separately, the telemetry batch endpoint
/// that sits outside that group because of its raw MessagePack body. One service both consume keeps
/// the digests computed once for the process rather than once per request.
/// </para>
/// <para>
/// <b>The comparison is constant-time</b>, matching the live hub's
/// <c>RaceIntelligence.Web.Live.LiveApiKeyGate</c>. Hashing both sides first is what makes it
/// length-independent: <see cref="CryptographicOperations.FixedTimeEquals"/> is only constant-time
/// for equal-length inputs and returns immediately otherwise, which would leak the key's length.
/// </para>
/// <para>
/// The loop over configured keys deliberately has <b>no early exit</b>. Breaking on the first match
/// would make a request presenting the first-configured key measurably faster than one presenting
/// the last, which leaks the matching key's position in the set.
/// </para>
/// </remarks>
public sealed class CollectorKeyGate
{
    /// <summary>The header a collector presents its key in.</summary>
    public const string HeaderName = "X-Api-Key";

    private readonly (string Label, byte[] Digest)[] _keys;

    /// <summary>Creates a gate over the keys in <paramref name="options"/>.</summary>
    public CollectorKeyGate(IOptions<IngestAuthOptions> options)
    {
        // Blank values are dropped rather than stored as an empty digest: a key configured as ""
        // must never be presentable, and IsValid's empty-input guard alone would not stop a
        // caller sending an empty header from matching an empty configured value.
        _keys = [.. options.Value.ApiKeys
            .Where(pair => !string.IsNullOrEmpty(pair.Value))
            .Select(pair => (pair.Key, Digest(pair.Value)))];
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="providedKey"/> is one of the configured
    /// collector keys, setting <paramref name="label"/> to the label it was configured under.
    /// </summary>
    /// <remarks>
    /// An unconfigured gate never matches, including when the caller also presents nothing. An
    /// ingest API started without keys is misconfigured, and the safe reading of that is "nobody
    /// may upload" rather than "anybody may".
    /// </remarks>
    public bool TryResolve(string? providedKey, out string label)
    {
        label = string.Empty;

        if (_keys.Length == 0 || string.IsNullOrEmpty(providedKey))
        {
            return false;
        }

        var presented = Digest(providedKey);
        var matched = -1;

        for (var i = 0; i < _keys.Length; i++)
        {
            if (CryptographicOperations.FixedTimeEquals(_keys[i].Digest, presented))
            {
                matched = i;
            }
        }

        if (matched < 0)
        {
            return false;
        }

        label = _keys[matched].Label;
        return true;
    }

    private static byte[] Digest(string value) =>
        string.IsNullOrEmpty(value) ? [] : SHA256.HashData(Encoding.UTF8.GetBytes(value));

    /// <summary>
    /// The digest of a presented key, as the rate limiter's partition key.
    /// </summary>
    /// <remarks>
    /// The limiter is middleware and runs before endpoint filters, so it cannot use the label this
    /// gate resolves. The digest is the next best partition: stable, one-to-one with the key, and
    /// safe to hold in a limiter's keyed state in a way the key itself is not.
    /// <para>
    /// A request presenting no key at all partitions on the empty string, so all keyless traffic
    /// shares one bucket. That is deliberate, and it matters more now the endpoint is on the
    /// internet: keyless requests are refused by <see cref="ApiKeyFilter"/> anyway, one shared
    /// bucket contains a scanner without letting it evade anything by varying a value, and giving
    /// each keyless request its own partition would be an unbounded-state denial of service in
    /// itself. Legitimate collectors are unaffected — they sit in their own key-digest partitions.
    /// </para>
    /// </remarks>
    public static string PartitionKey(string? providedKey) =>
        string.IsNullOrEmpty(providedKey) ? string.Empty : Convert.ToHexString(Digest(providedKey));
}
