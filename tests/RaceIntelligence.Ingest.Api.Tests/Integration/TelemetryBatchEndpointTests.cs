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
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, session.SessionId, samples[0].SequenceNumber, samples[^1].SequenceNumber, samples, []);

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
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, sessionId, samples[0].SequenceNumber, samples[^1].SequenceNumber, samples, []);

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
        var batch = new TelemetryBatchRequest(SchemaVersion.Current + 1, session.SessionId, samples[0].SequenceNumber, samples[^1].SequenceNumber, samples, []);

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
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, sessionId, samples[0].SequenceNumber, samples[^1].SequenceNumber, samples, []);
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
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, session.SessionId, 0, 0, null!, []);

        using var response = await PostBatchAsync(session.SessionId, batch);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a missing member is a malformed request, not a server fault");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("Samples");
    }

    /// <summary>
    /// The one nil a sample can still arrive as. Every channel on it is a value type or a nullable
    /// one, so a <c>nil</c> in the payload lands as the null that already means "not reported" —
    /// there is no required reference member left to omit, and no JSON string to validate, which is
    /// what the four tests that used to sit here were guarding.
    /// </summary>
    [Fact]
    public async Task Batch_with_a_nil_sample_returns_400_naming_the_index()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var session = DtoFactory.SessionCreateRequest();
        (await PostJsonAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var samples = new[] { DtoFactory.TelemetrySample(session.SessionId, 0), null! };
        var batch = new TelemetryBatchRequest(SchemaVersion.Current, session.SessionId, 0, 1, samples, []);

        using var response = await PostBatchAsync(session.SessionId, batch);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("index 1", Case.Insensitive);
    }

    /// <summary>
    /// The operating windows ride on the batch rather than having an endpoint of their own, and the
    /// server keeps the first row per <c>(session, corner, compound)</c> — so the same four rows
    /// arriving on every batch is the normal case, not a conflict.
    /// </summary>
    [Fact]
    public async Task Repeated_operating_windows_are_accepted_rather_than_conflicting()
    {
        if (!fixture.IsAvailable)
        {
            Assert.Skip(fixture.SkipReason ?? "Aspire app unavailable.");
        }

        var session = DtoFactory.SessionCreateRequest();
        (await PostJsonAsync("/api/v1/sessions", session)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var windows = DtoFactory.OperatingWindows();

        for (var batchIndex = 0; batchIndex < 2; batchIndex++)
        {
            var samples = DtoFactory.TelemetryBatch(session.SessionId, count: 2, startSequence: batchIndex * 2);
            var batch = new TelemetryBatchRequest(
                SchemaVersion.Current, session.SessionId, samples[0].SequenceNumber, samples[^1].SequenceNumber, samples, windows);

            using var response = await PostBatchAsync(session.SessionId, batch);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
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
