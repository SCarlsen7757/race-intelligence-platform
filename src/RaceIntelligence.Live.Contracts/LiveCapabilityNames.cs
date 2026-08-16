using RaceIntelligence.Core.Capabilities;

namespace RaceIntelligence.Live.Contracts;

/// <summary>
/// Turns a <see cref="SimCapabilities"/> bitmask into the names the dashboard reasons about.
/// </summary>
/// <remarks>
/// <para>
/// The browser is sent names, not the bitmask, for two reasons that both bite in practice.
/// JavaScript numbers are IEEE doubles, so a <see cref="ulong"/> with a bit above 2^53 cannot be
/// represented exactly — and the enum is deliberately sized for 64 capabilities, so that is a
/// question of when rather than whether. And a panel written as
/// <c>needs: ["TyreWear", "TyrePressure"]</c> is readable in review and in devtools, whereas the
/// equivalent bit arithmetic is a silent off-by-one waiting to happen.
/// </para>
/// <para>
/// Names are the enum's own, unchanged, so they match the C# side exactly. That matters when the
/// question being debugged is "why is this panel hidden".
/// </para>
/// </remarks>
public static class LiveCapabilityNames
{
    private static readonly (SimCapabilities Flag, string Name)[] Known =
        [.. Enum.GetValues<SimCapabilities>()
            .Where(value => value != SimCapabilities.None)
            .Select(value => (value, value.ToString()))];

    /// <summary>Expands a capability bitmask into its set flag names.</summary>
    /// <remarks>
    /// Bits this build has no name for are dropped rather than reported as numbers. A client newer
    /// than the hub can declare a capability the hub has never heard of, and the hub cannot say
    /// anything useful about it — the dashboard it serves was built against the same enum, so a
    /// name it could not match would be dead weight either way.
    /// </remarks>
    public static IReadOnlyList<string> From(ulong capabilities)
    {
        if (capabilities == 0)
        {
            return [];
        }

        var caps = (SimCapabilities)capabilities;
        var names = new List<string>();

        foreach (var (flag, name) in Known)
        {
            if (caps.Has(flag))
            {
                names.Add(name);
            }
        }

        return names;
    }
}
