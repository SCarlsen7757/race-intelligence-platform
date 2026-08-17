using Microsoft.Extensions.Options;
using RaceIntelligence.Collector.Plugins.Ingest;
using Shouldly;

namespace RaceIntelligence.Collector.Plugins.Ingest.Tests;

/// <summary>
/// The credentials rules for the archive plugin.
/// </summary>
/// <remarks>
/// This validator is registered only when the plugin is enabled, which is why it never checks
/// <see cref="IngestOptions.Enabled"/> itself. A collector that only publishes live never reaches
/// this code, and so can never be failed by ingest settings it will not use.
/// </remarks>
public class IngestOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(IngestOptions options) =>
        new IngestOptionsValidator().Validate(name: null, options);

    [Fact]
    public void A_configured_endpoint_is_accepted()
    {
        Validate(new IngestOptions { BaseUrl = "https://home-server:5443/", ApiKey = "key" })
            .Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_api_key_is_rejected_with_a_message_naming_how_to_supply_it(string apiKey)
    {
        var result = Validate(new IngestOptions { BaseUrl = "https://home-server:5443/", ApiKey = apiKey });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Collector:Ingest:ApiKey");
        result.FailureMessage.ShouldContain("Collector__Ingest__ApiKey", customMessage: "the message should say how to set it without committing it.");
    }

    [Fact]
    public void A_missing_base_url_is_rejected()
    {
        var result = Validate(new IngestOptions { BaseUrl = string.Empty, ApiKey = "key" });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Collector:Ingest:BaseUrl");
    }

    [Fact]
    public void The_defaults_need_only_an_api_key_to_be_valid()
    {
        Validate(new IngestOptions { ApiKey = "key" }).Succeeded.ShouldBeTrue();
    }
}
