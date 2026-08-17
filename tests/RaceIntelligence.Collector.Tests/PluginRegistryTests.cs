using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RaceIntelligence.Collector.Abstractions;
using RaceIntelligence.Collector.Plugins;
using Shouldly;

namespace RaceIntelligence.Collector.Tests;

/// <summary>
/// Which plugins run, and what happens when the answer is "none".
/// </summary>
public class PluginRegistryTests
{
    private sealed class StubPlugin(string id) : ITelemetryPlugin
    {
        public string Id => id;

        public bool WasRegistered { get; private set; }

        public void Register(IHostApplicationBuilder builder) => WasRegistered = true;
    }

    private static IHostApplicationBuilder BuilderWith(params (string Key, string Value)[] settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)));
        return builder;
    }

    [Fact]
    public void A_plugin_switched_on_in_configuration_is_registered()
    {
        var plugin = new StubPlugin("Ingest");
        var builder = BuilderWith(("Collector:Ingest:Enabled", "true"));

        var enabled = PluginRegistry.RegisterEnabled(builder, [new PluginCandidate("Ingest", EnabledByDefault: false, () => plugin)]);

        enabled.ShouldHaveSingleItem().Id.ShouldBe("Ingest");
        plugin.WasRegistered.ShouldBeTrue();
    }

    [Fact]
    public void A_plugin_switched_off_is_never_even_constructed()
    {
        // Deferred construction is the point: a disabled plugin must not be asked for things a
        // collector running without it has no reason to produce.
        bool constructed = false;
        var builder = BuilderWith(("Collector:Live:Enabled", "false"), ("Collector:Ingest:Enabled", "true"));

        PluginRegistry.RegisterEnabled(builder,
        [
            new PluginCandidate("Ingest", EnabledByDefault: true, () => new StubPlugin("Ingest")),
            new PluginCandidate("Live", EnabledByDefault: true, () =>
            {
                constructed = true;
                return new StubPlugin("Live");
            }),
        ]);

        constructed.ShouldBeFalse();
    }

    [Fact]
    public void An_absent_setting_falls_back_to_the_plugins_own_default()
    {
        // Archiving is the platform's primary job and defaults on; publishing sends this machine's
        // session somewhere other people can watch, so it defaults off.
        var builder = BuilderWith();

        var enabled = PluginRegistry.RegisterEnabled(builder,
        [
            new PluginCandidate("Ingest", EnabledByDefault: true, () => new StubPlugin("Ingest")),
            new PluginCandidate("Live", EnabledByDefault: false, () => new StubPlugin("Live")),
        ]);

        enabled.Select(plugin => plugin.Id).ShouldBe(["Ingest"]);
    }

    [Fact]
    public void Enabling_no_plugin_at_all_fails_at_startup()
    {
        // Otherwise the collector would read the simulator at 60 Hz and throw every frame away —
        // discoverable only by noticing, after the race, that nothing was recorded.
        var builder = BuilderWith(("Collector:Ingest:Enabled", "false"), ("Collector:Live:Enabled", "false"));

        var act = () => PluginRegistry.RegisterEnabled(builder,
        [
            new PluginCandidate("Ingest", EnabledByDefault: true, () => new StubPlugin("Ingest")),
            new PluginCandidate("Live", EnabledByDefault: false, () => new StubPlugin("Live")),
        ]);

        var exception = act.ShouldThrow<InvalidOperationException>();
        exception.Message.ShouldContain("Collector:Ingest:Enabled");
        exception.Message.ShouldContain("Collector:Live:Enabled");
    }

    [Fact]
    public void Both_plugins_enabled_is_the_normal_case()
    {
        var builder = BuilderWith(("Collector:Ingest:Enabled", "true"), ("Collector:Live:Enabled", "true"));

        var enabled = PluginRegistry.RegisterEnabled(builder,
        [
            new PluginCandidate("Ingest", EnabledByDefault: true, () => new StubPlugin("Ingest")),
            new PluginCandidate("Live", EnabledByDefault: false, () => new StubPlugin("Live")),
        ]);

        enabled.Select(plugin => plugin.Id).ShouldBe(["Ingest", "Live"]);
    }
}
