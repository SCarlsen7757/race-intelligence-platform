using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

        // The dashboard is a separate origin now, so the room list is a cross-origin read and needs
        // saying so explicitly. Same origin list as the socket check below: they are two halves of
        // one answer to "which page may look at this hub", and letting them drift would mean a
        // dashboard whose first paint works and whose socket does not, or the reverse.
        //
        // No credentials, because the viewing side carries none — it is open by design, and
        // AllowCredentials would additionally forbid the wildcard-free list from ever being
        // relaxed for a quick diagnosis.
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<LiveHubOptions>>((cors, live) =>
            cors.AddPolicy(DashboardCorsPolicy, policy => policy
                .WithOrigins(live.Value.AllowedOrigins)
                .WithMethods("GET")
                .AllowAnyHeader()));

        return services;
    }

    /// <summary>The CORS policy the room list is served under.</summary>
    public const string DashboardCorsPolicy = "dashboard";

    /// <summary>
    /// Turns upgrade requests into WebSockets. Must run before the live endpoints are mapped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keep-alive matters more than it looks. Both sockets can be legitimately silent — a
    /// collector between sessions, a viewer watching a room nobody is publishing to — and the tunnel
    /// and any proxy in front of it will drop a connection that has carried nothing for long enough.
    /// Pings keep it open without the application inventing a heartbeat message of its own.
    /// </para>
    /// <para>
    /// <b><see cref="WebSocketOptions.AllowedOrigins"/> is set here because leaving it unset means
    /// accepting every origin.</b> The viewing socket is open — no key — so before the dashboard
    /// moved to its own origin the only thing keeping a hostile page from opening one was that
    /// nobody had tried. Now that the dashboard's origin is a configured fact, it is stated.
    /// </para>
    /// <para>
    /// A request with no <c>Origin</c> header is still accepted, which is what keeps the collector
    /// working: it is not a browser and sends none. That is the correct shape of the check — origin
    /// is a browser's self-report about which page opened the connection, and it means nothing at
    /// all coming from a program.
    /// </para>
    /// </remarks>
    public static void UseLiveWebSockets(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) };

        foreach (string origin in app.Services.GetRequiredService<IOptions<LiveHubOptions>>().Value.AllowedOrigins)
        {
            options.AllowedOrigins.Add(origin);
        }

        app.UseWebSockets(options);
    }
}
