using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RaceIntelligence.Collector.Abstractions;

namespace RaceIntelligence.Collector.Plugins;

/// <summary>
/// Decides which plugins are switched on and lets each register its own services.
/// </summary>
/// <remarks>
/// <para>
/// Composition is config-driven rather than dynamic: the set of plugins the collector <i>can</i> run
/// is fixed when it is built, and configuration chooses which of them actually run. That keeps the
/// build trimmable and AOT-safe, avoids a public plugin API to keep compatible across versions, and
/// makes a misconfigured plugin a startup failure rather than a load-time surprise mid-race. The
/// cost is that a genuinely new destination needs a rebuild, which for a platform deployed by the
/// person developing it is not a cost at all.
/// </para>
/// <para>
/// A plugin is enabled by <c>Collector:&lt;Id&gt;:Enabled</c>, using each plugin's own default when
/// the key is absent — archiving defaults on, publishing defaults off.
/// </para>
/// </remarks>
public static class PluginRegistry
{
    /// <summary>
    /// Registers every enabled plugin, returning the ones that were registered.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No plugin is enabled, which would leave the collector reading the simulator and doing nothing
    /// with what it reads. Failing here rather than at the first frame means the mistake is found
    /// when the collector is started, not after a race has been run.
    /// </exception>
    public static IReadOnlyList<ITelemetryPlugin> RegisterEnabled(
        IHostApplicationBuilder builder,
        IReadOnlyList<PluginCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(candidates);

        var enabled = new List<ITelemetryPlugin>(candidates.Count);

        foreach (var candidate in candidates)
        {
            if (!IsEnabled(builder.Configuration, candidate))
            {
                continue;
            }

            var plugin = candidate.Create();
            plugin.Register(builder);
            enabled.Add(plugin);
        }

        if (enabled.Count == 0)
        {
            string names = string.Join(", ", candidates.Select(c => $"'{CollectorOptions.SectionName}:{c.Id}:Enabled'"));
            throw new InvalidOperationException(
                $"No collector plugin is enabled ({names} are all false), so the collector would read the "
                + "simulator and do nothing with it. Enable at least one.");
        }

        return enabled;
    }

    /// <summary>Whether a plugin is switched on, falling back to its own default.</summary>
    public static bool IsEnabled(IConfiguration configuration, PluginCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(candidate);

        return configuration
            .GetSection($"{CollectorOptions.SectionName}:{candidate.Id}:Enabled")
            .Get<bool?>() ?? candidate.EnabledByDefault;
    }
}

/// <summary>
/// A plugin the collector knows how to build, together with the two facts needed to decide whether
/// to build it.
/// </summary>
/// <remarks>
/// The factory is deferred so a disabled plugin is never constructed — a plugin's constructor may
/// reasonably demand things (a connector's game key, its capability set) that a collector running
/// without it should not have to produce.
/// </remarks>
/// <param name="Id">Matches <see cref="ITelemetryPlugin.Id"/> and the configuration block name.</param>
/// <param name="EnabledByDefault">Used when <c>Collector:&lt;Id&gt;:Enabled</c> is absent.</param>
/// <param name="Create">Builds the plugin. Called only when it is enabled.</param>
public sealed record PluginCandidate(string Id, bool EnabledByDefault, Func<ITelemetryPlugin> Create);
