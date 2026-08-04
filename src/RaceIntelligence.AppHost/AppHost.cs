// Local development inner loop only.
//
// This orchestrates PostgreSQL, the ingest API and the collector on a single machine so the whole
// Phase 1 pipeline can be run and debugged with one command. It is NOT the production topology:
// in production the collector runs on the gaming PC (reading simulator shared memory) while the
// ingest API and PostgreSQL run on the home server, and neither is launched by Aspire.
//
// There is no lock-in. ServiceDefaults is a thin wrapper over standard Microsoft.Extensions and
// OpenTelemetry APIs, so both services behave identically when launched by docker-compose,
// as a Windows service, or via plain `dotnet run`.

var builder = DistributedApplication.CreateBuilder(args);

// Shared secret between collector and ingest API. Prompted once and stored in user secrets,
// so it never lands in source control.
var ingestApiKey = builder.AddParameter("ingest-api-key", secret: true);

// WithDataVolume keeps collected telemetry across AppHost restarts — losing a test session's
// data on every restart would make the "raw data is permanent" behaviour impossible to exercise.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var database = postgres.AddDatabase("raceintel");

var ingestApi = builder.AddProject<Projects.RaceIntelligence_Ingest_Api>("ingest-api")
    .WithReference(database)
    .WaitFor(database)
    .WithEnvironment("Ingest__ApiKey", ingestApiKey);

// The collector holds no database credentials by design — it reaches the database only through
// the ingest API. WithReference injects the API's URL via service discovery.
builder.AddProject<Projects.RaceIntelligence_Collector>("collector")
    .WithReference(ingestApi)
    .WaitFor(ingestApi)
    .WithEnvironment("Ingest__ApiKey", ingestApiKey);

builder.Build().Run();
