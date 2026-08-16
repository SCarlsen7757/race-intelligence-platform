using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using RaceIntelligence.Collector;
using RaceIntelligence.Collector.Buffering;
using RaceIntelligence.Collector.Live;
using RaceIntelligence.Collector.Upload;
using RaceIntelligence.Connectors.RaceRoom;
using RaceIntelligence.Core.Buffering;
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

// The rules that depend on which jobs are switched on — an ingest key is required only when
// archiving is enabled, and enabling neither job is a misconfiguration — cannot be expressed as
// attributes, so they live in a validator alongside them.
builder.Services.AddSingleton<IValidateOptions<CollectorOptions>, CollectorOptionsValidator>();

// Registered as a service so TelemetryUploadService's batch-by-age logic is testable against a
// fake clock instead of real wall-clock sleeps.
builder.Services.AddSingleton(TimeProvider.System);

// Read once here rather than per-registration: the two blocks below decide which halves of the
// collector exist at all, and re-resolving options to answer the same question repeatedly invites
// them to disagree.
var collectorOptions = builder.Configuration
    .GetSection(CollectorOptions.SectionName)
    .Get<CollectorOptions>() ?? new CollectorOptions();

if (collectorOptions.Ingest.Enabled)
{
    // Lets the collector see how many samples are sitting in the uploader's not-yet-flushed batch —
    // samples that have already left the buffer but are not uploaded yet.
    builder.Services.AddSingleton<OpenBatchTracker>();

    builder.Services.AddSingleton<ITelemetryBuffer>(sp =>
    {
        var options = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;
        var logger = sp.GetRequiredService<ILogger<ChannelTelemetryBuffer>>();
        return new ChannelTelemetryBuffer(options.Ingest.BufferCapacity, options.Ingest.BufferFullMode, logger);
    });

    // Collector:Ingest:BaseUrl may be a concrete address (standalone deployment) or a
    // service-discovery name such as "https+http://ingest-api/" (Aspire's AppHost injects the
    // latter). AddServiceDefaults above already attached both the service-discovery handler and the
    // standard resilience handler to every HttpClient via ConfigureHttpClientDefaults, so neither is
    // repeated here — adding AddStandardResilienceHandler again would stack a second set of retries.
    builder.Services.AddHttpClient<IIngestClient, IngestClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;
        client.BaseAddress = new Uri(options.Ingest.BaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", options.Ingest.ApiKey);
    });
}
else
{
    // A publish-only collector still runs the same collect loop, which posts sessions and laps
    // through these. Null implementations keep that loop free of "is archiving on?" branches — the
    // decision is made once, here.
    builder.Services.AddSingleton<OpenBatchTracker>();
    builder.Services.AddSingleton<ITelemetryBuffer, NullTelemetryBuffer>();
    builder.Services.AddSingleton<IIngestClient, NullIngestClient>();
}

if (collectorOptions.Live.Enabled)
{
    builder.Services.AddSingleton<LiveOutbox>();
    builder.Services.AddSingleton<ILiveOutbox>(sp => sp.GetRequiredService<LiveOutbox>());
}
else
{
    builder.Services.AddSingleton<ILiveOutbox, NullLiveOutbox>();
}

// The only simulator-specific lines in this file: everything else here (and every other type in
// this project) depends solely on ITelemetrySource, ITelemetryBuffer, ILiveOutbox and IIngestClient.
// Adding a second simulator's connector later means adding an alternative registration here — no
// other change to the collector.
//
// RaceRoomTelemetrySource/RaceRoomConnectorOptions carry [SupportedOSPlatform("windows")] (they
// read a Windows named shared-memory block) — CA1416 is suppressed only for this one call site,
// deliberately not for the whole assembly, since every other type in this project (the buffer, the
// ingest client, both hosted services) is genuinely platform-agnostic and must stay callable from
// the (non-Windows-gated) test project.
#pragma warning disable CA1416
const string GameKey = "raceroom";
var connectorCapabilities = RaceRoomTelemetrySource.DeclaredCapabilities;

builder.Services.AddSingleton<ITelemetrySource>(sp =>
{
    var options = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;
    return new RaceRoomTelemetrySource(new RaceRoomConnectorOptions
    {
        PollInterval = options.PollInterval,

        // Reading the simulator's whole driver array is the most expensive thing the connector
        // does, and standings exist only to be published. With nothing publishing, the connector is
        // told not to read it at all rather than to read it and have the result discarded.
        StandingsInterval = options.Live.Enabled ? options.Live.StandingsInterval : Timeout.InfiniteTimeSpan,
    });
});
#pragma warning restore CA1416

if (collectorOptions.Live.Enabled)
{
    builder.Services.AddSingleton<ILiveConnectionFactory>(sp => new LiveWebSocketConnectionFactory(
        sp.GetRequiredService<IOptions<CollectorOptions>>(),
        sp.GetRequiredService<ILogger<LiveWebSocketConnectionFactory>>())
    {
        GameKey = GameKey,
        Capabilities = (ulong)connectorCapabilities,
    });
}

// Order matters: the host stops hosted services in reverse registration order, so the consumers
// (upload, publish) are registered first and the producer (collect) last. That way shutdown stops
// the producer first and leaves the consumers running to drain what is still pending — the reverse
// would stop the drain first and strand the producer against a full buffer.
if (collectorOptions.Ingest.Enabled)
{
    builder.Services.AddHostedService<TelemetryUploadService>();
}

if (collectorOptions.Live.Enabled)
{
    builder.Services.AddHostedService<LivePublishService>();
}

builder.Services.AddHostedService<TelemetryCollectorService>();

var host = builder.Build();
host.Run();
