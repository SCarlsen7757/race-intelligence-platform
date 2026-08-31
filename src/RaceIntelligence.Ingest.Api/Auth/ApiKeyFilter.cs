namespace RaceIntelligence.Ingest.Api.Auth;

/// <summary>
/// Checks the <c>X-Api-Key</c> request header against the configured per-collector keys
/// (<c>Ingest:ApiKeys</c>), and records which collector the request came from. Applied to the
/// <c>/api/v1/sessions</c> group and, separately, to the telemetry batch endpoint that sits outside
/// it — so <c>/health</c> and <c>/alive</c> (mapped by <c>MapDefaultEndpoints</c>) are never gated.
/// </summary>
/// <remarks>
/// <para>
/// One key per collector, each under a label. Deleting a label revokes that collector and leaves
/// the others working; both revocation and rotation take effect on restart, because the digests
/// are computed once at startup. The comparison is constant-time and length-independent — see
/// <see cref="CollectorKeyGate"/> — matching the posture the live hub has always had.
/// </para>
/// <para>
/// <b>What this is not:</b> the key is a bearer credential held in configuration, not an identity
/// vouched for by the driver registry. It says which configured collector is uploading, not which
/// human — the ingest API still attributes a session to whatever driver the payload claims. Tying a
/// key to a registered driver needs a registration flow that does not exist yet.
/// </para>
/// <para>
/// <b>The credential is transmitted on every request</b>, so its confidentiality is exactly the
/// transport's. That makes TLS termination a load-bearing deployment concern rather than an
/// implementation detail: this check is sound over HTTPS and worth nothing over plaintext.
/// </para>
/// <para>
/// Volume is handled a layer up, by the rate limiter on these endpoints, not here. The check itself
/// is a SHA-256 and a fixed-time compare — well under a microsecond, against a request that
/// carries a few hundred telemetry samples and costs milliseconds to decode and store.
/// </para>
/// </remarks>
public sealed class ApiKeyFilter(CollectorKeyGate gate) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var provided = context.HttpContext.Request.Headers[CollectorKeyGate.HeaderName].ToString();

        if (!gate.TryResolve(provided, out var label))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing or invalid API key.");
        }

        context.HttpContext.SetCollectorLabel(label);

        return await next(context).ConfigureAwait(false);
    }
}
