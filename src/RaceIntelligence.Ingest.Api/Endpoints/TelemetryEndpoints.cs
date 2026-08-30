using MessagePack;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Ingest.Api.Auth;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Mapping;
using RaceIntelligence.Ingest.Contracts.Telemetry;
using RaceIntelligence.Persistence.Core;
using RaceIntelligence.Persistence.Core.Bulk;

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
    /// <summary>
    /// Largest telemetry batch body accepted, in bytes. A 60 Hz session uploading every few seconds
    /// produces batches three orders of magnitude smaller than this; the cap exists so a single
    /// unauthenticated-at-the-network-edge POST cannot make the server buffer and decode an
    /// arbitrarily large payload.
    /// </summary>
    private const long MaxBatchBodyBytes = 8 * 1024 * 1024;

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
        TelemetryDbContext db,
        ITelemetryWriter writer,
        CancellationToken ct)
    {
        if (context.Request.ContentLength > MaxBatchBodyBytes)
        {
            return PayloadTooLarge();
        }

        // A body sent without a Content-Length (chunked) gets no free pass: lowering the server's
        // own limit makes the read itself stop at the cap rather than trusting the declared length.
        var sizeLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = MaxBatchBodyBytes;
        }

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
        catch (BadHttpRequestException)
        {
            return PayloadTooLarge();
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

        // C#'s non-nullable annotations are compile-time only: MessagePack happily decodes a `nil`
        // into any of them, so a hand-written payload omitting Samples reaches this line as a null
        // and used to surface as a NullReferenceException and a 500.
        //
        // Only the two lists need this now. Every channel on a sample is a value type or a nullable
        // one, so a `nil` in the payload lands as the null that already means "not reported" — there
        // is no required reference member left to omit, and no JSON string to validate, because the
        // sample carries no JSON at all.
        if (batch.Samples is null)
        {
            return MissingMember("Samples", sampleIndex: null);
        }

        for (var i = 0; i < batch.Samples.Count; i++)
        {
            if (batch.Samples[i] is null)
            {
                return MissingMember("the sample itself", i);
            }
        }

        var samples = batch.Samples;

        TelemetryWriteResult result;
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

    private static IResult MissingMember(string member, int? sampleIndex) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Incomplete telemetry batch",
        detail: sampleIndex is { } index
            ? $"Sample at index {index} is missing required member '{member}'."
            : $"The batch is missing required member '{member}'.");

    private static IResult PayloadTooLarge() => Results.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "Telemetry batch too large",
        detail: $"A telemetry batch body may be at most {MaxBatchBodyBytes} bytes.");
}
