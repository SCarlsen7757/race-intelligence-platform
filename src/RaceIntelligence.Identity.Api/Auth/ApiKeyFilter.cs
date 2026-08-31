namespace RaceIntelligence.Identity.Api.Auth;

/// <summary>
/// Checks a single static administrative key (<c>Identity:ApiKey</c>) against the <c>X-Api-Key</c>
/// request header. Applied only to the <c>/api/v1</c> endpoint group, so <c>/health</c> and
/// <c>/alive</c> are never gated by it.
/// </summary>
/// <remarks>
/// <para>
/// <b>One key here is deliberate, not a leftover.</b> The ingest API holds one key per collector
/// because it has several clients that must be revocable independently; this service has exactly
/// one kind of caller — a human curating the registry — so a second key would name nothing. The
/// comparison is constant-time all the same: see <see cref="IdentityApiKeyGate"/>.
/// </para>
/// <para>
/// Its own key rather than the ingest one, for the reason the ingest and live keys are already
/// separate: these guard different services with different exposure, and one key for both means a
/// leak of either compromises both. This one guards the only hand-curated, unrebuildable state in
/// the platform, which argues for keeping it apart rather than for sharing it.
/// </para>
/// </remarks>
public sealed class ApiKeyFilter(IdentityApiKeyGate gate) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var provided = context.HttpContext.Request.Headers[IdentityApiKeyGate.HeaderName].ToString();

        if (!gate.IsValid(provided))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing or invalid API key.");
        }

        return await next(context).ConfigureAwait(false);
    }
}
