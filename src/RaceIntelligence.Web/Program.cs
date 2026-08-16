using RaceIntelligence.Web.Live;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Service discovery, resilience, health checks (/health, /alive), and OpenTelemetry.
builder.AddServiceDefaults();

builder.Services.AddProblemDetails();

builder.Services.AddLiveHub(builder.Configuration);

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseLiveWebSockets();

app.MapDefaultEndpoints();
app.MapLiveEndpoints();

// The dashboard, built by Vite into wwwroot. Serving it from the same origin as the live socket is
// what keeps the deployment to one tunnel and one hostname, and means the browser needs no CORS
// preflight and no configured API address — it opens a socket back to wherever it was loaded from.
//
// In development the SPA is normally run from the Vite dev server instead, for hot reload; that
// server proxies /live and /api here. Both work, and neither is required for the hub itself to run
// — a wwwroot that was never built simply serves nothing, which is why this is not conditional.
app.UseDefaultFiles();
app.UseStaticFiles();

// An unmatched request under the API or socket prefixes is a 404, not the dashboard. Registered
// ahead of the SPA fallback because it is the more specific pattern: without it a typo in a fetch
// URL would come back as 200 with a page of HTML, which is a far worse thing to debug than a
// status code.
app.MapFallback("/api/{**rest}", () => Results.NotFound());
app.MapFallback("/live/{**rest}", () => Results.NotFound());

// Client-side routing: a deep link such as /room/abc123 is not a file on disk and must return
// index.html rather than a 404.
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Entry point partial, exposed so test hosts can reference this assembly's <c>Program</c>.</summary>
public partial class Program;
