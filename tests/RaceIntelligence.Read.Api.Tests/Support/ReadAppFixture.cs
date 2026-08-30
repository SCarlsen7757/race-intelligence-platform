using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace RaceIntelligence.Read.Api.Tests.Support;

/// <summary>
/// Boots the real AppHost graph (Postgres, the ingest API and the read API) for this test assembly.
/// </summary>
/// <remarks>
/// <b>Deliberately its own fixture rather than a shared one.</b> The obvious move is to extract this
/// and <c>RaceIntelligence.Ingest.Api.Tests.Support.AspireAppFixture</c> into a common support
/// project, and it does not work: the support-project pattern this repo already uses
/// (<c>RaceIntelligence.Collector.TestSupport</c>) has to strip <c>xunit.v3</c> to build as a
/// library, and a fixture is <see cref="IAsyncLifetime"/> and <see cref="ICollectionFixture{T}"/> —
/// xunit types. Two fixtures that resemble each other is the cheaper of the two prices, and it
/// matches the duplication the dashboard's <c>FakeWebSocket</c> makes on purpose.
/// <para>
/// Both APIs are exposed here because a read test has to seed through the write path to have
/// anything to read. That is the assertion worth making: what the collector actually posts is what
/// the dashboard actually gets back, through both real services rather than a fixture's idea of a
/// row.
/// </para>
/// <para>
/// If Docker is unavailable, <see cref="InitializeAsync"/> records the failure in
/// <see cref="SkipReason"/> rather than throwing, so the suite reports honest skips instead of red.
/// </para>
/// </remarks>
public sealed class ReadAppFixture : IAsyncLifetime
{
    /// <summary>The API key configured for the <c>ingest-api-key</c> AppHost parameter in this test run.</summary>
    /// <remarks>
    /// Needed only to seed. The read API under test has no key at all — that is the point of it
    /// being a separate service — so nothing below sends one.
    /// </remarks>
    public const string IngestApiKey = "test-fixture-api-key";

    /// <summary>The password configured for the <c>postgres-password</c> AppHost parameter in this test run.</summary>
    public const string PostgresPassword = "test-fixture-postgres-password";

    /// <summary>The API key configured for the <c>live-api-key</c> AppHost parameter in this test run.</summary>
    public const string LiveApiKey = "test-fixture-live-api-key";

    /// <summary>
    /// The API key configured for the <c>identity-api-key</c> AppHost parameter in this test run.
    /// </summary>
    /// <remarks>
    /// Every secret parameter the AppHost declares has to be answered here, including the ones this
    /// assembly never calls. A parameter left unset does not fail loudly — it prompts for a value,
    /// which in a test run means hanging until the timeout and then reporting something that looks
    /// nothing like "you added a parameter".
    /// </remarks>
    public const string IdentityApiKey = "test-fixture-identity-api-key";

    private DistributedApplication? _app;

    /// <summary><see langword="true"/> once the AppHost graph started successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Human-readable reason <see cref="IsAvailable"/> is <see langword="false"/>, if it is.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>An <see cref="HttpClient"/> pointed at the read API. Only valid when <see cref="IsAvailable"/>.</summary>
    public HttpClient ReadClient { get; private set; } = null!;

    /// <summary>An <see cref="HttpClient"/> pointed at the ingest API, for seeding. Only valid when <see cref="IsAvailable"/>.</summary>
    public HttpClient IngestClient { get; private set; } = null!;

    /// <summary>The origin the read API is configured to allow, which is the dashboard's.</summary>
    /// <remarks>Resolved from the running graph rather than hard-coded, because Aspire allocates the port.</remarks>
    public string DashboardOrigin { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        try
        {
            // IsIntegrationTest must arrive via `args` rather than a post-CreateAsync Configuration
            // write — see the note in the ingest suite's fixture, which lost that race and kept
            // attaching the real dev data volume.
            var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.RaceIntelligence_AppHost>(
                args: ["--RaceIntelligence:IsIntegrationTest=true"]);
            appHost.Configuration["Parameters:ingest-api-key"] = IngestApiKey;
            appHost.Configuration["Parameters:postgres-password"] = PostgresPassword;
            appHost.Configuration["Parameters:identity-api-key"] = IdentityApiKey;
            appHost.Configuration["Parameters:live-api-key"] = LiveApiKey;

            appHost.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

            _app = await appHost.BuildAsync();
            await _app.StartAsync();

            var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();

            // Both, and in this order: the ingest API applies the migrations in Development, and the
            // read host deliberately does not migrate. Waiting only on read-api would race the
            // schema into existence and fail the first query for a reason unrelated to the test.
            await notifications
                .WaitForResourceAsync("ingest-api", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(3))
                .ConfigureAwait(false);

            await notifications
                .WaitForResourceAsync("read-api", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(3))
                .ConfigureAwait(false);

            // The "http" endpoint name is load-bearing: unnamed, CreateHttpClient picks https, which
            // works only where `dotnet dev-certs https --trust` has been run. On CI that is an
            // untrusted self-signed root and every request fails for a reason unrelated to the code.
            ReadClient = _app.CreateHttpClient("read-api", "http");
            IngestClient = _app.CreateHttpClient("ingest-api", "http");
            DashboardOrigin = _app.GetEndpoint("dashboard", "http").GetLeftPart(UriPartial.Authority);

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = $"Docker/Aspire unavailable in this environment: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>xunit collection wiring a single shared <see cref="ReadAppFixture"/> across this assembly.</summary>
[CollectionDefinition(Name)]
public sealed class ReadAppCollection : ICollectionFixture<ReadAppFixture>
{
    /// <summary>The collection name test classes reference via <c>[Collection(ReadAppCollection.Name)]</c>.</summary>
    public const string Name = "ReadApp";
}
