using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Live.Contracts.View;
using RaceIntelligence.Web.Live;
using RaceIntelligence.Web.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Live;

/// <summary>
/// Covers the projection from a simulator's scoring view into the rows a race engineer reads.
/// </summary>
public sealed class LiveTowerProjectorTests
{
    private static IReadOnlySet<string> Self(params string[] keys) => keys.ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Rows_are_ordered_by_position()
    {
        var standings = LiveDtoFactory.Standings(
            LiveDtoFactory.Standing(simDriverId: "c", position: 3),
            LiveDtoFactory.Standing(simDriverId: "a", position: 1),
            LiveDtoFactory.Standing(simDriverId: "b", position: 2));

        var rows = LiveTowerProjector.Project(standings, Self());

        rows.Select(row => row.DriverKey).ShouldBe(["id:a", "id:b", "id:c"]);
    }

    /// <summary>
    /// A car with no reported position has not retired to the back of the field; it is a car the
    /// simulator has not placed. Sorting it last keeps it visible without making a claim the data
    /// does not support.
    /// </summary>
    [Fact]
    public void A_car_with_no_position_sorts_last_rather_than_first()
    {
        var standings = LiveDtoFactory.Standings(
            LiveDtoFactory.Standing(simDriverId: "unplaced", position: null),
            LiveDtoFactory.Standing(simDriverId: "leader", position: 1));

        var rows = LiveTowerProjector.Project(standings, Self());

        rows.Select(row => row.DriverKey).ShouldBe(["id:leader", "id:unplaced"]);
    }

    /// <summary>
    /// The tier is what the dashboard branches on to decide whether a row can be opened into a
    /// telemetry panel, so it has to follow whose machine is actually publishing.
    /// </summary>
    [Fact]
    public void A_driver_whose_own_machine_is_publishing_is_marked_self()
    {
        var standings = LiveDtoFactory.Standings(
            LiveDtoFactory.Standing(simDriverId: "publishing", position: 1),
            LiveDtoFactory.Standing(simDriverId: "watched", position: 2));

        var rows = LiveTowerProjector.Project(standings, Self("id:publishing"));

        rows.Single(row => row.DriverKey == "id:publishing").Tier.ShouldBe(LiveDataTier.Self);
        rows.Single(row => row.DriverKey == "id:watched").Tier.ShouldBe(LiveDataTier.Observed);
    }

    /// <summary>
    /// The identity ladder is shared with the roster fingerprint so that a viewer's focus
    /// subscription keeps resolving once step 6 merges two clients on the same keys.
    /// </summary>
    [Theory]
    [InlineData("4242", null, "Mark", "id:4242")]
    [InlineData(null, 7, "Mark", "slot:7")]
    [InlineData(null, null, "Mark", "name:Mark")]
    public void The_driver_key_follows_the_identity_ladder(
        string? simDriverId,
        int? slotId,
        string displayName,
        string expected)
    {
        var standing = LiveDtoFactory.Standing(simDriverId, slotId, displayName);

        LiveTowerProjector.DriverKeyFor(standing).ShouldBe(expected);
    }

