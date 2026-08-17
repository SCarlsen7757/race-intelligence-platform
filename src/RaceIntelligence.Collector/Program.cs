using Microsoft.Extensions.Options;
using RaceIntelligence.Collector;
using RaceIntelligence.Collector.Abstractions;
using RaceIntelligence.Collector.Plugins;
using RaceIntelligence.Collector.Plugins.Ingest;
using RaceIntelligence.Collector.Plugins.Live;
using RaceIntelligence.Connectors.RaceRoom;
using RaceIntelligence.Core.Telemetry;
using Serilog;

// Bare flags such as --live are rewritten into the Collector:Live:Enabled=true form the
// command-line configuration provider expects; everything else passes through untouched.
var builder = Host.CreateApplicationBuilder(CollectorCommandLine.Expand(args));

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Service discovery, HTTP resilience defaults, health checks, and OpenTelemetry (Aspire).
builder.AddServiceDefaults();

builder.Services
    .AddOptions<CollectorOptions>()
    .Bind(builder.Configuration.GetSection(CollectorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Registered as a service so the upload plugin's batch-by-age logic is testable against a fake
// clock instead of real wall-clock sleeps.
builder.Services.AddSingleton(TimeProvider.System);

// The only simulator-specific lines in this file. Everything else here, and every other type in this
// project, depends solely on ITelemetrySource and the plugin abstractions. Adding a second
// simulator's connector later means adding an alternative registration here — no other change to the
// collector.
//
// RaceRoomTelemetrySource/RaceRoomConnectorOptions carry [SupportedOSPlatform("windows")] (they read
// a Windows named shared-memory block) — CA1416 is suppressed only for these call sites,
// deliberately not for the whole assembly, since every other type in this project is genuinely
// platform-agnostic and must stay callable from the (non-Windows-gated) test project.
#pragma warning disable CA1416
const string GameKey = "raceroom";
var connectorCapabilities = RaceRoomTelemetrySource.DeclaredCapabilities;
#pragma warning restore CA1416

// Composition: every plugin the collector knows how to build, and the configuration decides which of
// them actually run. Plugin services are registered here — before the collect loop below — so the
// host stops the loop first on shutdown and leaves the plugins running to drain what is pending.
var plugins = PluginRegistry.RegisterEnabled(builder,
[
    new PluginCandidate(IngestPlugin.PluginId, EnabledByDefault: true, () => new IngestPlugin()),
    new PluginCandidate(LivePlugin.PluginId, EnabledByDefault: false, () => new LivePlugin
    {
        GameKey = GameKey,
        Capabilities = (ulong)connectorCapabilities,
    }),
]);

#pragma warning disable CA1416
builder.Services.AddSingleton<ITelemetrySource>(sp =>
{
    var options = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;

    // Reading the simulator's whole driver array is the most expensive thing the connector does, and
    // standings exist only to be observed. Asking the registered observers how often they want them
    // — rather than asking whether a particular plugin is switched on — means a collector running
    // nothing that consumes standings tells the connector not to read the array at all, and a future
    // plugin that does consume them needs no change here.
    var standingsObservers = sp.GetServices<IStandingsObserver>().ToArray();
    var standingsInterval = standingsObservers.Length == 0
        ? Timeout.InfiniteTimeSpan
        : standingsObservers.Min(observer => observer.StandingsInterval);

    // Same question asked of the extras channel, and the same answer: with nothing consuming the
    // simulator-specific document the connector is told not to publish one at all, and the fastest
    // consumer sets the rate for everybody. Extras cost almost nothing to produce — the sample
    // already carries the string — but every consumer downstream parses JSON, which is what the
    // rate is really limiting.
    var extrasObservers = sp.GetServices<IExtrasObserver>().ToArray();
    var extrasInterval = extrasObservers.Length == 0
        ? Timeout.InfiniteTimeSpan
        : extrasObservers.Min(observer => observer.ExtrasInterval);

    return new RaceRoomTelemetrySource(new RaceRoomConnectorOptions
    {
        PollInterval = options.PollInterval,
        StandingsInterval = standingsInterval,
        ExtrasInterval = extrasInterval,
    });
});
#pragma warning restore CA1416

builder.Services.AddHostedService<TelemetryCollectorService>();

var host = builder.Build();

host.Services.GetRequiredService<ILogger<Program>>()
    .LogInformation("Collector starting with plugins: {Plugins}.", string.Join(", ", plugins.Select(p => p.Id)));

host.Run();

/// <summary>Named so the startup log line has a category rather than an anonymous one.</summary>
public partial class Program;
