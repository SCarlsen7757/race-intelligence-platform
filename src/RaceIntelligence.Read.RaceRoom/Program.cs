using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Core;
using RaceIntelligence.Persistence.Core.Repositories;
using RaceIntelligence.Persistence.RaceRoom;
using RaceIntelligence.Read.Api.Endpoints;
using RaceIntelligence.Read.RaceRoom;
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

// Absent fields are omitted rather than written as null, the same convention the live wire already
// follows (RaceIntelligence.Live.Contracts.View.LiveViewJson) and the one the dashboard's
// hand-mirrored contracts are written against — they type an unreported channel as optional, which
// is only true if it is actually absent.
//
// It is not only tidiness. Most of a telemetry sample is nullable and most laps are untimed, so
// `"lapTimeMs": null` on every row is bytes on the wire for no information, and a client checking
// `!== undefined` reads a present null as a real value. That mismatch is exactly the bug this
// setting removes rather than documents.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

// RaceRoom's telemetry store, read-only. The connection string is the same one the ingest host uses
// — one database per simulator (ADR 0001) — but this process reaches it through a different door:
// it never writes, never migrates, and holds no API key.
var connectionString = builder.Configuration.GetConnectionString("raceintel")
    ?? throw new InvalidOperationException("Connection string 'raceintel' is not configured.");

// The concrete store, registered as the shape the endpoints know. As on the ingest host, these two
// lines are the whole of what makes this RaceRoom's read API rather than any other simulator's.
builder.Services.AddDbContext<RaceRoomDbContext>(options => options
    .UseNpgsql(connectionString)
    // Nothing here saves, so the change tracker would only ever accumulate. The repositories say
    // AsNoTracking on every query as well; this makes forgetting it harmless rather than slow.
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
builder.Services.AddScoped<TelemetryDbContext>(sp => sp.GetRequiredService<RaceRoomDbContext>());

builder.Services.AddScoped<SessionReadRepository>();
builder.Services.AddScoped<TelemetryReadRepository>();

// Which origins may read this, validated at startup rather than on the first request — a misconfigured
// allowlist should stop the container, not surface as a dashboard that loads and then cannot fetch.
builder.Services.AddOptions<ReadApiOptions>()
    .Bind(builder.Configuration.GetSection(ReadApiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Same shape as the hub's dashboard policy, and the same reasoning: an explicit origin list, GET
// only, and no credentials — the reading side carries none, and AllowCredentials would additionally
// forbid the list from ever being relaxed for a quick diagnosis.
builder.Services.AddCors();
builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
    .Configure<Microsoft.Extensions.Options.IOptions<ReadApiOptions>>((cors, read) =>
        cors.AddDefaultPolicy(policy => policy
            .WithOrigins(read.Value.AllowedOrigins)
            .WithMethods("GET")
            .AllowAnyHeader()));

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseCors();

app.MapDefaultEndpoints();

// No MigrateAsync, deliberately, and not merely because production applies migrations out-of-band.
// This host does not own RaceRoom's schema — the ingest side does (ADR 0001) — and a read API that
// can create tables is a read API that can create the wrong ones when it starts against an empty
// database. If the schema is not there, failing to read is the correct outcome.

app.MapSessionReadEndpoints();
app.MapTelemetryReadEndpoints();

app.Run();

/// <summary>Entry point partial, exposed so Aspire test hosts can reference this assembly's <c>Program</c>.</summary>
public partial class Program;
