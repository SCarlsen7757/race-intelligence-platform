using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using RaceIntelligence.Collector.Abstractions;

namespace RaceIntelligence.Collector.Plugins.Ingest;

/// <summary>
/// Configuration for the archive plugin: batched upload to the ingest API, bound from
/// <c>Collector:Ingest</c>.
/// </summary>
public sealed class IngestOptions
{
    /// <summary>The configuration section this type binds to.</summary>
    public const string SectionName = $"{CollectorOptions.SectionName}:{IngestPlugin.PluginId}";

    /// <summary>
    /// Whether to archive telemetry at all. Default: <see langword="true"/> — permanent storage is
    /// the platform's primary job, so it is the plugin that stays on unless deliberately turned off.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Base URL of the ingest API. Must end with a trailing slash so relative request paths
    /// (<c>api/v1/sessions</c>, ...) combine correctly with <see cref="HttpClient.BaseAddress"/>.
    /// </summary>
    /// <remarks>
    /// Accepts either a concrete address (<c>https://home-server:5443/</c>) for a standalone
    /// deployment, or a service-discovery name (<c>https+http://ingest-api/</c>) when something —
    /// Aspire's AppHost in the local dev loop — resolves logical service names for us.
    /// </remarks>
    [ServiceUrl]
    public string BaseUrl { get; init; } = "https://localhost:5443/";

    /// <summary>
    /// Shared secret sent as the <c>X-Api-Key</c> header on every ingest API request. Never commit
    /// a real value to <c>appsettings.json</c> — supply it via user secrets or an environment
    /// variable on the collecting machine.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Maximum number of samples the local <see cref="Buffering.ChannelTelemetryBuffer"/> holds
    /// before <see cref="BufferFullMode"/> takes effect.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int BufferCapacity { get; init; } = 20_000;

    /// <summary>
    /// How the buffer behaves once <see cref="BufferCapacity"/> is reached. See
    /// <see cref="Buffering.ChannelTelemetryBuffer"/> for the <see cref="BoundedChannelFullMode.Wait"/>
    /// vs. <see cref="BoundedChannelFullMode.DropWrite"/> trade-off. Default: <see cref="BoundedChannelFullMode.Wait"/>.
    /// </summary>
    public BoundedChannelFullMode BufferFullMode { get; init; } = BoundedChannelFullMode.Wait;

    /// <summary>
    /// Maximum number of samples <see cref="Upload.TelemetryUploadService"/> puts in a single
    /// upload batch before flushing, regardless of age.
    /// </summary>
    [Range(1, 10_000)]
    public int MaxBatchSize { get; init; } = 500;

    /// <summary>
    /// Maximum time a batch is allowed to sit open before <see cref="Upload.TelemetryUploadService"/>
    /// flushes it, regardless of size — guarantees a slow-filling batch still uploads promptly.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:01:00")]
    public TimeSpan MaxBatchAge { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Validates the rules that depend on the plugin being switched on, which cannot be expressed as
/// attributes.
/// </summary>
/// <remarks>
/// Registered only when the plugin is enabled, so it never has to ask. That is the point of a plugin
/// owning its own configuration: an endpoint the collector will never call must not be able to fail
/// startup for a collector that is only publishing live.
/// </remarks>
public sealed class IngestOptionsValidator : IValidateOptions<IngestOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, IngestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            failures.Add("'Collector:Ingest:BaseUrl' is required when 'Collector:Ingest:Enabled' is true.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add(
                "'Collector:Ingest:ApiKey' is required when 'Collector:Ingest:Enabled' is true. "
                + "Supply it via user secrets or the Collector__Ingest__ApiKey environment variable.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
