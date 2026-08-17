using System.Security.Cryptography;
using System.Text;
using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Live.Contracts;

/// <summary>
/// Derives a stable identity for "the set of cars in this session", used to tell two clients in the
/// same online server apart from two clients in different servers that happen to share a session
/// key.
/// </summary>
/// <remarks>
/// <para>
/// RaceRoom publishes no server identifier, so a room key has to be derived from what the
/// simulator does report: track, layout, session type and session iteration. That tuple is a good
/// key and a poor identity — two people qualifying at Spa at the same time in different servers
/// produce exactly the same one. What differs is who they can see, and that is what this captures.
/// </para>
/// <para>
/// <b>An exact fingerprint match is strong evidence, not the merge rule.</b> Rosters change as
/// drivers join, leave and are disconnected, so two clients in one server will disagree the moment
/// anyone moves. The hub therefore treats a match as a fast path and otherwise measures overlap
/// against the full rosters it already holds from each client's standings. The fingerprint's real
/// job is to be available in the session announcement, before any standings have arrived.
/// </para>
/// <para>
/// Truncated SHA-256 rather than a non-cryptographic hash, and hex rather than raw bytes: it
/// crosses a machine boundary and travels as a string, and a 128-bit prefix makes an accidental
/// collision between two live sessions not worth reasoning about. It is not a secret and provides
/// no security property — it identifies a room, it does not protect one.
/// </para>
/// </remarks>
public static class LiveRosterFingerprint
{
    /// <summary>
    /// The per-driver key a fingerprint is built from, in preference order: the simulator's driver
    /// identity, then its per-session slot, then the display name.
    /// </summary>
    /// <remarks>
    /// The prefixes matter. Without them a driver whose identity is the string <c>"7"</c> and a
    /// driver in slot 7 would collide, and the fallbacks exist precisely for sessions where
    /// identities are missing — which is exactly when such a collision would happen.
    /// </remarks>
    public static string KeyFor(DriverStanding driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        // Never null in practice: DisplayName is required, so the last fallback always produces a
        // key. The coalesce is for the empty-name case, where the overload declines rather than
        // inventing "name:" — a roster row still needs a key of its own to be counted.
        return KeyFor(driver.SimDriverId, driver.SlotId, driver.DisplayName)
            ?? $"name:{driver.DisplayName}";
    }

    /// <summary>
    /// The same key from loose identity fields, for a car described outside a
    /// <see cref="DriverStanding"/> — the local car of a publishing client, which the hub knows
    /// only from the session announcement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole chain, not just the first step.</b> This overload exists because the hub used
    /// to derive the local car's key from its simulator identity alone while tower rows came
    /// through <see cref="KeyFor(DriverStanding)"/> above with two fallbacks behind it. The two
    /// agreed for as long as the simulator issued identities and silently stopped agreeing when it
    /// did not, which is every offline RaceRoom session — and a key that resolves to nothing means
    /// the focus and extras channels are dropped, so the driver's own car loses its pedals, tyre
    /// data and damage while the tower carries on. Both paths now format keys here, so the two can
    /// no longer drift apart.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The key, or <see langword="null"/> when nothing identifies the car at all. Null is a real
    /// answer rather than a failure: guessing a row would show a race engineer another driver's
    /// damage, which is worse than showing them none.
    /// </returns>
    public static string? KeyFor(string? simDriverId, int? slotId, string? displayName)
    {
        if (!string.IsNullOrEmpty(simDriverId))
        {
            return $"id:{simDriverId}";
        }

        if (slotId is { } slot)
        {
            return $"slot:{slot}";
        }

        return string.IsNullOrEmpty(displayName) ? null : $"name:{displayName}";
    }

    /// <summary>
    /// Computes the fingerprint of a roster. Order-independent — two clients list the field in
    /// whatever order their own simulator does, and must still agree.
    /// </summary>
    /// <returns>A 32-character lowercase hex string, or the empty string for an empty roster.</returns>
    public static string Compute(IEnumerable<DriverStanding> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);

        return Compute(drivers.Select(KeyFor));
    }

    /// <summary>Computes the fingerprint from pre-derived keys. See <see cref="Compute(IEnumerable{DriverStanding})"/>.</summary>
    public static string Compute(IEnumerable<string> driverKeys)
    {
        ArgumentNullException.ThrowIfNull(driverKeys);

        // Distinct before sorting: a simulator that lists the same slot twice during a disconnect
        // must not produce a different fingerprint from one that has settled.
        string[] keys = [.. driverKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (keys.Length == 0)
        {
            return string.Empty;
        }

        // Ordinal join with a separator that cannot appear in a key prefix, so ["a", "b"] and
        // ["a\nb"] cannot hash alike.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', keys)));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Measures how much two rosters have in common, as a fraction of the smaller one: 1.0 when one
    /// is a subset of the other, 0.0 when they are disjoint.
    /// </summary>
    /// <remarks>
    /// Divided by the smaller roster rather than the union, because the two clients are not
    /// symmetric in what they can see — one may have joined a race already in progress, or be
    /// mid-reconnect with a partial field. Against the union, a client seeing 5 of 20 cars would
    /// score 0.25 and never merge, even though every car it sees is in the other's list.
    /// </remarks>
    /// <returns>0.0 when either roster is empty — nothing is known, so nothing is confirmed.</returns>
    public static double Overlap(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Count == 0 || right.Count == 0)
        {
            return 0.0;
        }

        var leftSet = left.ToHashSet(StringComparer.Ordinal);
        var rightSet = right.ToHashSet(StringComparer.Ordinal);
        int shared = leftSet.Count(rightSet.Contains);

        return (double)shared / Math.Min(leftSet.Count, rightSet.Count);
    }
}
