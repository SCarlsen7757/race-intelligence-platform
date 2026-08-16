namespace RaceIntelligence.Collector;

/// <summary>
/// Expands the collector's bare command-line flags into the configuration keys they stand for.
/// </summary>
/// <remarks>
/// <para>
/// .NET's command-line configuration provider needs a value for every switch — even a boolean is
/// <c>--Collector:Live:Enabled true</c> — and its switch-mapping feature only shortens the key, not
/// the shape. Someone starting the collector to publish a session wants <c>--live</c>, so the
/// handful of flags that matter are rewritten into the long form before the provider sees them.
/// </para>
/// <para>
/// Only these flags are touched. Everything else is passed through untouched, so the full
/// <c>--Collector:Live:StandingsInterval 00:00:00.2</c> form keeps working for anything without a
/// shorthand, and a typo stays a typo rather than being silently reinterpreted.
/// </para>
/// </remarks>
public static class CollectorCommandLine
{
    private static readonly Dictionary<string, string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["--live"] = "Collector:Live:Enabled=true",
        ["--no-live"] = "Collector:Live:Enabled=false",
        ["--ingest"] = "Collector:Ingest:Enabled=true",
        ["--no-ingest"] = "Collector:Ingest:Enabled=false",
    };

    /// <summary>Rewrites recognised bare flags in <paramref name="args"/> into <c>key=value</c> form.</summary>
    public static string[] Expand(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var expanded = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            expanded[i] = Flags.TryGetValue(args[i], out string? replacement) ? replacement : args[i];
        }

        return expanded;
    }

    /// <summary>The recognised flags, for a usage message or a test that keeps documentation honest.</summary>
    public static IReadOnlyCollection<string> KnownFlags => Flags.Keys;
}
