using System.ComponentModel.DataAnnotations;
using Shouldly;

namespace RaceIntelligence.Collector.Tests;

/// <summary>
/// Covers the startup validation applied to <see cref="CollectorOptions"/> via
/// <c>ValidateDataAnnotations().ValidateOnStart()</c>. The interesting case is
/// <see cref="CollectorOptions.IngestBaseUrl"/>, which must accept both a concrete address (the
/// standalone deployment, where the collector is configured with the real host and port) and a
/// service-discovery name (the Aspire dev loop, where the AppHost injects a logical service name
/// and no port is baked in anywhere).
/// </summary>
public class CollectorOptionsTests
{
    private static IReadOnlyList<ValidationResult> Validate(CollectorOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    private static CollectorOptions WithIngestBaseUrl(string url) => new() { IngestBaseUrl = url, ApiKey = "key" };

    [Theory]
    [InlineData("https://home-server:5443/")]
    [InlineData("http://192.168.1.10:5047/")]
    [InlineData("https+http://ingest-api/")]
    [InlineData("http://ingest-api/")]
    public void An_absolute_base_address_with_a_trailing_slash_is_accepted(string url)
    {
        Validate(WithIngestBaseUrl(url)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("https://home-server:5443")] // no trailing slash: relative paths would replace the last segment.
    [InlineData("ingest-api")] // not absolute.
    [InlineData("/api/v1/")] // not absolute.
    [InlineData("")]
    public void An_address_that_would_not_combine_with_relative_request_paths_is_rejected(string url)
    {
        Validate(WithIngestBaseUrl(url))
            .ShouldContain(result => result.MemberNames.Contains(nameof(CollectorOptions.IngestBaseUrl)));
    }

    [Fact]
    public void A_service_discovery_name_is_usable_as_an_HttpClient_BaseAddress()
    {
        // The point of the whole exercise: what validation accepts must also be assignable to
        // HttpClient.BaseAddress, since that is where the service-discovery handler picks it up.
        using var httpClient = new HttpClient { BaseAddress = new Uri("https+http://ingest-api/") };

        new Uri(httpClient.BaseAddress!, "api/v1/sessions").ToString().ShouldBe("https+http://ingest-api/api/v1/sessions");
    }

    [Fact]
    public void A_missing_ApiKey_is_rejected()
    {
        var options = new CollectorOptions { IngestBaseUrl = "https://localhost:5443/", ApiKey = string.Empty };

        Validate(options).ShouldContain(result => result.MemberNames.Contains(nameof(CollectorOptions.ApiKey)));
    }
}
