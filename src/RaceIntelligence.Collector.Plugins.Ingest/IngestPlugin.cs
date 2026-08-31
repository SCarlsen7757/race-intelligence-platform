using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using RaceIntelligence.Collector.Abstractions;
using RaceIntelligence.Collector.Plugins.Ingest.Buffering;
using RaceIntelligence.Collector.Plugins.Ingest.Upload;
using RaceIntelligence.Collector.Abstractions.Buffering;

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
        builder.Services.AddSingleton<LatestOperatingWindows>();

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
        // The key is read once here and baked into DefaultRequestHeaders, so changing it — a
        // rotation, or a key the server has revoked — takes a collector restart, not a config
        // reload. That was invisible while the server held one immutable shared key; now that the
        // server holds a revocable key per collector, it is an operational limit worth knowing.
        //
        // HTTP/2 is requested but not required, and it is worth being precise about where that
        // actually applies. A remote collector reaches the ingest API through the tunnel over TLS,
        // where ALPN negotiates h2 — that is the leg this is for, because per-request connection
        // setup on a home uplink is real cost in a way it never was on a LAN. A collector on the
        // LAN talks to the container's cleartext endpoint, which serves HTTP/1.1 only (nothing in
        // this repo configures Kestrel, and its default for a non-TLS endpoint is HTTP/1.1); there
        // is no h2c upgrade negotiation, so RequestVersionOrLower quietly stays on 1.1 and this
        // setting is a no-op there. That asymmetry is intended. Forcing the LAN leg to h2c would
        // mean RequestVersionExact on this client, which would turn any plaintext or
        // proxy-terminated hop into a failure instead of a downgrade.
        builder.Services.AddHttpClient<IIngestClient, IngestClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<IngestOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
            client.DefaultRequestVersion = HttpVersion.Version20;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        });

        // Until now every resilience number this client used was an invisible framework default —
        // 10s per attempt, 30s in total — chosen by nobody and never examined against anything but
        // a LAN. They are too tight for a collector on a home uplink: a 500-sample batch during a
        // backlog drain is the largest payload this client sends, and it is sent exactly when the
        // uplink is already the bottleneck, so a 10s attempt can expire on the body write and three
        // retries of a doomed upload then burn the 30s total before anything lands.
        //
        // Configured through the standard handler's named options rather than by calling
        // AddStandardResilienceHandler again — AddServiceDefaults already attached it to every
        // client via ConfigureHttpClientDefaults, and adding a second would stack a second set of
        // retries. The "-standard" suffix is the pipeline name that handler registers for a client,
        // so these override that client's options and no other's; the hub and read API clients are
        // on a LAN and keep the tighter defaults. IngestPluginRegistrationTests asserts a request
        // actually gives up on this schedule, because binding the wrong name here would leave the
        // framework defaults silently in force rather than fail.
        builder.Services.Configure<HttpStandardResilienceOptions>(
            $"{nameof(IIngestClient)}-standard",
            resilience =>
            {
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);

                // Must stay at least twice the attempt timeout or the options fail validation, and
                // the ratio is the point: a window shorter than a couple of attempts would trip the
                // breaker on one slow upload rather than on a pattern.
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            });

        builder.Services.AddSingleton<IngestObserver>();
        builder.Services.AddSingleton<ISessionObserver>(sp => sp.GetRequiredService<IngestObserver>());
        builder.Services.AddSingleton<ISampleObserver>(sp => sp.GetRequiredService<IngestObserver>());
        builder.Services.AddSingleton<ISlowChannelObserver>(sp => sp.GetRequiredService<IngestObserver>());

        // Both are registered before the collect loop, and the host stops hosted services in reverse
        // registration order — so on shutdown the loop stops first and these keep running to drain
        // what is still pending. The reverse would stop the drain first and strand the producer
        // against a full buffer.
        builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestObserver>());
        builder.Services.AddHostedService<TelemetryUploadService>();
    }
}
