using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;

namespace RaceIntelligence.Collector;

/// <summary>
/// Tuning knobs and endpoint configuration for the collector worker, bound from the
/// <c>Collector</c> configuration section.
/// </summary>
/// <remarks>
/// Registered with <c>ValidateDataAnnotations().ValidateOnStart()</c> in <c>Program.cs</c>, so a
/// misconfigured collector (missing API key, non-existent ingest URL shape, absurd batch settings)
/// fails fast at startup rather than silently dropping telemetry once a race is underway.
/// </remarks>
public sealed class CollectorOptions
{
    /// <summary>The configuration section this type binds to.</summary>
    public const string SectionName = "Collector";

    /// <summary>
    /// Base URL of the ingest API. Must end with a trailing slash so relative request paths
    /// (<c>api/v1/sessions</c>, ...) combine correctly with <see cref="HttpClient.BaseAddress"/>.
    /// </summary>
    /// <remarks>
    /// Accepts either a concrete address (<c>https://home-server:5443/</c>) for a standalone
    /// deployment, or a service-discovery name (<c>https+http://ingest-api/</c>) when something —
    /// Aspire's AppHost in the local dev loop — resolves logical service names for us. The latter is
    /// why plain <c>[Url]</c> validation is not used here: it rejects any scheme that isn't
    /// http/https/ftp, including the <c>https+http</c> scheme-preference form.
    /// </remarks>
    [Required]
    [ServiceUrl]
    public string IngestBaseUrl { get; init; } = "https://localhost:5443/";

    /// <summary>
    /// Shared secret sent as the <c>X-Api-Key</c> header on every ingest API request. Never commit
    /// a real value to <c>appsettings.json</c> — supply it via user secrets or an environment
    /// variable on the collecting machine.
    /// </summary>
    [Required]
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>How often the connector polls the simulator's telemetry API. Default: 60 Hz.</summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "00:00:01")]
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1.0 / 60.0);

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
/// Validates a base address that may be either a concrete URL or a service-discovery name: an
/// absolute URI with an authority, ending in a trailing slash. Deliberately does not constrain the
/// scheme — <c>https+http</c> (service discovery's "prefer https, accept http" form) is as valid
/// here as plain <c>https</c>, and <see cref="UrlAttribute"/> rejects it.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
internal sealed class ServiceUrlAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is not string text)
        {
            // Absent/non-string values are [Required]'s business, not this attribute's.
            return true;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && !string.IsNullOrEmpty(uri.Authority)
            && text.EndsWith('/');
    }

    public override string FormatErrorMessage(string name) =>
        $"'{name}' must be an absolute base address with a trailing slash, e.g. 'https://home-server:5443/' or 'https+http://ingest-api/'.";
}
