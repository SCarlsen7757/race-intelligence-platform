using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace RaceIntelligence.Collector;

/// <summary>
/// Tuning knobs and endpoint configuration for the collector worker, bound from the
/// <c>Collector</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// The collector does two independent jobs, and either can run without the other. It
/// <b>archives</b> telemetry to the ingest API for permanent storage, and it <b>publishes</b> a
/// live view to the dashboard hub. A driver may want both (the normal case), archiving only (no
/// engineer watching), or publishing only (a one-off session nobody wants stored). Each is
/// configured in its own block and switched independently — see <see cref="Ingest"/> and
/// <see cref="Live"/>.
/// </para>
/// <para>
/// Validated by <see cref="CollectorOptionsValidator"/> with <c>ValidateOnStart</c>, so a
/// misconfigured collector fails at startup rather than silently dropping telemetry once a race is
/// under way. Validation is conditional on which blocks are enabled: a publish-only collector must
/// not be forced to supply an ingest API key it will never use.
/// </para>
/// </remarks>
public sealed class CollectorOptions
{
    /// <summary>The configuration section this type binds to.</summary>
    public const string SectionName = "Collector";

    /// <summary>How often the connector polls the simulator's telemetry API. Default: 60 Hz.</summary>
    /// <remarks>
    /// At the root rather than inside a block because it feeds both jobs: it is the rate at which
    /// samples are captured for the archive <i>and</i> the rate at which the focus stream updates.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00.001", "00:00:01")]
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1.0 / 60.0);

    /// <summary>Archiving telemetry to the ingest API for permanent storage.</summary>
    public IngestOptions Ingest { get; init; } = new();

    /// <summary>Publishing a live view to the dashboard hub.</summary>
    public LiveOptions Live { get; init; } = new();
}

/// <summary>Configuration for the collector's archive path: batched upload to the ingest API.</summary>
public sealed class IngestOptions
{
    /// <summary>
    /// Whether to archive telemetry at all. Default: <see langword="true"/> — permanent storage is
    /// the platform's primary job, so it is the half that stays on unless deliberately turned off.
    /// </summary>
    public bool Enabled { get; init; } = true;

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

/// <summary>Configuration for the collector's live path: streaming to the dashboard hub.</summary>
public sealed class LiveOptions
{
    /// <summary>
    /// Whether to publish a live view. Default: <see langword="false"/> — it sends this machine's
    /// session to a server other people can watch, which is not something to start doing because
    /// someone upgraded.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Base URL of the dashboard hub. Same trailing-slash and service-discovery rules as
    /// <see cref="IngestOptions.BaseUrl"/>.
    /// </summary>
    /// <remarks>
    /// An <c>http</c>/<c>https</c> address, not <c>ws</c>/<c>wss</c>: the publishing endpoint is
    /// reached by an HTTP upgrade, and writing the scheme the way every other URL in the
    /// configuration is written keeps service discovery working. The scheme is switched to
    /// <c>ws</c>/<c>wss</c> when the socket is opened.
    /// </remarks>
    [ServiceUrl]
    public string BaseUrl { get; init; } = "https://localhost:5444/";

    /// <summary>Shared secret sent as the <c>X-Api-Key</c> header when opening the publishing socket.</summary>
    /// <remarks>
    /// Publishing is authenticated even though viewing the dashboard is not. The asymmetry is
    /// deliberate: anyone may watch, but only a known collector may claim to <i>be</i> a driver's
    /// telemetry, or a stranger could inject a fictional car into someone's timing tower.
    /// </remarks>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Stable identity for this installation, so a reconnecting collector is recognised as the same
    /// publisher rather than appearing alongside its own stale entry.
    /// </summary>
    /// <remarks>
    /// Left empty, a random id is generated at startup — fine for a single run, but it means every
    /// restart looks like a new client. Set it once per machine for a dashboard that stays tidy.
    /// </remarks>
    public Guid? ClientId { get; init; }

    /// <summary>
    /// Human-readable label for this machine, shown in the dashboard's client list. Defaults to the
    /// machine name.
    /// </summary>
    public string? ClientName { get; init; }

    /// <summary>
    /// How often to re-read and publish the whole field's scoring data — the timing tower's rate.
    /// Default: 10 Hz.
    /// </summary>
    /// <remarks>
    /// Deliberately far slower than <see cref="CollectorOptions.PollInterval"/>. Positions, lap and
    /// sector times and gaps do not change meaningfully between two 60 Hz frames, and reading the
    /// simulator's whole driver array is a much larger copy than one car's telemetry. When
    /// <see cref="Enabled"/> is <see langword="false"/> the connector is told not to read it at all.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00.020", "00:00:05")]
    public TimeSpan StandingsInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>How long to wait before the first reconnect attempt after the socket drops.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:01:00")]
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The longest the backoff between reconnect attempts is allowed to grow to.
    /// </summary>
    /// <remarks>
    /// Bounded so a hub that was down for an hour is picked back up within
    /// <see cref="MaxReconnectDelay"/> of returning, rather than after an exponential delay that has
    /// grown past the length of the race.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Validates <see cref="CollectorOptions"/>, including the rules that depend on which jobs are
/// enabled and so cannot be expressed as attributes.
/// </summary>
public sealed class CollectorOptionsValidator : IValidateOptions<CollectorOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CollectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!options.Ingest.Enabled && !options.Live.Enabled)
        {
            failures.Add(
                "Both 'Collector:Ingest:Enabled' and 'Collector:Live:Enabled' are false, so the collector "
                + "would read the simulator and do nothing with it. Enable at least one.");
        }

        if (options.Ingest.Enabled)
        {
            ValidateEndpoint("Ingest", options.Ingest.BaseUrl, options.Ingest.ApiKey, failures);
        }

        if (options.Live.Enabled)
        {
            ValidateEndpoint("Live", options.Live.BaseUrl, options.Live.ApiKey, failures);
        }

        if (options.Live.Enabled && options.Live.StandingsInterval < options.PollInterval)
        {
            failures.Add(
                $"'Collector:Live:StandingsInterval' ({options.Live.StandingsInterval}) is shorter than "
                + $"'Collector:PollInterval' ({options.PollInterval}), which cannot produce standings any faster "
                + "than the poll rate. Lower the poll interval instead.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    /// <remarks>
    /// The URL shape is also covered by <see cref="ServiceUrlAttribute"/>, but only the enabled
    /// block's is worth reporting: a publish-only collector should not be told its unused ingest
    /// URL is malformed. Repeating the check here is what lets the attribute stay silent for a
    /// disabled block.
    /// </remarks>
    private static void ValidateEndpoint(string block, string baseUrl, string apiKey, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            failures.Add($"'Collector:{block}:BaseUrl' is required when 'Collector:{block}:Enabled' is true.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            failures.Add(
                $"'Collector:{block}:ApiKey' is required when 'Collector:{block}:Enabled' is true. "
                + $"Supply it via user secrets or the Collector__{block}__ApiKey environment variable.");
        }
    }
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

        if (text.Length == 0)
        {
            // An unset URL in a disabled block is not an error; CollectorOptionsValidator reports
            // it only when the block that owns it is switched on.
            return true;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && !string.IsNullOrEmpty(uri.Authority)
            && text.EndsWith('/');
    }

    public override string FormatErrorMessage(string name) =>
        $"'{name}' must be an absolute base address with a trailing slash, e.g. 'https://home-server:5443/' or 'https+http://ingest-api/'.";
}
