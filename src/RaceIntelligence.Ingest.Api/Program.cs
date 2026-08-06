using Microsoft.EntityFrameworkCore;
using Npgsql;
using RaceIntelligence.Ingest.Api.Endpoints;
using RaceIntelligence.Persistence;
using RaceIntelligence.Persistence.Bulk;
using RaceIntelligence.Persistence.Repositories;
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

// Deliberately NOT Aspire.Npgsql.* client integration: the ingest API is the only service in this
// platform allowed to hold database credentials, and it owns its own plain UseNpgsql wiring rather
// than pulling in Aspire's opinionated health-check/telemetry package for a connection it already
// fully controls.
var connectionString = builder.Configuration.GetConnectionString("raceintel")
    ?? throw new InvalidOperationException("Connection string 'raceintel' is not configured.");

builder.Services.AddDbContext<RaceIntelligenceDbContext>(options => options.UseNpgsql(connectionString));

// NpgsqlDataSource is the bulk telemetry writer's own connection source, kept separate from the
// DbContext because it never touches EF's change tracker (see NpgsqlTelemetryWriter remarks).
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<NpgsqlTelemetryWriter>();

builder.Services.AddScoped<GameRepository>();
builder.Services.AddScoped<TrackRepository>();
builder.Services.AddScoped<CarRepository>();
builder.Services.AddScoped<DriverRepository>();

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapDefaultEndpoints();

// Aspire's inner loop expects a fresh dev database to already be up to date; production
// deployments apply migrations out-of-band (a deliberate, explicit step) rather than at process
// startup.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<RaceIntelligenceDbContext>();
    await db.Database.MigrateAsync();
}

app.MapSessionEndpoints();
app.MapTelemetryEndpoints();

app.Run();

/// <summary>Entry point partial, exposed so <c>Microsoft.AspNetCore.Mvc.Testing</c>/Aspire test hosts can reference this assembly's <c>Program</c>.</summary>
public partial class Program;
