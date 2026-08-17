using Microsoft.Extensions.Options;
using RaceIntelligence.Collector.Abstractions;
using RaceIntelligence.Collector.Plugins.Live;
using Shouldly;

namespace RaceIntelligence.Collector.Plugins.Live.Tests;

/// <summary>
/// The credentials rules for the live plugin, plus the one rule that needs the collector's own poll
/// rate to judge.
/// </summary>
/// <remarks>
/// Registered only when the plugin is enabled, so it never checks <see cref="LiveOptions.Enabled"/>
/// itself: an archive-only collector is not held to a publishing rate it never uses.
/// </remarks>
public class LiveOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(LiveOptions options, TimeSpan? pollInterval = null)
    {
        var collectorOptions = Options.Create(new CollectorOptions
        {
            PollInterval = pollInterval ?? TimeSpan.FromSeconds(1.0 / 60.0),
        });

        return new LiveOptionsValidator(collectorOptions).Validate(name: null, options);
    }

    private static LiveOptions Configured(
        TimeSpan? standingsInterval = null,
        string baseUrl = "https://home-server:5444/",
        string apiKey = "key") => new()
    {
        BaseUrl = baseUrl,
        ApiKey = apiKey,
        StandingsInterval = standingsInterval ?? TimeSpan.FromMilliseconds(100),
    };

    [Fact]
    public void A_configured_endpoint_is_accepted()
    {
        Validate(Configured()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_api_key_is_rejected(string apiKey)
    {
        var result = Validate(Configured(apiKey: apiKey));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Collector:Live:ApiKey");
        result.FailureMessage.ShouldContain("Collector__Live__ApiKey");
    }

    [Fact]
    public void A_missing_base_url_is_rejected()
    {
        var result = Validate(Configured(baseUrl: string.Empty));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Collector:Live:BaseUrl");
    }

    [Fact]
    public void Standings_published_faster_than_the_poll_rate_is_rejected()
    {
        // Nothing can be published more often than it is read. Accepting it would silently publish
        // the same snapshot twice and look like a working 20 Hz tower.
        var result = Validate(
            Configured(standingsInterval: TimeSpan.FromMilliseconds(50)),
            pollInterval: TimeSpan.FromMilliseconds(100));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Collector:Live:StandingsInterval");
        result.FailureMessage.ShouldContain("Collector:PollInterval");
    }

    [Fact]
    public void A_standings_interval_slower_than_the_poll_rate_is_the_normal_case()
    {
        Validate(
            Configured(standingsInterval: TimeSpan.FromMilliseconds(100)),
            pollInterval: TimeSpan.FromMilliseconds(16))
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Standings_at_exactly_the_poll_rate_is_allowed()
    {
        Validate(
            Configured(standingsInterval: TimeSpan.FromMilliseconds(100)),
            pollInterval: TimeSpan.FromMilliseconds(100))
            .Succeeded.ShouldBeTrue();
    }
}
