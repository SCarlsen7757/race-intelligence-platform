using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using RaceIntelligence.Collector.Abstractions;

namespace RaceIntelligence.Collector.Plugins.Live;

/// <summary>
/// Configuration for the live plugin: streaming to the dashboard hub, bound from
/// <c>Collector:Live</c>.
/// </summary>
public sealed class LiveOptions
{
    /// <summary>The configuration section this type binds to.</summary>
    public const string SectionName = $"{CollectorOptions.SectionName}:{LivePlugin.PluginId}";

    /// <summary>
    /// Whether to publish a live view. Default: <see langword="false"/> — it sends this machine's
    /// session to a server other people can watch, which is not something to start doing because
    /// someone upgraded.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Base URL of the dashboard hub. Same trailing-slash and service-discovery rules as
    /// the ingest plugin's.
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
    /// simulator's whole driver array is a much larger copy than one car's telemetry. With this
    /// plugin switched off nothing implements <see cref="IStandingsObserver"/>, and the connector is
    /// told not to read the array at all.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00.020", "00:00:05")]
    public TimeSpan StandingsInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How often to publish the local car's simulator-specific document — car damage and the rest.
    /// Default: 1 Hz.
    /// </summary>
    /// <remarks>
    /// Slower again than <see cref="StandingsInterval"/>. The document costs nothing extra to
    /// produce, since the sample already carries it, but the dashboard parses JSON to read it and
    /// the values inside move on the scale of a race — damage after contact, push-to-pass once a
    /// lap. With this plugin switched off nothing implements <see cref="IExtrasObserver"/> and the
    /// connector is told not to publish extras at all.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00.020", "00:01:00")]
    public TimeSpan ExtrasInterval { get; init; } = TimeSpan.FromSeconds(1);

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
/// Validates the rules that depend on the plugin being switched on, and the one that depends on the
/// collector's own poll rate.
/// </summary>
public sealed class LiveOptionsValidator(IOptions<CollectorOptions> collectorOptions) : IValidateOptions<LiveOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, LiveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            failures.Add("'Collector:Live:BaseUrl' is required when 'Collector:Live:Enabled' is true.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add(
                "'Collector:Live:ApiKey' is required when 'Collector:Live:Enabled' is true. "
                + "Supply it via user secrets or the Collector__Live__ApiKey environment variable.");
        }

        var pollInterval = collectorOptions.Value.PollInterval;
        if (options.StandingsInterval < pollInterval)
        {
            failures.Add(
                $"'Collector:Live:StandingsInterval' ({options.StandingsInterval}) is shorter than "
                + $"'Collector:PollInterval' ({pollInterval}), which cannot produce standings any faster "
                + "than the poll rate. Lower the poll interval instead.");
        }

        if (options.ExtrasInterval < pollInterval)
        {
            failures.Add(
                $"'Collector:Live:ExtrasInterval' ({options.ExtrasInterval}) is shorter than "
                + $"'Collector:PollInterval' ({pollInterval}), which cannot produce extras any faster "
                + "than the poll rate. Lower the poll interval instead.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
