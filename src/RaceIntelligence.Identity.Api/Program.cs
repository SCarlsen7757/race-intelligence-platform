using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Identity;
using RaceIntelligence.Identity.Api.Endpoints;
using RaceIntelligence.Identity.Repositories;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Service discovery, resilience, health checks (/health, /alive), and OpenTelemetry.
builder.AddServiceDefaults();

// RFC 7807 ProblemDetails for unhandled errors and the framework's own 4xx responses.
builder.Services.AddProblemDetails();

// The registry's own database, and only its own. This service never learns a connection string for
// any simulator's telemetry store — see IdentityDbContext for why the separation is the point
// rather than an accident of deployment.
var connectionString = builder.Configuration.GetConnectionString("identity")
    ?? throw new InvalidOperationException("Connection string 'identity' is not configured.");

builder.Services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<PersonRepository>();

// Injected rather than reached for statically, so a test can assert on the timestamps a claim was
// written with instead of on "roughly now".
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RaceIntelligence.Identity.Api.Auth.IdentityApiKeyGate>();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapDefaultEndpoints();

// The same rule the ingest API follows: the Aspire inner loop expects a fresh dev database to be up
// to date, and production applies migrations out-of-band as a deliberate step.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await db.Database.MigrateAsync();
}

app.MapPersonEndpoints();

app.Run();

/// <summary>Entry point partial, exposed so test hosts can reference this assembly's <c>Program</c>.</summary>
public partial class Program;
