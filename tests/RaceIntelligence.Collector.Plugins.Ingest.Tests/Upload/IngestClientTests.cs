using System.Net;
using System.Net.Http.Json;
using RaceIntelligence.Collector.Plugins.Ingest.Tests.Support;
using RaceIntelligence.Collector.TestSupport;
using RaceIntelligence.Collector.Plugins.Ingest.Upload;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Mapping;
using RaceIntelligence.Ingest.Contracts.Telemetry;
using Shouldly;

namespace RaceIntelligence.Collector.Plugins.Ingest.Tests.Upload;

public class IngestClientTests
{
    private const string ApiKey = "test-api-key";

    private static IngestClient CreateClient(FakeHttpMessageHandler handler, out HttpClient httpClient)
    {
        httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://ingest.example/") };
        httpClient.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return new IngestClient(httpClient);
    }

    [Fact]
    public async Task CreateSessionAsync_sends_ApiKey_header_and_posts_to_sessions_route()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id = Guid.NewGuid() }),
        });
        var client = CreateClient(handler, out var httpClient);
        var request = new SessionCreateRequest(
            SchemaVersion.Current, Guid.NewGuid(),
            new GameVersionDto("raceroom", "RaceRoom Racing Experience", "1.0", 3, 5, "0.1.0"),
            0UL, "Track", "Layout", null, 1, DateTimeOffset.UtcNow, null, null, null, null, null);

        await client.CreateSessionAsync(request, TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem();
        var sent = handler.Requests[0];
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.RequestUri!.AbsolutePath.ShouldBe("/api/v1/sessions");
        sent.Headers.GetValues("X-Api-Key").ShouldHaveSingleItem().ShouldBe(ApiKey);

        httpClient.Dispose();
    }

    [Fact]
    public async Task UpdateSessionAsync_sends_ApiKey_header_and_patches_the_session_route()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler, out var httpClient);
        var sessionId = Guid.NewGuid();
        var request = new SessionUpdateRequest(SchemaVersion.Current, DateTimeOffset.UtcNow, null);

        await client.UpdateSessionAsync(sessionId, request, TestContext.Current.CancellationToken);

        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Patch);
        sent.RequestUri!.AbsolutePath.ShouldBe($"/api/v1/sessions/{sessionId}");
        sent.Headers.GetValues("X-Api-Key").ShouldHaveSingleItem().ShouldBe(ApiKey);

        httpClient.Dispose();
    }

    [Fact]
    public async Task RecordLapAsync_posts_to_the_laps_route_under_the_session()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler, out var httpClient);
        var sessionId = Guid.NewGuid();
        var request = new LapCompletedRequest(SchemaVersion.Current, 1, TimeSpan.FromMinutes(2), 3.5f, 40f, 60f, true);

        await client.RecordLapAsync(sessionId, request, TestContext.Current.CancellationToken);

        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.RequestUri!.AbsolutePath.ShouldBe($"/api/v1/sessions/{sessionId}/laps");
        sent.Headers.GetValues("X-Api-Key").ShouldHaveSingleItem().ShouldBe(ApiKey);

        httpClient.Dispose();
    }

    [Fact]
    public async Task UploadTelemetryBatchAsync_posts_MessagePack_to_the_batch_route_and_parses_the_JSON_response()
    {
        var expectedResponse = new TelemetryBatchResponse(Accepted: 3, Duplicates: 0, ServerReceivedAtUnixMs: 123);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expectedResponse),
        });
        var client = CreateClient(handler, out var httpClient);
        var sessionId = Guid.NewGuid();
        var sample = TelemetrySampleFactory.Create(sessionId, 0);
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, sessionId, 0, 0, [sample], []);

        var response = await client.UploadTelemetryBatchAsync(sessionId, batch, TestContext.Current.CancellationToken);

        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.RequestUri!.AbsolutePath.ShouldBe($"/api/v1/sessions/{sessionId}/telemetry:batch");
        sent.Headers.GetValues("X-Api-Key").ShouldHaveSingleItem().ShouldBe(ApiKey);
        sent.Content!.Headers.ContentType!.MediaType.ShouldBe("application/x-msgpack");
        response.ShouldBe(expectedResponse);

        httpClient.Dispose();
    }

    [Fact]
    public async Task A_non_success_response_surfaces_as_a_thrown_exception_instead_of_being_swallowed()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom"),
        });
        var client = CreateClient(handler, out var httpClient);
        var request = new LapCompletedRequest(SchemaVersion.Current, 1, null, null, null, null, true);

        var exception = await Should.ThrowAsync<HttpRequestException>(
            () => client.RecordLapAsync(Guid.NewGuid(), request, TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        exception.Message.ShouldContain("boom");

        httpClient.Dispose();
    }
}
