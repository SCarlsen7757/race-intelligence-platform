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
    .Enrich.FromLogContext());

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

builder.Services
    .AddHttpClient<IIngestClient, IngestClient>((sp, client) =>
    {
        var collectorOptions = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;
        client.BaseAddress = new Uri(collectorOptions.IngestBaseUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", collectorOptions.ApiKey);
    })
    .AddStandardResilienceHandler();

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

builder.Services.AddHostedService<TelemetryCollectorService>();
builder.Services.AddHostedService<TelemetryUploadService>();

var host = builder.Build();
host.Run();
