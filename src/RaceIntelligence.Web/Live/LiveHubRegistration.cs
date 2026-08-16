using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// Registers the live hub's services.
/// </summary>
/// <remarks>
/// Extracted from <c>Program.cs</c> so the end-to-end tests build the hub from the same code the
/// application does. A test that re-declared these registrations could pass against a graph the
/// real host never assembles, which would make it evidence of nothing.
/// </remarks>
public static class LiveHubRegistration
{
    /// <summary>Adds the live hub's options, singletons and background sweeper.</summary>
    public static IServiceCollection AddLiveHub(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Validated at startup rather than on first resolution: a hub that starts without a
        // publishing key looks healthy and silently refuses every collector. ValidateOnStart turns
        // that into a failure to boot, which is the honest outcome.
        services.AddOptions<LiveHubOptions>()
            .Bind(configuration.GetSection(LiveHubOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // TryAdd so a test can substitute a controllable clock before calling this.
        services.TryAddSingleton(TimeProvider.System);

        // The hub is a singleton graph holding all live state in memory. Nothing is persisted: a
        // live frame is superseded within milliseconds, the archive path already stores what is
        // worth keeping, and a restart mid-race costs a reconnect rather than data.
        services.AddSingleton<LiveViewerRegistry>();
        services.AddSingleton<LiveRoomRegistry>();
        services.AddSingleton<LiveApiKeyGate>();
        services.AddHostedService<LiveRoomJanitor>();

        // Neither session type holds per-connection state — that lives in LiveViewer and the room
        // registry — so one instance serves every connection.
        services.AddSingleton<PublisherSession>();
        services.AddSingleton<ViewerSession>();

        return services;
    }

    /// <summary>
    /// Turns upgrade requests into WebSockets. Must run before the live endpoints are mapped.
    /// </summary>
    /// <remarks>
    /// The keep-alive matters more than it looks. Both sockets can be legitimately silent — a
    /// collector between sessions, a viewer watching a room nobody is publishing to — and the tunnel
    /// and any proxy in front of it will drop a connection that has carried nothing for long enough.
    /// Pings keep it open without the application inventing a heartbeat message of its own.
    /// </remarks>
    public static void UseLiveWebSockets(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
    }
}
