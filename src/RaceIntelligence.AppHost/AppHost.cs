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

using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// AspireAppFixture boots this same AppHost graph for integration tests, and sets this so its
// Postgres resource never shares a port or data volume with a real dev run.
bool isIntegrationTest = builder.Configuration.GetValue<bool>("RaceIntelligence:IsIntegrationTest");

// Shared secret between collector and ingest API. Prompted once and stored in user secrets,
// so it never lands in source control.
var ingestApiKey = builder.AddParameter("ingest-api-key", secret: true);

// Fixed the same way as ingest-api-key, so external tools (DataGrip, psql, ...) don't need
// reconfiguring on every AppHost run.
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword, port: isIntegrationTest ? null : 55432);

// Keeps telemetry across AppHost restarts. Skipped for integration tests, which must get a
// throwaway container instead of the real dev database's volume.
if (!isIntegrationTest)
{
    postgres.WithDataVolume();
}

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
    .WithEnvironment("Collector__ApiKey", ingestApiKey);

builder.Build().Run();
