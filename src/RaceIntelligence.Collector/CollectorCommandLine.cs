using RaceIntelligence.Collector.Abstractions;

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
/// Two shapes are recognised. The named shorthands (<c>--live</c>, <c>--no-ingest</c>) cover the two
/// plugins anyone switches often enough to want a word for. The generic pair
/// (<c>--plugin &lt;id&gt;</c>, <c>--no-plugin &lt;id&gt;</c>) works for any plugin, including ones
/// added later that never get a shorthand of their own.
/// </para>
/// <para>
/// Only these are touched. Everything else passes through untouched, so the full
/// <c>--Collector:Live:StandingsInterval 00:00:00.2</c> form keeps working for anything without a
/// shorthand, and a typo stays a typo rather than being silently reinterpreted.
/// </para>
/// </remarks>
public static class CollectorCommandLine
{
    private const string PluginFlag = "--plugin";
    private const string NoPluginFlag = "--no-plugin";

    private static readonly Dictionary<string, string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["--live"] = EnableKey("Live", enabled: true),
        ["--no-live"] = EnableKey("Live", enabled: false),
        ["--ingest"] = EnableKey("Ingest", enabled: true),
        ["--no-ingest"] = EnableKey("Ingest", enabled: false),
    };

    /// <summary>Rewrites recognised bare flags in <paramref name="args"/> into <c>key=value</c> form.</summary>
    /// <exception cref="ArgumentException">
    /// <c>--plugin</c> or <c>--no-plugin</c> was given without a plugin id after it. Reported rather
    /// than ignored: silently dropping it would start the collector with the opposite set of plugins
    /// to the one asked for.
    /// </exception>
    public static string[] Expand(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var expanded = new List<string>(args.Length);

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (Flags.TryGetValue(arg, out string? replacement))
            {
                expanded.Add(replacement);
                continue;
            }

            bool isPluginFlag = arg.Equals(PluginFlag, StringComparison.OrdinalIgnoreCase);
            bool isNoPluginFlag = arg.Equals(NoPluginFlag, StringComparison.OrdinalIgnoreCase);

            if (isPluginFlag || isNoPluginFlag)
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                {
                    throw new ArgumentException(
                        $"'{arg}' needs a plugin id after it, e.g. '{arg} Live'.",
                        nameof(args));
                }

                expanded.Add(EnableKey(args[i + 1], enabled: isPluginFlag));
                i++;
                continue;
            }

            expanded.Add(arg);
        }

        return [.. expanded];
    }

    /// <summary>The recognised flags, for a usage message or a test that keeps documentation honest.</summary>
    public static IReadOnlyCollection<string> KnownFlags => [.. Flags.Keys, PluginFlag, NoPluginFlag];

    private static string EnableKey(string pluginId, bool enabled) =>
        $"{CollectorOptions.SectionName}:{pluginId}:Enabled={(enabled ? "true" : "false")}";
}
