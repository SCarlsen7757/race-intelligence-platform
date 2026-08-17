using System.ComponentModel.DataAnnotations;

namespace RaceIntelligence.Collector.Abstractions;

/// <summary>
/// Validates a base address that may be either a concrete URL or a service-discovery name: an
/// absolute URI with an authority, ending in a trailing slash. Deliberately does not constrain the
/// scheme — <c>https+http</c> (service discovery's "prefer https, accept http" form) is as valid
/// here as plain <c>https</c>, and <see cref="UrlAttribute"/> rejects it.
/// </summary>
/// <remarks>
/// Shared rather than per-plugin: every plugin that talks to something over the network configures
/// its endpoint the same way, and each re-implementing this would be four chances to disagree about
/// what a valid base address looks like.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ServiceUrlAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is not string text)
        {
            // Absent/non-string values are [Required]'s business, not this attribute's.
            return true;
        }

        if (text.Length == 0)
        {
            // An unset URL in a plugin that is switched off is not an error. Only the plugin's own
            // validator, which knows whether it is enabled, can decide that — see the note on
            // ITelemetryPlugin about plugins owning their configuration.
            return true;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && !string.IsNullOrEmpty(uri.Authority)
            && text.EndsWith('/');
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) =>
        $"'{name}' must be an absolute base address with a trailing slash, e.g. 'https://home-server:5443/' or 'https+http://ingest-api/'.";
}
