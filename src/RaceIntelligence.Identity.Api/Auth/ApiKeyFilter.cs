namespace RaceIntelligence.Identity.Api.Auth;

/// <summary>
/// Checks a single static shared secret (<c>Identity:ApiKey</c>) against the <c>X-Api-Key</c>
/// request header. Applied only to the <c>/api/v1</c> endpoint group, so <c>/health</c> and
/// <c>/alive</c> are never gated by it.
/// </summary>
/// <remarks>
/// <b>The same deliberate compromise the ingest API makes, and it deserves restating here rather
/// than referred to:</b> one shared key, no per-client identity, no rotation, no rate limiting, and
/// a plain non-constant-time comparison. It exists to stop a stray device on the home LAN writing to
/// this service, not to authenticate anybody.
/// <para>
/// Its own key rather than the ingest one, for the reason the ingest and live keys are already
/// separate: these guard different services with different exposure, and one key for both means a
/// leak of either compromises both. This one guards the only hand-curated, unrebuildable state in
/// the platform, which argues for keeping it apart rather than for sharing it.
/// </para>
/// </remarks>
public sealed class ApiKeyFilter(IConfiguration configuration) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = configuration["Identity:ApiKey"];
        var provided = context.HttpContext.Request.Headers["X-Api-Key"].ToString();

        if (string.IsNullOrEmpty(expected) || !string.Equals(expected, provided, StringComparison.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing or invalid API key.");
        }

        return await next(context).ConfigureAwait(false);
    }
}
