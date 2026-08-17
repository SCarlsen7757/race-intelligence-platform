using System.ComponentModel.DataAnnotations;
using RaceIntelligence.Collector.Abstractions;
using Shouldly;

namespace RaceIntelligence.Collector.Tests;

/// <summary>
/// Covers the shared base-address validation every plugin endpoint uses.
/// </summary>
/// <remarks>
/// <para>
/// The interesting requirement is that one attribute must accept both a concrete address — the
/// standalone deployment, configured with the real host and port — and a service-discovery name,
/// where Aspire's AppHost injects a logical service name and no port is baked in anywhere. That is
/// why <see cref="UrlAttribute"/> cannot be used: it rejects the <c>https+http</c> scheme-preference
/// form outright.
/// </para>
/// <para>
/// The rules that depend on a plugin being switched on now live with that plugin, in
/// <c>IngestOptionsValidator</c> and <c>LiveOptionsValidator</c>, and are tested there.
/// </para>
/// </remarks>
public class ServiceUrlAttributeTests
{
    private sealed class Endpoint
    {
        [ServiceUrl]
        public string BaseUrl { get; init; } = string.Empty;
    }

    private static IReadOnlyList<ValidationResult> Validate(string url)
    {
        var options = new Endpoint { BaseUrl = url };
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("https://home-server:5443/")]
    [InlineData("http://192.168.1.10:5047/")]
    [InlineData("https+http://ingest-api/")]
    [InlineData("http://ingest-api/")]
    public void An_absolute_base_address_with_a_trailing_slash_is_accepted(string url)
    {
        Validate(url).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("https://home-server:5443")] // no trailing slash: relative paths would replace the last segment.
    [InlineData("ingest-api")] // not absolute.
    [InlineData("/api/v1/")] // not absolute.
    public void An_address_that_would_not_combine_with_relative_request_paths_is_rejected(string url)
    {
        Validate(url).ShouldContain(result => result.MemberNames.Contains(nameof(Endpoint.BaseUrl)));
    }

    [Fact]
    public void An_empty_base_address_is_left_for_the_plugins_own_validator_to_judge()
    {
        // The attribute deliberately passes an empty URL. An unconfigured address belonging to a
        // plugin that is switched off is not a misconfiguration, and only that plugin's validator —
        // registered solely when it is enabled — knows the difference. Requiring it here would make
        // a publish-only collector fail on ingest settings it will never use.
        Validate(string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void A_service_discovery_name_is_usable_as_an_HttpClient_BaseAddress()
    {
        // The point of the whole exercise: what validation accepts must also be assignable to
        // HttpClient.BaseAddress, since that is where the service-discovery handler picks it up.
        using var httpClient = new HttpClient { BaseAddress = new Uri("https+http://ingest-api/") };

        new Uri(httpClient.BaseAddress!, "api/v1/sessions").ToString().ShouldBe("https+http://ingest-api/api/v1/sessions");
    }
}
