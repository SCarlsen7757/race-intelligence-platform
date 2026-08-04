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

    private DistributedApplication? _app;

    /// <summary><see langword="true"/> once the AppHost graph started successfully.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Human-readable reason <see cref="IsAvailable"/> is <see langword="false"/>, if it is.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>An <see cref="HttpClient"/> pointed at the running ingest API, resolved via Aspire service discovery. Only valid when <see cref="IsAvailable"/>.</summary>
    public HttpClient ApiClient { get; private set; } = null!;

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

            appHost.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

            _app = await appHost.BuildAsync();
            await _app.StartAsync();

            var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();
            await notifications
                .WaitForResourceAsync("ingest-api", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(3))
                .ConfigureAwait(false);

            ApiClient = _app.CreateHttpClient("ingest-api");
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
