using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Live.Contracts;
using RaceIntelligence.Live.Contracts.View;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// Turns a publisher's standings snapshot into the timing tower a browser renders.
/// </summary>
/// <remarks>
/// <para>
/// The one place that decides a car's <see cref="LiveDataTier"/>, and therefore the one place that
/// decides whether a row can be opened into a telemetry panel.
/// </para>
/// <para>
/// <b>Step 4 projects a single publisher's view; it does not merge several.</b> When two clients
/// are in one room the hub picks the more complete snapshot rather than combining them field by
/// field. That is honest about what is implemented: everyone in the session still appears, and
/// every driver running a client is still marked <see cref="LiveDataTier.Self"/> and still gets a
/// focus stream — what is missing is only preferring one publisher's timing over another's for a
/// car both can see, which is what the authority ladder in step 6 adds. Doing it that way round
/// keeps this projection a pure function of one snapshot, which is what makes it testable at all.
/// </para>
/// </remarks>
public static class LiveTowerProjector
{
    /// <summary>
    /// Projects a standings snapshot into tower rows, sorted by position.
    /// </summary>
    /// <param name="standings">The snapshot to project.</param>
    /// <param name="selfDriverKeys">
    /// Driver keys whose own machine is publishing, as produced by
    /// <see cref="LiveRosterFingerprint.KeyFor"/>. Those rows are marked
    /// <see cref="LiveDataTier.Self"/>.
    /// </param>
    public static IReadOnlyList<TowerRow> Project(
        SessionStandings standings,
        IReadOnlySet<string> selfDriverKeys)
    {
        ArgumentNullException.ThrowIfNull(standings);
        ArgumentNullException.ThrowIfNull(selfDriverKeys);

        var rows = new List<TowerRow>(standings.Drivers.Count);
        foreach (var driver in standings.Drivers)
        {
            string key = LiveRosterFingerprint.KeyFor(driver);
            rows.Add(ToRow(driver, key, selfDriverKeys.Contains(key) ? LiveDataTier.Self : LiveDataTier.Observed));
        }

        // Sorted here rather than in the browser so every viewer of a room renders the same order,
        // and so a client cannot be the thing that decides what "leading" means. Cars with no
        // reported position sort last: a car that has not been placed yet has not retired to the
        // back of the field, and showing it as though it had would be a claim the data does not
        // make.
        rows.Sort(static (left, right) => (left.Position ?? int.MaxValue).CompareTo(right.Position ?? int.MaxValue));

        return rows;
    }

    /// <summary>The key a driver is known by within a room. See <see cref="LiveRosterFingerprint.KeyFor"/>.</summary>
    /// <remarks>
    /// Reused rather than reinvented so the identity the merge joins on in step 6 is the identity
    /// viewers already subscribe with — a focus subscription must not stop resolving the moment two
    /// clients are in one room.
    /// </remarks>
    public static string DriverKeyFor(DriverStanding driver) => LiveRosterFingerprint.KeyFor(driver);

    /// <summary>
    /// The driver key a simulator driver id maps to, or <see langword="null"/> when the id is
    /// absent.
    /// </summary>
    /// <remarks>
    /// Mirrors the <c>id:</c> branch of <see cref="LiveRosterFingerprint.KeyFor"/>. This is how a
    /// publisher's announced local driver, and the <c>SimDriverId</c> on its self frames, are
    /// matched to a tower row. A publisher whose simulator reports no identity for the local car
    /// cannot be matched to a row at all, and its rich telemetry goes unattached rather than being
    /// guessed onto someone.
    /// </remarks>
    public static string? DriverKeyForSimDriverId(string? simDriverId) =>
        string.IsNullOrEmpty(simDriverId) ? null : $"id:{simDriverId}";

    private static TowerRow ToRow(DriverStanding driver, string driverKey, LiveDataTier tier) => new(
        driverKey,
        driver.DisplayName,
        driver.CarNumber,
        driver.SimCarId,
        driver.SimCarClassId,
        driver.Position,
        driver.PositionInClass,
        driver.CompletedLaps,
        driver.TrackPositionFraction,
        driver.Sector,
        driver.Speed,
        ToMilliseconds(driver.CurrentLapTime),
        ToMilliseconds(driver.PreviousLapTime),
        ToMilliseconds(driver.BestLapTime),
        driver.CurrentLapValid,
        ToMilliseconds(driver.CurrentSectorTimes),
        ToMilliseconds(driver.PreviousSectorTimes),
        ToMilliseconds(driver.BestSectorTimes),
        ToMilliseconds(driver.GapToCarAhead),
        ToMilliseconds(driver.GapToCarBehind),
        driver.InPitLane,
        (int)driver.PitStopStatus,
        driver.PitStopCount,
        (int)driver.FinishStatus,
        driver.PenaltyCount,
        tier);

    /// <summary>
    /// Converts a duration to milliseconds for JSON, preserving <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The null-preservation is the point. A gap that the simulator does not report must not reach
    /// a race engineer as <c>0.0s</c> — that is not a smaller version of "unknown", it is a
    /// different and confident claim, and it is the one someone would make a pit call on.
    /// </remarks>
    internal static double? ToMilliseconds(TimeSpan? value) => value?.TotalMilliseconds;

    internal static IReadOnlyList<double?> ToMilliseconds(IReadOnlyList<TimeSpan?> values)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var converted = new double?[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            converted[i] = ToMilliseconds(values[i]);
        }

        return converted;
    }
}
