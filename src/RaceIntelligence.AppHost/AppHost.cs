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

// Shared secret between collector and live hub. A separate parameter from the ingest key on
// purpose: the two guard different services with different exposure — the ingest API is LAN-only,
// while the hub is the one meant to be reachable through a tunnel — and one key for both would mean
// a leak of either compromises both.
var liveApiKey = builder.AddParameter("live-api-key", secret: true);

// Shared secret guarding the identity registry. A third parameter rather than a reuse of either
// above, on the same argument that separated the first two: these guard different services with
// different exposure. This one guards the only hand-curated, unrebuildable state in the platform,
// which is a reason to keep its key apart rather than to economise on keys.
var identityApiKey = builder.AddParameter("identity-api-key", secret: true);

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

// The identity registry, in a database of its own.
//
// One PostgreSQL server here because this is a single-machine inner loop, but a separate database
// on it — and that separation is the design rather than a convenience. Storage is becoming one
// database per simulator, and the registry is the one thing that cannot be: it has to outlive any
// of them, so restoring RaceRoom's database must not take iRacing's mapping with it. See ADR 0002.
var identityDatabase = postgres.AddDatabase("identity");

var ingestApi = builder.AddProject<Projects.RaceIntelligence_Ingest_Api>("ingest-api")
    .WithReference(database)
    .WaitFor(database)
    .WithEnvironment("Ingest__ApiKey", ingestApiKey)
    // Which simulator this database holds. One per simulator now (ADR 0001), so the ingest API
    // refuses a session claiming any other — and refuses to start at all without this, because an
    // ingest API that accepts anything is the silent mixing the split exists to prevent.
    .WithEnvironment("Ingest__GameKey", "raceroom");

// The cross-simulator identity registry. The only other service allowed to hold database
// credentials, and only ever its own — it is never given a reference to the telemetry store, which
// is what keeps the separation above true in code rather than only in a diagram.
builder.AddProject<Projects.RaceIntelligence_Identity_Api>("identity-api")
    .WithReference(identityDatabase)
    .WaitFor(identityDatabase)
    .WithEnvironment("Identity__ApiKey", identityApiKey);

// The live dashboard hub. It holds no database credentials either — everything it serves is in
// memory and arrives over the publishing socket — so unlike the ingest API it gets no reference to
// the database.
//
// Declared before the collector because the collector references it. Aspire resolves references
// against resources already added to the builder, so the reverse order would not compile away
// cleanly — it would simply fail to wire the collector to a hub it could not yet name.
var web = builder.AddProject<Projects.RaceIntelligence_Web>("web")
    .WithEnvironment("Live__ApiKey", liveApiKey);

// The dashboard, as its own Node process on its own origin. It is no longer served by the hub: the
// browser opens its WebSocket straight at the hub, which is both the more modular arrangement and
// the lower-latency one — proxying through Node would add a connection and force that event loop to
// re-emit every focus frame sixty times a second.
//
// Two origins means two things have to agree, and both are wired here rather than written down in
// a README that drifts: the dashboard is told where the hub is, and the hub is told which origin to
// accept. Aspire allocates the ports, so neither side has one hard-coded.
//
// HUB_URL is read by Vite at config time and baked into the bundle, so it must be set before the
// process starts — which it is, as an environment variable, exactly like the collector's BaseUrl.
// The http endpoint rather than https for the same reason as the collector below: it is the one
// that exists under either launch profile.
var dashboard = builder.AddViteApp("dashboard", "../RaceIntelligence.Dashboard")
    .WithEnvironment("HUB_URL", web.GetEndpoint("http"))
    .WaitFor(web);

// Stated after the dashboard exists, because it names the dashboard's endpoint. There is no cycle:
// Aspire allocates every endpoint before it resolves any environment variable, so the two
// references below resolve against addresses that are already decided.
//
// __0 is the configuration binder's array syntax — Live:AllowedOrigins is a list, and this is its
// first entry. Without it the hub refuses to boot rather than accepting every origin, which is the
// whole point of the setting.
web.WithEnvironment("Live__AllowedOrigins__0", dashboard.GetEndpoint("http"));

// The collector holds no database credentials by design — it reaches the database only through
// the ingest API.
//
// The two hops are wired differently, and the difference is not cosmetic.
//
// Ingest goes over HttpClient, so it gets the logical name: WithReference publishes the API's
// endpoints as services__ingest-api__<scheme>__<index>, and ServiceDefaults' service-discovery
// handler resolves them. "https+http" means prefer https and fall back to http, so neither a port
// number nor a launch-profile choice is baked in anywhere.
//
// Live publishing goes over a raw ClientWebSocket, which never passes through an HttpClient and so
// is never seen by the service-discovery handler. A logical name there would reach the collector
// unresolved and fail DNS. The hub's endpoint is therefore injected already resolved — Aspire
// substitutes the real address at launch, which keeps the port out of source control just the same.
//
// The http endpoint rather than https, because it is the one that exists under either launch
// profile: the "https" profile binds both, the "http" profile binds only http. Everything here is
// loopback on one machine, so there is nothing on the wire to protect; the hop that crosses a
// network in production is the tunnel, which terminates its own TLS.
//
// Live publishing is switched on here, unlike the shipped default: the whole point of running the
// AppHost graph is to exercise the full pipeline, and a collector that archived but never published
// would leave the dashboard permanently empty in the one environment built to demonstrate it.
//
// The trailing slash on the endpoint is not decoration. An endpoint reference resolves to a bare
// origin with no trailing slash, and the collector combines this base with a relative publish path
// via Uri(Uri, string) — which replaces the last segment of a base that does not end in one. The
// same rule is what [ServiceUrl] enforces, so without the slash the collector refuses to start.
builder.AddProject<Projects.RaceIntelligence_Collector>("collector")
    .WithReference(ingestApi)
    .WaitFor(ingestApi)
    .WaitFor(web)
    .WithEnvironment("Collector__Ingest__ApiKey", ingestApiKey)
    .WithEnvironment("Collector__Ingest__BaseUrl", "https+http://ingest-api/")
    .WithEnvironment("Collector__Live__Enabled", "true")
    .WithEnvironment("Collector__Live__ApiKey", liveApiKey)
    .WithEnvironment(
        "Collector__Live__BaseUrl",
        ReferenceExpression.Create($"{web.GetEndpoint("http")}/"));

builder.Build().Run();
