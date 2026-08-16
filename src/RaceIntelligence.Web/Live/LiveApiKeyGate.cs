using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// Checks the <c>X-Api-Key</c> header on a publishing WebSocket upgrade.
/// </summary>
/// <remarks>
/// <para>
/// A plain service rather than an <c>IEndpointFilter</c> like the ingest API's
/// <c>ApiKeyFilter</c>. The upgrade has to be rejected <i>before</i> it is accepted — once
/// <c>AcceptWebSocketAsync</c> has run, the response has already been written and a filter can no
/// longer turn it into a 401.
/// </para>
/// <para>
/// <b>The comparison is constant-time</b>, which the ingest API's is not, and the difference is
/// deliberate rather than an inconsistency. That key guards a service documented as LAN-only; this
/// one guards an endpoint whose entire purpose is to be reachable from the internet through a
/// tunnel, where an attacker can time responses. A length-independent digest comparison costs
/// nothing and removes the question.
/// </para>
/// </remarks>
public sealed class LiveApiKeyGate(IOptions<LiveHubOptions> options)
{
    /// <summary>The header a publishing client presents its key in.</summary>
    public const string HeaderName = "X-Api-Key";

    private readonly byte[] _expectedDigest = Digest(options.Value.ApiKey);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="providedKey"/> is the configured
    /// publishing key.
    /// </summary>
    /// <remarks>
    /// An unconfigured key never matches, including when the caller also presents nothing. A hub
    /// started without a key is misconfigured, and the safe reading of that is "nobody may
    /// publish" rather than "anybody may".
    /// </remarks>
    public bool IsValid(string? providedKey)
    {
        if (_expectedDigest.Length == 0 || string.IsNullOrEmpty(providedKey))
        {
            return false;
        }

        // Hashing both sides first is what makes this length-independent: FixedTimeEquals is only
        // constant-time for equal-length inputs, and returns immediately otherwise, which would
        // leak the key's length.
        return CryptographicOperations.FixedTimeEquals(_expectedDigest, Digest(providedKey));
    }

    private static byte[] Digest(string value) =>
        string.IsNullOrEmpty(value) ? [] : SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
