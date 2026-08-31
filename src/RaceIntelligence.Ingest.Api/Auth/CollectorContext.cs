namespace RaceIntelligence.Ingest.Api.Auth;

/// <summary>
/// Carries the authenticated collector's label across the request, from
/// <see cref="ApiKeyFilter"/> to whatever downstream code wants to know who is uploading.
/// </summary>
/// <remarks>
/// <see cref="HttpContext.Items"/> rather than a <see cref="System.Security.Claims.ClaimsPrincipal"/>
/// because both places the key is checked are endpoint filters, which run after the authentication
/// middleware would have. Adopting the authentication stack for a single scheme would buy nothing
/// and would not reach the telemetry batch endpoint, which is mapped outside the guarded group.
/// </remarks>
public static class CollectorContext
{
    private const string ItemKey = "RaceIntelligence.Ingest.CollectorLabel";

    /// <summary>Records the label of the collector key this request presented.</summary>
    public static void SetCollectorLabel(this HttpContext context, string label) =>
        context.Items[ItemKey] = label;

    /// <summary>
    /// The label of the collector key this request presented, or <see langword="null"/> on a
    /// request that did not pass through <see cref="ApiKeyFilter"/>.
    /// </summary>
    public static string? GetCollectorLabel(this HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;
}
