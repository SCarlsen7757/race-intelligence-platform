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

app.UseCors();
app.UseLiveWebSockets();

app.MapDefaultEndpoints();
app.MapLiveEndpoints();

// This host serves no UI. The dashboard is its own Node service on its own origin
// (src/RaceIntelligence.Dashboard), and the browser opens its socket straight at this hub rather
// than being proxied through it — a proxy would add a second connection and force that event loop
// to re-emit every focus frame sixty times a second, which is latency and jitter bought for
// nothing.
//
// So there is no static-file middleware and no SPA fallback here, and an unmatched request is a
// plain 404. That is worth having on its own: with a fallback, a typo in a fetch URL came back as
// 200 and a page of HTML, which is a far worse thing to debug than a status code.
app.Run();

/// <summary>Entry point partial, exposed so test hosts can reference this assembly's <c>Program</c>.</summary>
public partial class Program;
