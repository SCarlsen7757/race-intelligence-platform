using System.Security.Cryptography;
using System.Text;

namespace RaceIntelligence.Identity.Api.Auth;

/// <summary>
/// Checks a presented <c>X-Api-Key</c> against the configured <c>Identity:ApiKey</c>.
/// </summary>
/// <remarks>
/// A singleton so the expected digest is computed once rather than on every request. Hashing both
/// sides first is what makes the comparison length-independent:
/// <see cref="CryptographicOperations.FixedTimeEquals"/> is only constant-time for equal-length
/// inputs and returns immediately otherwise, which would leak the key's length.
/// </remarks>
public sealed class IdentityApiKeyGate
{
    /// <summary>The header a caller presents its key in.</summary>
    public const string HeaderName = "X-Api-Key";

    private readonly byte[] _expectedDigest;

    /// <summary>Creates a gate over <c>Identity:ApiKey</c> in <paramref name="configuration"/>.</summary>
    public IdentityApiKeyGate(IConfiguration configuration) =>
        _expectedDigest = Digest(configuration["Identity:ApiKey"]);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="providedKey"/> is the configured key.
    /// </summary>
    /// <remarks>
    /// An unconfigured key never matches, including when the caller also presents nothing. A
    /// registry started without a key is misconfigured, and the safe reading of that is "nobody may
    /// call it" rather than "anybody may".
    /// </remarks>
    public bool IsValid(string? providedKey)
    {
        if (_expectedDigest.Length == 0 || string.IsNullOrEmpty(providedKey))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(_expectedDigest, Digest(providedKey));
    }

    private static byte[] Digest(string? value) =>
        string.IsNullOrEmpty(value) ? [] : SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
