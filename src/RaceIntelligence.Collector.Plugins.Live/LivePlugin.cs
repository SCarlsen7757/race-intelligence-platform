using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIntelligence.Collector.Abstractions;

namespace RaceIntelligence.Collector.Plugins.Live;

/// <summary>
/// Publishes a live view to the dashboard hub for a race engineer to watch: the path whose defining
/// property is that it must never show a stale value.
/// </summary>
/// <remarks>
/// Conflating and newest-wins, the exact opposite of the archive plugin. Every hop keeps the newest
/// frame and drops the rest, and a socket that drops resumes with the current race rather than
/// replaying where the cars used to be. Nothing here is buffered, because a live value is worthless
/// the moment a fresher one exists.
/// </remarks>
public sealed class LivePlugin : ITelemetryPlugin
{
    /// <summary>The plugin's id, and the name of its configuration block under <c>Collector</c>.</summary>
    public const string PluginId = "Live";

    /// <summary>
    /// The simulator this collector is reading, sent in the hub handshake so the dashboard can pick
    /// sim-specific panels.
    /// </summary>
    public required string GameKey { get; init; }

    /// <summary>The connector's declared <c>SimCapabilities</c>, sent in the same handshake.</summary>
    public required ulong Capabilities { get; init; }

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public void Register(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOptions<LiveOptions>()
            .Bind(builder.Configuration.GetSection(LiveOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSingleton<IValidateOptions<LiveOptions>, LiveOptionsValidator>();

        builder.Services.AddSingleton<LiveOutbox>();
        builder.Services.AddSingleton<ILiveOutbox>(sp => sp.GetRequiredService<LiveOutbox>());

        string gameKey = GameKey;
        ulong capabilities = Capabilities;
        builder.Services.AddSingleton<ILiveConnectionFactory>(sp => new LiveWebSocketConnectionFactory(
            sp.GetRequiredService<IOptions<LiveOptions>>(),
            sp.GetRequiredService<ILogger<LiveWebSocketConnectionFactory>>())
        {
            GameKey = gameKey,
            Capabilities = capabilities,
        });

        builder.Services.AddSingleton<LiveObserver>();
        builder.Services.AddSingleton<ISessionObserver>(sp => sp.GetRequiredService<LiveObserver>());
        builder.Services.AddSingleton<ISampleObserver>(sp => sp.GetRequiredService<LiveObserver>());
        builder.Services.AddSingleton<IStandingsObserver>(sp => sp.GetRequiredService<LiveObserver>());
        builder.Services.AddSingleton<ISlowChannelObserver>(sp => sp.GetRequiredService<LiveObserver>());

        builder.Services.AddHostedService<LivePublishService>();
    }
}
