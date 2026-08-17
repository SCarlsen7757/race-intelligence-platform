using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Live.Contracts.View;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// Every completed lap the hub has watched go by, per driver.
/// </summary>
/// <remarks>
/// <para>
/// The one piece of live state that is deliberately <i>not</i> "the latest value wins". Everything
/// else on this path describes a moment and is worthless once a newer moment exists; a stint is the
/// opposite — its meaning is the shape of the whole sequence, and a race engineer reading tyre
/// degradation off five laps needs all five. It is still not storage: the archive path owns
/// permanence, and this is forgotten with the room.
/// </para>
/// <para>
/// <b>Fed from the selected snapshot, not from one publisher's frame.</b> The room hands this
/// whichever snapshot the tower is projected from, so when
/// <c>LiveRoom.SelectSnapshotLocked</c> switches publishers mid-race the accumulator simply keeps
/// reading the same driver keys from a different source. What makes that safe is that recording is
/// <b>idempotent by lap number</b>: a snapshot from a publisher that is a lap behind reports a lap
/// number already recorded and produces nothing, so a switch can neither duplicate a lap nor skip
/// one.
/// </para>
/// <para>
/// <b>Guarded by the owning room's lock.</b> Nothing here is thread-safe on its own — it is only
/// ever touched from inside <c>LiveRoom</c>'s <c>_gate</c>, and <see cref="SnapshotFor"/> returns a
/// copy precisely so the caller can broadcast it after leaving the lock.
/// </para>
/// </remarks>
internal sealed class LapHistoryAccumulator
{
    /// <summary>
    /// The most laps kept for one driver before the oldest are dropped.
    /// </summary>
    /// <remarks>
    /// Bounded because a room outlives any one session's sanity: an endurance race, or a practice
    /// server left running overnight, would otherwise grow this without limit for every car on the
    /// grid. 512 laps is well beyond any real session — the longest Le Mans is under 400 — so the
    /// cap is a memory backstop rather than a limit anybody is expected to reach. When it does bite,
    /// the oldest laps go and the wire says so, because a truncated history that claims to be
    /// complete would have a race engineer reading a stint that starts in the wrong place.
    /// </remarks>
    public const int MaxLapsPerDriver = 512;

    private readonly Dictionary<string, DriverLapHistory> _drivers = new(StringComparer.Ordinal);

    /// <summary>
    /// Applies a standings snapshot, returning the driver keys whose history gained a lap.
    /// </summary>
    /// <remarks>
    /// Returning only what changed is what keeps the broadcast proportional to laps completed
    /// rather than to snapshots received: standings arrive at 10 Hz and a lap finishes about once a
    /// minute, so all but a handful of calls return nothing at all.
    /// </remarks>
    public IReadOnlyList<string> Observe(SessionStandings standings)
    {
        ArgumentNullException.ThrowIfNull(standings);

        List<string>? changed = null;

        foreach (var driver in standings.Drivers)
        {
            string key = LiveTowerProjector.DriverKeyFor(driver);

            if (!_drivers.TryGetValue(key, out var history))
            {
                history = new DriverLapHistory();
                _drivers[key] = history;
            }

            if (history.Observe(driver))
            {
                (changed ??= []).Add(key);
            }
        }

        return changed ?? (IReadOnlyList<string>)[];
    }

    /// <summary>Whether this driver has been seen in a snapshot at all.</summary>
    /// <remarks>
    /// Separate from "has laps": a driver who has been seen but completed nothing has an empty
    /// history, which is a real answer. Only a key that has never appeared is unknown.
    /// </remarks>
    public bool Knows(string driverKey) => _drivers.ContainsKey(driverKey);

    /// <summary>
    /// Whether this driver's most recently completed lap was valid, for the tower's last-lap column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tower cannot answer this from a standings snapshot, and the reason is the same one this
    /// class already carries <c>_lapInProgressValid</c> for: the simulator's flag describes the lap
    /// <i>in progress</i>. Reading it against the previous lap's time — which the tower did until
    /// this existed — strikes a clean lap through the moment the driver puts a wheel off on the next
    /// one, and leaves the lap that was actually binned looking legitimate.
    /// </para>
    /// <para>
    /// <see langword="null"/> for a driver with no recorded laps, and for a lap recorded while the
    /// publisher was away and so never described by a snapshot. Both mean "not known", which is not
    /// the same as invalid.
    /// </para>
    /// </remarks>
    public bool? LastCompletedLapValidFor(string driverKey) =>
        _drivers.TryGetValue(driverKey, out var history) ? history.LastCompletedLapValid : null;

