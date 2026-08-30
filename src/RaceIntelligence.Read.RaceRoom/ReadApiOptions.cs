using System.ComponentModel.DataAnnotations;

namespace RaceIntelligence.Read.RaceRoom;

/// <summary>Host configuration for the read API, bound from the <c>Read</c> section.</summary>
public sealed class ReadApiOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Read";

    /// <summary>
    /// The exact browser origins allowed to read this API.
    /// </summary>
    /// <remarks>
    /// <b>Required and non-empty, because an empty list would mean "allow everything".</b> This is
    /// the same trap <c>RaceIntelligence.Web.Live.LiveHubOptions</c> guards against for the
    /// hub, and for the same reason: this service is the read half of a deployment that is
    /// deliberately exposed through a tunnel (ADR 0003), so a permissive default is a permissive
    /// production.
    /// <para>
    /// The allowlist is what guards an unkeyed API. It is not much — a non-browser client ignores
    /// CORS entirely — but the data here is read-only telemetry and the threat being managed is a
    /// page on another origin reading it, not a determined caller.
    /// </para>
    /// </remarks>
    [Required(ErrorMessage = "Read:AllowedOrigins must list the dashboard's origin; an empty list would accept every origin.")]
    [MinLength(1, ErrorMessage = "Read:AllowedOrigins must list the dashboard's origin; an empty list would accept every origin.")]
    public string[] AllowedOrigins { get; init; } = [];
}
