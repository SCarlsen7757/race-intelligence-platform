using Microsoft.Extensions.Options;
using RaceIntelligence.Web.Live;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Live;

/// <summary>
/// Covers the only thing standing between the internet and the data every race engineer's decisions
/// are based on.
/// </summary>
public sealed class LiveApiKeyGateTests
{
    private static LiveApiKeyGate Gate(string configuredKey) =>
        new(Options.Create(new LiveHubOptions { ApiKey = configuredKey }));

    [Fact]
    public void The_configured_key_is_accepted() => Gate("secret").IsValid("secret").ShouldBeTrue();

    [Theory]
    [InlineData("wrong")]
    [InlineData("secre")]
    [InlineData("secrets")]
    [InlineData("SECRET")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused(string? provided) => Gate("secret").IsValid(provided).ShouldBeFalse();

    /// <summary>
    /// A hub started without a key is misconfigured, and the safe reading of that is "nobody may
    /// publish" rather than "anybody may" — including for a caller who also presents nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anything")]
    public void An_unconfigured_hub_accepts_nobody(string? provided) => Gate("").IsValid(provided).ShouldBeFalse();
}