    /// <summary>
    /// The full history for one driver, as a copy safe to broadcast outside the room's lock.
    /// </summary>
    public (IReadOnlyList<LapRecord> Laps, bool Truncated) SnapshotFor(string driverKey) =>
        _drivers.TryGetValue(driverKey, out var history)
            ? history.Snapshot()
            : ([], false);

    /// <summary>One driver's laps, and what it takes to know when the next one is complete.</summary>
    private sealed class DriverLapHistory
    {
        private readonly List<LapRecord> _laps = [];

        private int _highestRecorded;
        private bool _seeded;
        private bool _truncated;

        /// <summary>
        /// The <c>CurrentLapValid</c> flag as of the previous observation.
        /// </summary>
        /// <remarks>
        /// The whole reason this field exists. <c>CurrentLapValid</c> describes the lap
        /// <i>in progress</i>, so by the time a snapshot reports the lap count has gone up, the flag
        /// on that same snapshot has already moved on to the new lap. The validity that belongs to
        /// the lap just completed is the one observed a tick earlier, which is what this holds.
        /// </remarks>
        private bool? _lapInProgressValid;

        public bool Observe(DriverStanding driver)
        {
            // Without a lap count there is no edge to trigger on. Ignored entirely rather than
            // treated as zero: a simulator that does not report lap counts must produce no history,
            // not a history that restarts from lap 1 on every snapshot.
            if (driver.CompletedLaps is not { } completed)
            {
                return false;
            }

            // First sighting seeds the counter and records nothing. The snapshot's PreviousLapTime
            // may well describe a real lap, but the hub has no way to tell whether it belongs to
            // this session or is left over from the previous one the simulator ran — and a history
            // whose first row might be from another session is worse than one that starts where the
            // hub started watching.
            if (!_seeded)
            {
                _seeded = true;
                _highestRecorded = completed;
                _lapInProgressValid = driver.CurrentLapValid;
                return false;
            }

            if (completed <= _highestRecorded)
            {
                _lapInProgressValid = driver.CurrentLapValid;
                return false;
            }

            // One record per lap actually completed, not one per observation — mirroring the
            // connector's own lap emission. Standings arrive far faster than laps finish, so this
            // loop runs once in normal operation; it runs more only when a publisher was away for
            // longer than a lap, and then the laps it missed are recorded with their timings
            // unknown rather than silently absent. Only the newest is described by this snapshot,
            // so giving the others its numbers would attribute one lap's time to several.
            for (int lapNumber = _highestRecorded + 1; lapNumber <= completed; lapNumber++)
            {
                bool describedByThisSnapshot = lapNumber == completed;

                Append(new LapRecord(
                    lapNumber,
                    describedByThisSnapshot ? LiveTowerProjector.ToMilliseconds(driver.PreviousLapTime) : null,
                    describedByThisSnapshot ? LiveTowerProjector.ToMilliseconds(driver.PreviousSectorTimes) : [],
                    describedByThisSnapshot ? _lapInProgressValid : null));
            }

            _highestRecorded = completed;
            _lapInProgressValid = driver.CurrentLapValid;
            return true;
        }

        /// <summary>The validity recorded against the newest lap, or null before there is one.</summary>
        public bool? LastCompletedLapValid => _laps.Count > 0 ? _laps[^1].Valid : null;

        public (IReadOnlyList<LapRecord> Laps, bool Truncated) Snapshot() => ([.. _laps], _truncated);

        private void Append(LapRecord lap)
        {
            _laps.Add(lap);

            if (_laps.Count <= MaxLapsPerDriver)
            {
                return;
            }

            // O(n) on a 512-element list, once per lap completed — a few microseconds a minute.
            _laps.RemoveAt(0);
            _truncated = true;
        }
    }
}
