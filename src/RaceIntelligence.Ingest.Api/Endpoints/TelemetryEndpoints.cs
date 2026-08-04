using MessagePack;
using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Ingest.Api.Auth;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Mapping;
using RaceIntelligence.Ingest.Contracts.Telemetry;
using RaceIntelligence.Persistence;
using RaceIntelligence.Persistence.Bulk;

namespace RaceIntelligence.Ingest.Api.Endpoints;

/// <summary>Maps the hot-path, MessagePack telemetry batch endpoint.</summary>
/// <remarks>
/// <para>
/// <b>How MessagePack gets into a minimal API endpoint:</b> ASP.NET Core's minimal API model
/// binding only understands JSON out of the box; there is no built-in "MessagePack body parameter"
/// binding source. Rather than write a full <c>IInputFormatter</c> (an MVC-era abstraction minimal
/// APIs don't use) or a custom parameter-binding <c>TryParse</c>/<c>BindAsync</c> convention, this
/// endpoint takes the raw <see cref="HttpContext"/> and deserializes
/// <see cref="HttpRequest.Body"/> directly with <see cref="MessagePackSerializer.DeserializeAsync{T}(System.IO.Stream, MessagePackSerializerOptions?, System.Threading.CancellationToken)"/>.
/// This is the simplest option that actually works with minimal APIs, at the cost of the request
/// no longer showing up with typed OpenAPI body metadata (there is nothing to reflect: the body
/// isn't bound by the framework at all).
/// </para>
/// </remarks>
public static class TelemetryEndpoints
{
    /// <summary>Registers the telemetry batch endpoint on <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/sessions/{id:guid}/telemetry:batch", HandleBatchAsync)
            .AddEndpointFilter<ApiKeyFilter>();

        return app;
    }

    private static async Task<IResult> HandleBatchAsync(
        Guid id,
        HttpContext context,
        RaceIntelligenceDbContext db,
        ITelemetryWriter writer,
        CancellationToken ct)
    {
        TelemetryBatchRequest batch;
        try
        {
            batch = await MessagePackSerializer.DeserializeAsync<TelemetryBatchRequest>(
                context.Request.Body, TelemetryMessagePackOptions.Default, ct).ConfigureAwait(false);
        }
        catch (MessagePackSerializationException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Malformed telemetry batch", detail: ex.Message);
        }

        if (!SchemaVersion.IsSupported(batch.SchemaVersion))
        {
            return ProblemResults.SchemaVersionUnsupported(batch.SchemaVersion);
        }

        if (batch.SessionId != id)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Session id mismatch",
                detail: $"Route session id '{id}' does not match batch session id '{batch.SessionId}'.");
        }

        var sessionExists = await db.Sessions.AnyAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (!sessionExists)
        {
            return ProblemResults.SessionNotFound(id);
        }

        var samples = batch.Samples.Select(TelemetrySampleContractMapper.ToCore).ToList();

        RaceIntelligence.Persistence.Bulk.TelemetryWriteResult result;
        try
        {
            result = await writer.WriteAsync(id, samples, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            // A sample in the batch carried a SessionId other than the route/batch session — the
            // writer's own consistency check; surface it as a client error, not a 500.
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid telemetry batch", detail: ex.Message);
        }

        return Results.Ok(new TelemetryBatchResponse(result.Inserted, result.Duplicates, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
    }
}
