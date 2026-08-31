using Microsoft.Extensions.Options;
using RaceIntelligence.Ingest.Api.Auth;
using Shouldly;

namespace RaceIntelligence.Ingest.Api.Tests.Auth;

/// <summary>
/// Covers the check standing between anyone who can reach this service and the telemetry store.
/// </summary>
public sealed class CollectorKeyGateTests
{
    private static CollectorKeyGate Gate(params (string Label, string Key)[] keys) =>
        new(Options.Create(new IngestAuthOptions
        {
            ApiKeys = keys.ToDictionary(pair => pair.Label, pair => pair.Key),
        }));

    [Fact]
    public void A_configured_key_is_accepted()
    {
        Gate(("mark-pc", "secret")).TryResolve("secret", out var label).ShouldBeTrue();
        label.ShouldBe("mark-pc");
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("secre")]
    [InlineData("secrets")]
    [InlineData("SECRET")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused(string? provided) =>
        Gate(("mark-pc", "secret")).TryResolve(provided, out _).ShouldBeFalse();

    /// <summary>
    /// The point of the whole change: two collectors, two keys, each resolving to its own label so
    /// one can be revoked without touching the other.
    /// </summary>
    [Theory]
    [InlineData("first-key", "mark-pc")]
    [InlineData("second-key", "friend-pc")]
    public void Every_configured_key_resolves_to_its_own_label(string provided, string expected)
    {
        var gate = Gate(("mark-pc", "first-key"), ("friend-pc", "second-key"));

        gate.TryResolve(provided, out var label).ShouldBeTrue();
        label.ShouldBe(expected);
    }

    /// <summary>
    /// An ingest API started with no keys is misconfigured, and the safe reading of that is "nobody
    /// may upload" rather than "anybody may" — including for a caller who also presents nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anything")]
    public void An_unconfigured_gate_accepts_nobody(string? provided) =>
        Gate().TryResolve(provided, out _).ShouldBeFalse();

    /// <summary>
    /// A key configured as blank must not become a usable credential for a caller who sends the
    /// header empty, which the empty-input guard alone would not prevent.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_blank_configured_key_is_not_a_credential(string? provided) =>
        Gate(("misconfigured", "")).TryResolve(provided, out _).ShouldBeFalse();

    /// <summary>
    /// The partition key is what the rate limiter buckets on, so it must separate distinct keys and
    /// never be the key itself.
    /// </summary>
    [Fact]
    public void The_partition_key_is_a_digest_not_the_key()
    {
        var partition = CollectorKeyGate.PartitionKey("secret");

        partition.ShouldNotBe("secret");
        partition.ShouldNotBe(CollectorKeyGate.PartitionKey("other"));
        partition.ShouldBe(CollectorKeyGate.PartitionKey("secret"));
        CollectorKeyGate.PartitionKey("").ShouldBeEmpty();
    }
}
