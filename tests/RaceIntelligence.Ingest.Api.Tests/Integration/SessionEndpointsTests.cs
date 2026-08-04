using System.Net;
using System.Net.Http.Json;
using RaceIntelligence.Ingest.Api.Tests.Support;
using RaceIntelligence.Ingest.Contracts;
using Shouldly;

namespace RaceIntelligence.Ingest.Api.Tests.Integration;

/// <summary>
/// End-to-end tests against the real AppHost graph (Postgres + ingest API) for the JSON session/lap
/// endpoints. Skips gracefully when Docker is unavailable — see <see cref="AspireAppFixture"/>.
/// </summary>
[Collection(AspireAppCollection.Name)]
public sealed class SessionEndpointsTests(AspireAppFixture fixture)
{
    [Fact]
    public async Task Creating_the_same_session_twice_is_idempotent()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var request = DtoFactory.SessionCreateRequest();

        var first = await PostAsync("/api/v1/sessions", request);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await PostAsync("/api/v1/sessions", request);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, "a repeated create with the same SessionId must succeed, not conflict or duplicate");
    }

    [Fact]
    public async Task Unsupported_schema_version_is_rejected_with_400()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var request = DtoFactory.SessionCreateRequest(schemaVersion: SchemaVersion.Current + 1);

        var response = await PostAsync("/api/v1/sessions", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Missing_api_key_is_rejected_with_401()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sessions")
        {
            Content = JsonContent.Create(DtoFactory.SessionCreateRequest()),
        };

        using var response = await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_api_key_is_rejected_with_401()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sessions")
        {
            Content = JsonContent.Create(DtoFactory.SessionCreateRequest()),
        };
        message.Headers.Add("X-Api-Key", "not-the-right-key");

        using var response = await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_endpoint_is_reachable_without_an_api_key()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        using var response = await fixture.ApiClient.GetAsync("/alive", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Patching_an_unknown_session_returns_404()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var update = new SessionUpdateRequest(SchemaVersion.Current, DateTimeOffset.UtcNow, null, null, null);

        using var message = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/sessions/{Guid.CreateVersion7()}")
        {
            Content = JsonContent.Create(update),
        };
        message.Headers.Add("X-Api-Key", AspireAppFixture.ApiKey);

        using var response = await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recording_a_lap_for_an_unknown_session_returns_404()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var lap = new LapCompletedRequest(SchemaVersion.Current, 1, TimeSpan.FromMinutes(1.5), 2f, 40f, 60f, true);

        var response = await PostAsync($"/api/v1/sessions/{Guid.CreateVersion7()}/laps", lap);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recording_the_same_lap_number_twice_upserts_rather_than_duplicates()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var session = DtoFactory.SessionCreateRequest();
        (await PostAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var lap = new LapCompletedRequest(SchemaVersion.Current, 1, TimeSpan.FromMinutes(1.5), 2f, 40f, 60f, true);
        var firstLap = await PostAsync($"/api/v1/sessions/{session.SessionId}/laps", lap);
        firstLap.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updatedLap = lap with { LapTime = TimeSpan.FromMinutes(1.4) };
        var secondLap = await PostAsync($"/api/v1/sessions/{session.SessionId}/laps", updatedLap);
        secondLap.StatusCode.ShouldBe(HttpStatusCode.OK, "re-submitting the same lap number must upsert, not error");
    }

    private async Task<HttpResponseMessage> PostAsync<T>(string path, T body)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        message.Headers.Add("X-Api-Key", AspireAppFixture.ApiKey);
        return await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);
    }
}