    /// <summary>
    /// The prefix is what keeps a driver whose identity is the string "7" from colliding with the
    /// driver in slot 7 — and the fallbacks exist precisely for sessions where identities are
    /// missing, which is exactly when such a collision would arise.
    /// </summary>
    [Fact]
    public void A_driver_id_and_a_slot_with_the_same_digits_do_not_collide()
    {
        string byId = LiveTowerProjector.DriverKeyFor(LiveDtoFactory.Standing(simDriverId: "7"));
        string bySlot = LiveTowerProjector.DriverKeyFor(LiveDtoFactory.Standing(slotId: 7));

        byId.ShouldNotBe(bySlot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_publisher_with_nothing_identifying_its_own_car_maps_to_no_driver_key(string? empty) =>
        LiveTowerProjector.DriverKeyForLocalCar(empty, slotId: null, displayName: empty).ShouldBeNull();

    /// <summary>
    /// The regression this whole fallback chain exists for.
    /// </summary>
    /// <remarks>
    /// A publisher's local car has to resolve to the key its own tower row was built with, or the
    /// focus and extras channels are dropped for want of a row to attach them to — the dashboard
    /// then shows a healthy timing tower with no pedals, no tyre data and no damage on it. RaceRoom
    /// issues account ids only to authenticated online sessions, so every offline race arrives with
    /// no identity and lands on the <c>slot:</c> fallback, which is the case that used to break.
    /// </remarks>
    [Fact]
    public void A_publisher_with_no_identity_falls_back_to_the_slot_its_tower_row_uses()
    {
        string rowKey = LiveTowerProjector.DriverKeyFor(
            LiveDtoFactory.Standing(simDriverId: null, slotId: 3, displayName: "Mark"));

        LiveTowerProjector
            .DriverKeyForLocalCar(simDriverId: null, slotId: 3, displayName: "Mark")
            .ShouldBe(rowKey);
    }

    [Fact]
    public void A_publisher_with_neither_identity_nor_slot_falls_back_to_the_name_its_row_uses()
    {
        string rowKey = LiveTowerProjector.DriverKeyFor(
            LiveDtoFactory.Standing(simDriverId: null, slotId: null, displayName: "Mark"));

        LiveTowerProjector
            .DriverKeyForLocalCar(simDriverId: null, slotId: null, displayName: "Mark")
            .ShouldBe(rowKey);
    }

    /// <summary>An identity still wins outright, so an online session is unaffected by the fallbacks.</summary>
    [Fact]
    public void An_identity_is_preferred_over_the_slot_and_the_name() =>
        LiveTowerProjector
            .DriverKeyForLocalCar(simDriverId: "4242", slotId: 3, displayName: "Mark")
            .ShouldBe("id:4242");

    [Fact]
    public void Durations_cross_to_the_browser_as_milliseconds() =>
        LiveTowerProjector
            .Project(
                LiveDtoFactory.Standings(LiveDtoFactory.Standing(
                    simDriverId: "a", position: 1, bestLap: TimeSpan.FromSeconds(102.5))),
                Self())
            .Single().BestLapMs.ShouldBe(102_500);

    /// <summary>
    /// The sentinel discipline the whole platform runs on, at the point where it is most visible: a
    /// gap the simulator does not report must not reach a race engineer as a confident 0.0s. That
    /// is not a smaller version of "unknown" — it is the number someone makes a pit call on.
    /// </summary>
    [Fact]
    public void An_unreported_time_stays_null_rather_than_becoming_zero()
    {
        var rows = LiveTowerProjector.Project(
            LiveDtoFactory.Standings(LiveDtoFactory.Standing(simDriverId: "a", position: 1)),
            Self());

        var row = rows.Single();
        row.BestLapMs.ShouldBeNull();
        row.PreviousLapMs.ShouldBeNull();
        row.GapToCarAheadMs.ShouldBeNull();
        row.CompletedLaps.ShouldBeNull();
    }

    [Fact]
    public void Sector_times_are_converted_element_by_element_and_keep_their_gaps()
    {
        var standing = LiveDtoFactory.Standing(simDriverId: "a", position: 1) with
        {
            PreviousSectorTimes = [TimeSpan.FromSeconds(30), null, TimeSpan.FromSeconds(95)],
        };

        var row = LiveTowerProjector.Project(LiveDtoFactory.Standings(standing), Self()).Single();

        row.PreviousSectorMs.ShouldBe([30_000, null, 95_000]);
    }

    [Fact]
    public void Status_enums_cross_as_their_underlying_values()
    {
        var standing = LiveDtoFactory.Standing(simDriverId: "a", position: 1) with
        {
            PitStopStatus = PitStopStatus.Served,
            FinishStatus = DriverFinishStatus.Finished,
        };

        var row = LiveTowerProjector.Project(LiveDtoFactory.Standings(standing), Self()).Single();

        row.PitStopStatus.ShouldBe((int)PitStopStatus.Served);
        row.FinishStatus.ShouldBe((int)DriverFinishStatus.Finished);
    }

    [Fact]
    public void An_empty_field_projects_to_no_rows() =>
        LiveTowerProjector.Project(LiveDtoFactory.Standings(), Self()).ShouldBeEmpty();
}
