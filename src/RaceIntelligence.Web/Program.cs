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

app.Run();

/// <summary>Entry point partial, exposed so test hosts can reference this assembly's <c>Program</c>.</summary>
public partial class Program;
