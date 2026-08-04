namespace RaceIntelligence.Ingest.Api.Auth;

/// <summary>
/// Checks a single static shared secret (<c>Ingest:ApiKey</c>) against the <c>X-Api-Key</c> request
/// header. Applied only to the <c>/api/v1</c> endpoint group, so <c>/health</c> and <c>/alive</c>
/// (mapped separately by <c>MapDefaultEndpoints</c>) are never gated by it.
/// </summary>
/// <remarks>
/// <b>This is a deliberate Phase-1 compromise, not real authentication:</b> one shared key, no
/// per-client identity, no rotation, no rate limiting, and a plain (non-constant-time) string
/// comparison. It exists only to stop an accidental scan or a stray device on the home LAN from
/// writing telemetry through a single trusted collector. Do not expose this service beyond that
/// LAN, and do not add a second real client, without replacing this with proper authentication.
/// </remarks>
public sealed class ApiKeyFilter(IConfiguration configuration) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = configuration["Ingest:ApiKey"];
        var provided = context.HttpContext.Request.Headers["X-Api-Key"].ToString();

        if (string.IsNullOrEmpty(expected) || !string.Equals(expected, provided, StringComparison.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing or invalid API key.");
        }

        return await next(context).ConfigureAwait(false);
    }
}
