using System.Net;
using System.Net.Http.Json;
using MessagePack;
using RaceIntelligence.Ingest.Api.Tests.Support;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Telemetry;
using Shouldly;

namespace RaceIntelligence.Ingest.Api.Tests.Integration;

/// <summary>
/// End-to-end tests against the real AppHost graph for the MessagePack telemetry batch hot path.
/// Skips gracefully when Docker is unavailable — see <see cref="AspireAppFixture"/>.
/// </summary>
[Collection(AspireAppCollection.Name)]
public sealed class TelemetryBatchEndpointTests(AspireAppFixture fixture)
{
    [Fact]
    public async Task First_batch_is_accepted_then_the_same_batch_replayed_reports_duplicates()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var session = DtoFactory.SessionCreateRequest();
        (await PostJsonAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var samples = DtoFactory.TelemetryBatch(session.SessionId, count: 20);
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, session.SessionId, samples[0].SequenceNumber, samples[^1].SequenceNumber, samples);

        var first = await PostBatchAsync(session.SessionId, batch);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstResult = await first.Content.ReadFromJsonAsync<TelemetryBatchResponse>(TestContext.Current.CancellationToken);
        firstResult.ShouldNotBeNull();
        firstResult.Accepted.ShouldBe(20);
        firstResult.Duplicates.ShouldBe(0);

        // Simulate a retried upload: the exact same batch, byte for byte, submitted again.
        var second = await PostBatchAsync(session.SessionId, batch);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondResult = await second.Content.ReadFromJsonAsync<TelemetryBatchResponse>(TestContext.Current.CancellationToken);
        secondResult.ShouldNotBeNull();
        secondResult.Accepted.ShouldBe(0);
        secondResult.Duplicates.ShouldBe(20, "a retried batch must be a no-op at row granularity");
    }

    [Fact]
    public async Task Batch_for_an_unknown_session_returns_404()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var sessionId = Guid.CreateVersion7();
        var samples = DtoFactory.TelemetryBatch(sessionId, count: 5);
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, sessionId, samples[0].SequenceNumber, samples[^1].SequenceNumber, samples);

        var response = await PostBatchAsync(sessionId, batch);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Batch_with_unsupported_schema_version_returns_400()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var session = DtoFactory.SessionCreateRequest();
        (await PostJsonAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var samples = DtoFactory.TelemetryBatch(session.SessionId, count: 3);
        var batch = new TelemetryBatchRequest(SchemaVersion.Current + 1, session.SessionId, samples[0].SequenceNumber, samples[^1].SequenceNumber, samples);

        var response = await PostBatchAsync(session.SessionId, batch);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Batch_without_an_api_key_is_rejected_with_401()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var sessionId = Guid.CreateVersion7();
        var samples = DtoFactory.TelemetryBatch(sessionId, count: 1);
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, sessionId, samples[0].SequenceNumber, samples[^1].SequenceNumber, samples);
        var bytes = MessagePackSerializer.Serialize(batch, TelemetryMessagePackOptions.Default);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/telemetry:batch")
        {
            Content = new ByteArrayContent(bytes),
        };

        using var response = await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Batch_whose_samples_member_arrived_as_nil_returns_400()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var session = DtoFactory.SessionCreateRequest();
        (await PostJsonAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // `required`/non-nullable is a C# compile-time promise; MessagePack decodes nil into it
        // regardless, which is why null! here produces a payload a real client could hand-write.
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, session.SessionId, 0, 0, null!);

        using var response = await PostBatchAsync(session.SessionId, batch);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a missing member is a malformed request, not a server fault");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("Samples");
    }

    [Fact]
    public async Task Batch_with_a_nil_tyre_temperature_member_returns_400()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var session = DtoFactory.SessionCreateRequest();
        (await PostJsonAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var samples = new[] { DtoFactory.TelemetrySample(session.SessionId, 0) with { TyreTemperatureRearRight = null! } };
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, session.SessionId, 0, 0, samples);

        using var response = await PostBatchAsync(session.SessionId, batch);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain(nameof(TelemetrySampleDto.TyreTemperatureRearRight));
    }

    [Fact]
    public async Task Batch_with_nil_extras_returns_400()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var session = DtoFactory.SessionCreateRequest();
        (await PostJsonAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var samples = new[] { DtoFactory.TelemetrySample(session.SessionId, 0) with { Extras = null! } };
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, session.SessionId, 0, 0, samples);

        using var response = await PostBatchAsync(session.SessionId, batch);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain(nameof(TelemetrySampleDto.Extras));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("{\"a\":}")]
    public async Task Batch_with_malformed_extras_is_a_400_naming_the_sample(string malformed)
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        // Extras is client text that travels uninspected to a jsonb column, so this endpoint is the
        // only thing between it and a database error that names no sample.
        var session = DtoFactory.SessionCreateRequest();
        (await PostJsonAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var samples = new[]
        {
            DtoFactory.TelemetrySample(session.SessionId, 0),
            DtoFactory.TelemetrySample(session.SessionId, 1) with { Extras = malformed },
        };
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, session.SessionId, 0, 1, samples);

        using var response = await PostBatchAsync(session.SessionId, batch);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain(nameof(TelemetrySampleDto.Extras));
        body.ShouldContain("index 1", Case.Insensitive);
    }

    [Fact]
    public async Task Batch_with_valid_extras_is_stored_verbatim()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var session = DtoFactory.SessionCreateRequest();
        (await PostJsonAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        const string extras = """{"pushToPass":{"usesRemaining":5},"tags":["traffic"]}""";
        var samples = new[] { DtoFactory.TelemetrySample(session.SessionId, 0) with { Extras = extras } };
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, session.SessionId, 0, 0, samples);

        using var response = await PostBatchAsync(session.SessionId, batch);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Batch_body_past_the_size_cap_is_rejected_without_being_decoded()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var sessionId = Guid.CreateVersion7();

        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/telemetry:batch")
        {
            Content = new ByteArrayContent(new byte[(9 * 1024 * 1024) + 1]),
        };
        message.Headers.Add("X-Api-Key", AspireAppFixture.ApiKey);

        using var response = await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    private async Task<HttpResponseMessage> PostBatchAsync(Guid sessionId, TelemetryBatchRequest batch)
    {
        var bytes = MessagePackSerializer.Serialize(batch, TelemetryMessagePackOptions.Default);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/telemetry:batch")
        {
            Content = new ByteArrayContent(bytes),
        };
        message.Headers.Add("X-Api-Key", AspireAppFixture.ApiKey);

        return await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostJsonAsync<T>(string path, T body)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        message.Headers.Add("X-Api-Key", AspireAppFixture.ApiKey);
        return await fixture.ApiClient.SendAsync(message, TestContext.Current.CancellationToken);
    }
}
