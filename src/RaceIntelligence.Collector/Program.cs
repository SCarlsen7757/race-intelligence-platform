using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using RaceIntelligence.Collector;
using RaceIntelligence.Collector.Buffering;
using RaceIntelligence.Collector.Upload;
using RaceIntelligence.Connectors.RaceRoom;
using RaceIntelligence.Core.Buffering;
using RaceIntelligence.Core.Telemetry;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

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

// Registered as a service so TelemetryUploadService's batch-by-age logic is testable against a
// fake clock instead of real wall-clock sleeps.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<ITelemetryBuffer>(sp =>
{
    var collectorOptions = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<ChannelTelemetryBuffer>>();
    return new ChannelTelemetryBuffer(collectorOptions.BufferCapacity, collectorOptions.BufferFullMode, logger);
});

// CollectorOptions.IngestBaseUrl may be a concrete address (standalone deployment) or a
// service-discovery name such as "https+http://ingest-api/" (Aspire's AppHost injects the latter).
// AddServiceDefaults above already attached both the service-discovery handler and the standard
// resilience handler to every HttpClient via ConfigureHttpClientDefaults, so neither is repeated
// here — adding AddStandardResilienceHandler again would stack a second set of retries on top.
builder.Services.AddHttpClient<IIngestClient, IngestClient>((sp, client) =>
{
    var collectorOptions = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;
    client.BaseAddress = new Uri(collectorOptions.IngestBaseUrl);
    client.DefaultRequestHeaders.Add("X-Api-Key", collectorOptions.ApiKey);
});

// The only simulator-specific lines in this file: everything else here (and every other type in
// this project) depends solely on ITelemetrySource, ITelemetryBuffer, and IIngestClient. Adding a
// second simulator's connector later means adding an alternative registration here — no other
// change to the collector.
//
// RaceRoomTelemetrySource/RaceRoomConnectorOptions carry [SupportedOSPlatform("windows")] (they
// read a Windows named shared-memory block) — CA1416 is suppressed only for this one call site,
// deliberately not for the whole assembly, since every other type in this project (the buffer, the
// ingest client, both hosted services) is genuinely platform-agnostic and must stay callable from
// the (non-Windows-gated) test project.
#pragma warning disable CA1416
builder.Services.AddSingleton<ITelemetrySource>(sp =>
{
    var collectorOptions = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;
    return new RaceRoomTelemetrySource(new RaceRoomConnectorOptions { PollInterval = collectorOptions.PollInterval });
});
#pragma warning restore CA1416

// Order matters: the host stops hosted services in reverse registration order, so the consumer
// (upload) is registered first and the producer (collect) last. That way shutdown stops the
// producer first and leaves the consumer running to drain what is still buffered — the reverse
// would stop the drain first and strand the producer against a full buffer.
builder.Services.AddHostedService<TelemetryUploadService>();
builder.Services.AddHostedService<TelemetryCollectorService>();

var host = builder.Build();
host.Run();
