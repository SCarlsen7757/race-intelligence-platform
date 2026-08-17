using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Live.Contracts.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Live.Contracts.Tests;

/// <summary>
/// Covers the fingerprint that tells two clients in one online server apart from two clients whose
/// sessions merely share a key. Getting this wrong in either direction is a visible bug: too strict
/// and two teammates never merge, too loose and a stranger's telemetry appears in your timing
/// tower.
/// </summary>
public sealed class LiveRosterFingerprintTests
{
    private static DriverStanding Driver(string? simDriverId = null, int? slotId = null, string name = "Someone") => new()
    {
        SimDriverId = simDriverId,
        SlotId = slotId,
        DisplayName = name,
        CompletedLaps = 0,
    };

    [Fact]
    public void The_same_roster_in_a_different_order_fingerprints_alike()
    {
        DriverStanding[] asAlice = [Driver("1"), Driver("2"), Driver("3")];
        DriverStanding[] asBob = [Driver("3"), Driver("1"), Driver("2")];

        // Two clients list the field in whatever order their own simulator does. If order mattered,
        // two people in the same server would essentially never merge.
        LiveRosterFingerprint.Compute(asAlice).ShouldBe(LiveRosterFingerprint.Compute(asBob));
    }

    [Fact]
    public void Different_rosters_fingerprint_differently()
    {
        DriverStanding[] here = [Driver("1"), Driver("2")];
        DriverStanding[] elsewhere = [Driver("3"), Driver("4")];

        LiveRosterFingerprint.Compute(here).ShouldNotBe(LiveRosterFingerprint.Compute(elsewhere));
    }

    [Fact]
    public void An_empty_roster_fingerprints_as_empty_rather_than_as_a_hash_of_nothing()
    {
        // A hash of the empty string would make every client that has not yet seen the field look
        // like it belonged to the same room as every other.
        LiveRosterFingerprint.Compute(Array.Empty<DriverStanding>()).ShouldBe(string.Empty);
    }

    [Fact]
    public void A_duplicated_entry_does_not_change_the_fingerprint()
    {
        DriverStanding[] settled = [Driver("1"), Driver("2")];
        DriverStanding[] midDisconnect = [Driver("1"), Driver("2"), Driver("2")];

        LiveRosterFingerprint.Compute(settled).ShouldBe(LiveRosterFingerprint.Compute(midDisconnect));
    }

    /// <summary>
    /// The fallback keys are prefixed for a reason: they are used precisely when driver identities
    /// are missing, which is exactly when an unprefixed "7" (a slot) and "7" (an id) would collide.
    /// </summary>
    [Fact]
    public void An_identity_and_a_slot_with_the_same_number_are_different_keys()
    {
        LiveRosterFingerprint.KeyFor(Driver(simDriverId: "7"))
            .ShouldNotBe(LiveRosterFingerprint.KeyFor(Driver(slotId: 7)));
    }

    [Fact]
    public void Identity_is_preferred_over_slot_and_slot_over_name()
    {
        LiveRosterFingerprint.KeyFor(Driver(simDriverId: "42", slotId: 7, name: "Kimi")).ShouldBe("id:42");
        LiveRosterFingerprint.KeyFor(Driver(slotId: 7, name: "Kimi")).ShouldBe("slot:7");
        LiveRosterFingerprint.KeyFor(Driver(name: "Kimi")).ShouldBe("name:Kimi");
    }

    [Fact]
    public void An_empty_identity_falls_through_to_the_next_key()
    {
        // RaceRoom reports no id for an offline slot, and the mapper turns that into null; an empty
        // string is the same absence arriving by a different route and must behave identically.
        LiveRosterFingerprint.KeyFor(Driver(simDriverId: "", slotId: 7)).ShouldBe("slot:7");
    }

    [Fact]
    public void Fingerprints_are_thirty_two_hex_characters()
    {
        string fingerprint = LiveRosterFingerprint.Compute([Driver("1")]);

        fingerprint.Length.ShouldBe(32);
        fingerprint.ShouldAllBe(c => char.IsAsciiDigit(c) || (c >= 'a' && c <= 'f'));
    }

    [Theory]
    // Identical rosters: everything shared.
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "b", "c" }, 1.0)]
    // Disjoint: two different servers that happen to share a session key.
    [InlineData(new[] { "a", "b" }, new[] { "c", "d" }, 0.0)]
    // A client that joined late sees a subset. Measured against the smaller roster, that is a full
    // match — measured against the union it would score 0.4 and never merge.
    [InlineData(new[] { "a", "b" }, new[] { "a", "b", "c", "d", "e" }, 1.0)]
    // Half the smaller roster shared: genuinely ambiguous, and below the merge threshold.
    [InlineData(new[] { "a", "x" }, new[] { "a", "b", "c" }, 0.5)]
    public void Overlap_is_measured_against_the_smaller_roster(string[] left, string[] right, double expected)
    {
        LiveRosterFingerprint.Overlap(left, right).ShouldBe(expected, tolerance: 1e-9);
    }

    [Fact]
    public void Overlap_with_an_unknown_roster_confirms_nothing()
    {
        // Zero, not one: an empty roster is the absence of evidence, and treating it as a perfect
        // match would merge a client that has seen nothing into the first room it keyed onto.
        LiveRosterFingerprint.Overlap([], ["a", "b"]).ShouldBe(0.0);
        LiveRosterFingerprint.Overlap(["a", "b"], []).ShouldBe(0.0);
    }

    [Fact]
    public void A_realistic_roster_round_trips_through_the_standings_it_came_from()
    {
        var standings = LiveDtoFactory.FullyPopulatedStandings();

        string fingerprint = LiveRosterFingerprint.Compute(standings.Drivers);
        var keys = standings.Drivers.Select(LiveRosterFingerprint.KeyFor).ToArray();

        LiveRosterFingerprint.Compute(keys).ShouldBe(fingerprint);
        LiveRosterFingerprint.Overlap(keys, keys).ShouldBe(1.0);
    }
}
