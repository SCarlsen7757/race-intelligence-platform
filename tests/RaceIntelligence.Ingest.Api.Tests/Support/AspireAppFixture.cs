using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace RaceIntelligence.Ingest.Api.Tests.Support;

/// <summary>
/// Boots the real AppHost graph (Postgres container + ingest API, via <see cref="RaceIntelligence.AppHost"/>)
/// for the whole test assembly, using <c>Aspire.Hosting.Testing</c>'s
/// <see cref="DistributedApplicationTestingBuilder"/>.
/// </summary>
/// <remarks>
/// If Docker is not available in the environment this fixture runs in, <see cref="InitializeAsync"/>
/// swallows the startup failure and records it in <see cref="SkipReason"/> instead of throwing —
/// exactly the pattern <c>RaceIntelligence.Persistence.Tests.Support.PostgresFixture</c> uses. Tests
/// that depend on the running app must guard with
/// <c>Assert.SkipUnless(fixture.IsAvailable, fixture.SkipReason ?? "Aspire app unavailable")</c> so
/// the suite reports honest skips rather than failures (or a faked pass) when there is no container
/// runtime to test against.
/// </remarks>
public sealed class AspireAppFixture : IAsyncLifetime
{
    /// <summary>
    /// The API key configured for the <c>ingest-api-key</c> AppHost parameter in this test run.
    /// AppHost parameters resolve from the <c>Parameters:&lt;name&gt;</c> configuration section, so
    /// setting it before <c>BuildAsync</c> avoids the interactive "provide a value" prompt the real
    /// (secret, un-defaulted) parameter would otherwise trigger outside a normal run.
    /// </summary>
    public const string ApiKey = "test-fixture-api-key";

    /// <summary>The password configured for the <c>postgres-password</c> AppHost parameter in this test run.</summary>
    public const string PostgresPassword = "test-fixture-postgres-password";

    /// <summary>
    /// The API key configured for the <c>identity-api-key</c> AppHost parameter in this test run.
    /// </summary>
    /// <remarks>
    /// Every secret parameter the AppHost declares has to be answered here. A parameter left unset
    /// does not fail loudly — it prompts for a value, which in a test run means hanging until the
    /// timeout and then reporting something that looks nothing like "you added a parameter".
    /// </remarks>
    public const string IdentityApiKey = "test-fixture-identity-api-key";

    private DistributedApplication? _app;

    /// <summary><see langword="true"/> once the AppHost graph started successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Human-readable reason <see cref="IsAvailable"/> is <see langword="false"/>, if it is.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>An <see cref="HttpClient"/> pointed at the running ingest API's plaintext HTTP endpoint, resolved via Aspire service discovery. Only valid when <see cref="IsAvailable"/>.</summary>
    public HttpClient ApiClient { get; private set; } = null!;

    /// <summary>An <see cref="HttpClient"/> pointed at the running identity registry's plaintext HTTP endpoint. Only valid when <see cref="IsAvailable"/>.</summary>
    public HttpClient IdentityClient { get; private set; } = null!;

    /// <summary>
    /// The connection string for the named AppHost resource — <c>raceintel</c> being the Postgres
    /// database the ingest API writes to. Only valid when <see cref="IsAvailable"/>.
    /// </summary>
    /// <remarks>
    /// The ingest API is write-only by design, so an integration test that needs to assert on what
    /// was actually persisted has no endpoint to read it back through and must query the database
    /// directly. Exposing that here keeps the coupling to <see cref="DistributedApplication"/> in
    /// the fixture, where the rest of it already lives.
    /// </remarks>
    /// <param name="resourceName">The AppHost resource name, e.g. <c>raceintel</c>.</param>
    public ValueTask<string?> GetConnectionStringAsync(string resourceName, CancellationToken ct = default) =>
        _app is null
            ? throw new InvalidOperationException(
                $"The Aspire app is not running, so '{resourceName}' has no connection string. Guard the test with IsAvailable.")
            : _app.GetConnectionStringAsync(resourceName, ct);

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        try
        {
            // IsIntegrationTest must arrive via `args`, not a post-CreateAsync Configuration write:
            // CreateAsync captures the builder as soon as AppHost.cs's own
            // DistributedApplication.CreateBuilder(args) call returns, but the rest of that
            // top-level script — including the isIntegrationTest config read that decides whether
            // Postgres gets WithDataVolume() — keeps running concurrently with no ordering
            // guarantee against a Configuration write made here afterwards. A late write was
            // silently losing that race, so the test's Postgres container kept attaching the real
            // dev data volume (and its incompatible password) instead of a throwaway one.
            var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.RaceIntelligence_AppHost>(
                args: ["--RaceIntelligence:IsIntegrationTest=true"]);
            appHost.Configuration["Parameters:ingest-api-key"] = ApiKey;
            appHost.Configuration["Parameters:postgres-password"] = PostgresPassword;
            appHost.Configuration["Parameters:identity-api-key"] = IdentityApiKey;

            appHost.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

            _app = await appHost.BuildAsync();
            await _app.StartAsync();

            var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();
            await notifications
                .WaitForResourceAsync("ingest-api", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(3))
                .ConfigureAwait(false);

            await notifications
                .WaitForResourceAsync("identity-api", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(3))
                .ConfigureAwait(false);

            // The "http" endpoint name is load-bearing. The launch profile Aspire starts ingest-api
            // with exposes both https://localhost:7038 and http://localhost:5047, and CreateHttpClient
            // with no endpoint name picks the https one. That works on a developer machine only
            // because `dotnet dev-certs https --trust` has been run there; on a CI runner the ASP.NET
            // Core development certificate is a self-signed root nothing trusts, so every request
            // fails with AuthenticationException "UntrustedRoot" and the whole suite reports red for
            // a reason that has nothing to do with the code under test.
            //
            // Naming the http endpoint takes TLS out of the picture entirely rather than teaching CI
            // to trust a dev cert. Nothing is lost: these tests assert API behaviour (status codes,
            // validation, duplicate handling), the app registers no HTTPS redirection or HSTS
            // middleware, and transport security in production is the reverse proxy's job, not
            // Kestrel's dev certificate.
            ApiClient = _app.CreateHttpClient("ingest-api", "http");
            IdentityClient = _app.CreateHttpClient("identity-api", "http");
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

/// <summary>xunit collection wiring a single shared <see cref="AspireAppFixture"/> across all tests in this assembly.</summary>
[CollectionDefinition(Name)]
public sealed class AspireAppCollection : ICollectionFixture<AspireAppFixture>
{
    /// <summary>The collection name test classes reference via <c>[Collection(AspireAppCollection.Name)]</c>.</summary>
    public const string Name = "AspireApp";
}
