using Microsoft.Extensions.Configuration;
using Shouldly;

namespace RaceIntelligence.Collector.Tests;

/// <summary>
/// Covers the bare command-line flags. They exist because .NET's command-line provider needs a value
/// for every switch — even a boolean — and someone starting the collector to publish a session wants
/// <c>--live</c>, not <c>--Collector:Live:Enabled true</c>.
/// </summary>
public sealed class CollectorCommandLineTests
{
    [Theory]
    [InlineData("--live", "Collector:Live:Enabled", "true")]
    [InlineData("--no-live", "Collector:Live:Enabled", "false")]
    [InlineData("--ingest", "Collector:Ingest:Enabled", "true")]
    [InlineData("--no-ingest", "Collector:Ingest:Enabled", "false")]
    public void A_flag_binds_to_the_setting_it_stands_for(string flag, string key, string expected)
    {
        // Through the real configuration provider rather than asserting on the rewritten string:
        // what matters is the setting that comes out, not the shape of the intermediate array.
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(CollectorCommandLine.Expand([flag]))
            .Build();

        configuration[key].ShouldBe(expected);
    }

    [Fact]
    public void Flags_can_be_combined()
    {
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(CollectorCommandLine.Expand(["--live", "--no-ingest"]))
            .Build();

        configuration["Collector:Live:Enabled"].ShouldBe("true");
        configuration["Collector:Ingest:Enabled"].ShouldBe("false");
    }

    /// <summary>
    /// Anything without a shorthand has to keep working, so the long form is passed through
    /// untouched rather than the expansion becoming the only supported way in.
    /// </summary>
    [Fact]
    public void The_long_form_still_works_alongside_a_flag()
    {
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(CollectorCommandLine.Expand(
                ["--live", "--Collector:Live:StandingsInterval", "00:00:00.200"]))
            .Build();

        configuration["Collector:Live:Enabled"].ShouldBe("true");
        configuration["Collector:Live:StandingsInterval"].ShouldBe("00:00:00.200");
    }

    /// <summary>
    /// A typo must stay a typo. Silently reinterpreting an unrecognised argument would let someone
    /// think they had switched publishing on when they had not.
    /// </summary>
    [Fact]
    public void An_unrecognised_argument_is_left_alone()
    {
        CollectorCommandLine.Expand(["--livee"]).ShouldBe(["--livee"]);
    }

    [Fact]
    public void Expanding_nothing_yields_nothing()
    {
        CollectorCommandLine.Expand([]).ShouldBeEmpty();
    }

    [Fact]
    public void Flags_are_matched_regardless_of_case()
    {
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(CollectorCommandLine.Expand(["--LIVE"]))
            .Build();

        configuration["Collector:Live:Enabled"].ShouldBe("true");
    }

    /// <summary>
    /// Keeps the documented flag list and the implemented one from drifting apart — the docs in
    /// <c>docs/development.md</c> name exactly these.
    /// </summary>
    [Fact]
    public void The_documented_flags_are_the_implemented_ones()
    {
        string[] documented = ["--ingest", "--live", "--no-ingest", "--no-live", "--plugin", "--no-plugin"];

        CollectorCommandLine.KnownFlags.Order(StringComparer.Ordinal)
            .ShouldBe(documented.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("--plugin", "true")]
    [InlineData("--no-plugin", "false")]
    public void The_generic_plugin_flag_switches_any_plugin_by_id(string flag, string expected)
    {
        // The named shorthands only cover the two plugins that exist today. A plugin added later
        // must be switchable without waiting for a word of its own.
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(CollectorCommandLine.Expand([flag, "Live"]))
            .Build();

        configuration["Collector:Live:Enabled"].ShouldBe(expected);
    }

    [Fact]
    public void The_generic_flag_is_case_insensitive_like_the_shorthands()
    {
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(CollectorCommandLine.Expand(["--PLUGIN", "Ingest"]))
            .Build();

        configuration["Collector:Ingest:Enabled"].ShouldBe("true");
    }

    [Theory]
    [InlineData("--plugin")]
    [InlineData("--no-plugin")]
    public void A_plugin_flag_without_an_id_is_reported_rather_than_ignored(string flag)
    {
        // Dropping it silently would start the collector with the opposite set of plugins to the one
        // asked for, which is only discoverable after the race.
        var act = () => CollectorCommandLine.Expand([flag]);

        act.ShouldThrow<ArgumentException>().Message.ShouldContain(flag);
    }

    [Fact]
    public void A_plugin_flag_followed_by_another_flag_is_reported_rather_than_consuming_it()
    {
        var act = () => CollectorCommandLine.Expand(["--plugin", "--no-ingest"]);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Unrecognised_arguments_still_pass_through_untouched()
    {
        CollectorCommandLine.Expand(["--Collector:PollInterval", "00:00:00.02"])
            .ShouldBe(["--Collector:PollInterval", "00:00:00.02"]);
    }
}
