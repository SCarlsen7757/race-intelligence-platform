using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIntelligence.Collector.Abstractions;
using RaceIntelligence.Collector.Plugins.Ingest.Buffering;
using RaceIntelligence.Collector.Plugins.Ingest.Upload;
using RaceIntelligence.Core.Buffering;

namespace RaceIntelligence.Collector.Plugins.Ingest;

/// <summary>
/// Archives telemetry to the ingest API for permanent storage: the platform's primary job, and the
/// path whose defining property is that it must not lose a sample.
/// </summary>
/// <remarks>
/// Buffered, ordered and retried. Samples go into a bounded channel and leave it in batches, so a
/// momentary network stall costs latency rather than data. Session and lap bookkeeping is done
/// inline because it is rare; the sample stream never is.
/// </remarks>
public sealed class IngestPlugin : ITelemetryPlugin
{
    /// <summary>The plugin's id, and the name of its configuration block under <c>Collector</c>.</summary>
    public const string PluginId = "Ingest";

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public void Register(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOptions<IngestOptions>()
            .Bind(builder.Configuration.GetSection(IngestOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSingleton<IValidateOptions<IngestOptions>, IngestOptionsValidator>();

        // Lets the end-of-session drain see how many samples are sitting in the uploader's
        // not-yet-flushed batch — samples that have left the buffer but are not uploaded yet.
        builder.Services.AddSingleton<OpenBatchTracker>();

        builder.Services.AddSingleton<ITelemetryBuffer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<IngestOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<ChannelTelemetryBuffer>>();
            return new ChannelTelemetryBuffer(options.BufferCapacity, options.BufferFullMode, logger);
        });

        // BaseUrl may be a concrete address (standalone deployment) or a service-discovery name such
        // as "https+http://ingest-api/" (Aspire's AppHost injects the latter). AddServiceDefaults
        // already attached both the service-discovery handler and the standard resilience handler to
        // every HttpClient via ConfigureHttpClientDefaults, so neither is repeated here — adding
        // AddStandardResilienceHandler again would stack a second set of retries.
        builder.Services.AddHttpClient<IIngestClient, IngestClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<IngestOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
        });

        builder.Services.AddSingleton<IngestObserver>();
        builder.Services.AddSingleton<ISessionObserver>(sp => sp.GetRequiredService<IngestObserver>());
        builder.Services.AddSingleton<ISampleObserver>(sp => sp.GetRequiredService<IngestObserver>());

        // Both are registered before the collect loop, and the host stops hosted services in reverse
        // registration order — so on shutdown the loop stops first and these keep running to drain
        // what is still pending. The reverse would stop the drain first and strand the producer
        // against a full buffer.
        builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestObserver>());
        builder.Services.AddHostedService<TelemetryUploadService>();
    }
}
