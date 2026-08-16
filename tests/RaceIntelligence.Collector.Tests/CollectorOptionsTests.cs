using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Shouldly;

namespace RaceIntelligence.Collector.Tests;

/// <summary>
/// Covers the startup validation applied to <see cref="CollectorOptions"/>, which comes from two
/// places and is worth testing as two things.
/// <para>
/// The attributes cover shape — most interestingly <see cref="IngestOptions.BaseUrl"/>, which must
/// accept both a concrete address (the standalone deployment, where the collector is configured
/// with the real host and port) and a service-discovery name (the Aspire dev loop, where the
/// AppHost injects a logical service name and no port is baked in anywhere).
/// </para>
/// <para>
/// <see cref="CollectorOptionsValidator"/> covers the rules that depend on which of the two jobs
/// are switched on, and so cannot be expressed as attributes at all: a collector that only
/// publishes live must not be forced to supply an ingest API key it will never send.
/// </para>
/// </summary>
public class CollectorOptionsTests
{
    private static IReadOnlyList<ValidationResult> Validate(object options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    private static ValidateOptionsResult ValidateOptions(CollectorOptions options) =>
        new CollectorOptionsValidator().Validate(name: null, options);

    private static IngestOptions WithBaseUrl(string url) => new() { BaseUrl = url, ApiKey = "key" };

    [Theory]
    [InlineData("https://home-server:5443/")]
    [InlineData("http://192.168.1.10:5047/")]
    [InlineData("https+http://ingest-api/")]
    [InlineData("http://ingest-api/")]
    public void An_absolute_base_address_with_a_trailing_slash_is_accepted(string url)
    {
        Validate(WithBaseUrl(url)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("https://home-server:5443")] // no trailing slash: relative paths would replace the last segment.
    [InlineData("ingest-api")] // not absolute.
    [InlineData("/api/v1/")] // not absolute.
    public void An_address_that_would_not_combine_with_relative_request_paths_is_rejected(string url)
    {
        Validate(WithBaseUrl(url))
            .ShouldContain(result => result.MemberNames.Contains(nameof(IngestOptions.BaseUrl)));
    }

    [Fact]
    public void An_empty_base_address_is_left_for_the_conditional_validator_to_judge()
    {
        // The attribute deliberately passes an empty URL: an unconfigured address in a *disabled*
        // block is not a misconfiguration, and only CollectorOptionsValidator knows which blocks
        // are on. Requiring it here would make a publish-only collector fail on its unused ingest
        // settings.
        Validate(WithBaseUrl(string.Empty)).ShouldBeEmpty();
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
    public void The_defaults_are_valid_so_a_collector_with_only_an_api_key_configured_starts()
    {
        // Everything but the secret ships with a working default, so the minimum a user has to
        // supply is the ingest key. If this ever fails, the out-of-the-box experience is broken.
        var options = new CollectorOptions { Ingest = new IngestOptions { ApiKey = "key" } };

        Validate(options).ShouldBeEmpty();
        Validate(options.Ingest).ShouldBeEmpty();
        Validate(options.Live).ShouldBeEmpty();
        ValidateOptions(options).Failed.ShouldBeFalse();
    }

    [Fact]
    public void A_collector_with_neither_job_enabled_is_rejected()
    {
        // It would poll the simulator at 60 Hz and throw every sample away. Almost certainly a
        // typo in configuration rather than an intent, so fail at startup and say so.
        var options = new CollectorOptions
        {
            Ingest = new IngestOptions { Enabled = false, ApiKey = "key" },
            Live = new LiveOptions { Enabled = false, ApiKey = "key" },
        };

        var result = ValidateOptions(options);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(failure => failure.Contains("Enabled", StringComparison.Ordinal));
    }

    [Fact]
    public void An_archive_only_collector_does_not_need_live_credentials()
    {
        // The common case: no engineer watching, so the Live block is left entirely unconfigured.
        // Demanding a hub key here would force every user to invent one.
        var options = new CollectorOptions
        {
            Ingest = new IngestOptions { Enabled = true, BaseUrl = "https://localhost:5443/", ApiKey = "ingest-key" },
            Live = new LiveOptions { Enabled = false, BaseUrl = string.Empty, ApiKey = string.Empty },
        };

        ValidateOptions(options).Failed.ShouldBeFalse();
    }

    [Fact]
    public void A_publish_only_collector_does_not_need_ingest_credentials()
    {
        // The mirror case: a one-off session nobody wants stored. Archiving is off, so its URL and
        // key are irrelevant and must not be validated.
        var options = new CollectorOptions
        {
            Ingest = new IngestOptions { Enabled = false, BaseUrl = string.Empty, ApiKey = string.Empty },
            Live = new LiveOptions { Enabled = true, BaseUrl = "https://localhost:5444/", ApiKey = "live-key" },
        };

        ValidateOptions(options).Failed.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")] // whitespace is not a key; it would be sent as a header and rejected server-side.
    public void A_missing_ApiKey_on_an_enabled_block_is_rejected(string apiKey)
    {
        var options = new CollectorOptions { Ingest = new IngestOptions { Enabled = true, ApiKey = apiKey } };

        var result = ValidateOptions(options);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(failure => failure.Contains("Collector:Ingest:ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_BaseUrl_on_an_enabled_block_is_rejected()
    {
        // The attribute lets an empty URL through so a disabled block stays quiet; once the block
        // is switched on, the omission has to be reported by someone.
        var options = new CollectorOptions
        {
            Ingest = new IngestOptions { Enabled = true, BaseUrl = string.Empty, ApiKey = "key" },
        };

        var result = ValidateOptions(options);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(failure => failure.Contains("Collector:Ingest:BaseUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void A_live_block_missing_its_own_key_is_rejected_even_when_ingest_is_configured()
    {
        // Each block carries its own credentials; a valid ingest key must not stand in for a
        // missing hub key, or publishing would fail at the socket instead of at startup.
        var options = new CollectorOptions
        {
            Ingest = new IngestOptions { Enabled = true, ApiKey = "ingest-key" },
            Live = new LiveOptions { Enabled = true, ApiKey = string.Empty },
        };

        var result = ValidateOptions(options);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(failure => failure.Contains("Collector:Live:ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public void Standings_published_faster_than_the_poll_rate_is_rejected()
    {
        // Standings come from the same poll loop, so asking for them more often than the loop runs
        // cannot produce fresher data — it only looks like it is configured to.
        var options = new CollectorOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(100),
            Ingest = new IngestOptions { ApiKey = "ingest-key" },
            Live = new LiveOptions { Enabled = true, ApiKey = "live-key", StandingsInterval = TimeSpan.FromMilliseconds(50) },
        };

        var result = ValidateOptions(options);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(failure => failure.Contains("StandingsInterval", StringComparison.Ordinal));
    }

    [Fact]
    public void A_standings_interval_slower_than_the_poll_rate_is_the_normal_case_and_is_accepted()
    {
        var options = new CollectorOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(16),
            Ingest = new IngestOptions { ApiKey = "ingest-key" },
            Live = new LiveOptions { Enabled = true, ApiKey = "live-key", StandingsInterval = TimeSpan.FromMilliseconds(100) },
        };

        ValidateOptions(options).Failed.ShouldBeFalse();
    }

    [Fact]
    public void A_disabled_live_block_is_not_held_to_the_poll_rate()
    {
        // Nothing reads StandingsInterval when publishing is off, so a leftover value from a
        // previous experiment must not stop the collector from starting.
        var options = new CollectorOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(100),
            Ingest = new IngestOptions { ApiKey = "ingest-key" },
            Live = new LiveOptions { Enabled = false, StandingsInterval = TimeSpan.FromMilliseconds(20) },
        };

        ValidateOptions(options).Failed.ShouldBeFalse();
    }
}
