using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Connectors.RaceRoom;

/// <summary>
/// Grades a car's pit-lane progress across frames, for the cars RaceRoom only reports a binary
/// <c>in_pitlane</c> for.
/// </summary>
/// <remarks>
/// <para>
/// RaceRoom publishes its five-stage <c>pit_state</c> for the local car alone. Every other entry in
/// the driver array carries a flag and a speed, and nothing else about pitting — so entering and
/// exiting are indistinguishable from one frame, and separating them needs memory.
/// </para>
/// <para>
/// The memory is one bit per car: <b>has this car been seen stationary since it entered the pit
/// lane?</b> Before it has, it is on its way in; after, it is on its way out. That is the whole
/// state machine, and it is deliberately the smallest one that answers the question, because every
/// extra bit of inferred state is another thing that can be wrong on a tower a pit call is made
/// from.
/// </para>
/// <para>
/// <b>Inference, not observation.</b> A car that crawls through the pit lane without ever stopping
/// reads as entering for the whole transit, and a car observed only after its stop began reads as
/// exiting from the first frame it moves. Both are the honest reading of what was seen. What the
/// tracker never does is claim a stage for a car whose speed RaceRoom did not report — that returns
/// <see cref="PitLaneState.InPitLane"/>, the ungraded answer, rather than a guess.
/// </para>
/// </remarks>
internal sealed class R3EPitLaneTracker
{
    /// <summary>
    /// Below this, a car counts as stationary in its box. Metres per second.
    /// </summary>
    /// <remarks>
    /// Not zero. A car on the jacks still reports small non-zero speeds as the physics settles, and
    /// a threshold of exactly zero would flicker such a car between stopped and exiting for the
    /// length of its stop — which is precisely the moment a strategist is watching it.
    /// </remarks>
    private const float StoppedSpeedMetersPerSecond = 0.5f;

    /// <summary>
    /// Cars observed stationary since entering the pit lane, keyed by slot. Cleared per car the
    /// moment it leaves the pit lane, so the next visit starts over rather than opening on "exiting".
    /// </summary>
    private readonly HashSet<int> _stoppedThisVisit = [];

    /// <summary>Forgets every car — call when the session changes underneath the tracker.</summary>
    public void Clear() => _stoppedThisVisit.Clear();

    /// <summary>
    /// Grades one car's stage from this frame plus what earlier frames showed.
    /// </summary>
    /// <param name="slotId">
    /// The car's per-session slot, which is what the memory is keyed by. RaceRoom reuses slots
    /// across sessions but not within one, which is exactly the lifetime this state needs.
    /// </param>
    /// <param name="inPitLane">The simulator's flag, or <see langword="null"/> when it reports none.</param>
    /// <param name="speedMetersPerSecond">The car's speed, or <see langword="null"/> when unreported.</param>
    public PitLaneState Observe(int? slotId, bool? inPitLane, float? speedMetersPerSecond)
    {
        if (inPitLane is null)
        {
            Forget(slotId);
            return PitLaneState.Unavailable;
        }

        if (!inPitLane.Value)
        {
            Forget(slotId);
            return PitLaneState.OnTrack;
        }

        // A car with no slot cannot be remembered between frames, so it gets the ungraded answer
        // rather than one derived from another car's history.
        if (slotId is null || speedMetersPerSecond is null)
        {
            return PitLaneState.InPitLane;
        }

        if (speedMetersPerSecond.Value <= StoppedSpeedMetersPerSecond)
        {
            _stoppedThisVisit.Add(slotId.Value);
            return PitLaneState.Stopped;
        }

        return _stoppedThisVisit.Contains(slotId.Value) ? PitLaneState.Exiting : PitLaneState.Entering;
    }

    /// <summary>
    /// The local car's stage, which RaceRoom reports directly and therefore does not need inferring.
    /// </summary>
    /// <remarks>
    /// <paramref name="rawPitState"/> is the root block's <c>pit_state</c>: -1 N/A, 0 none,
    /// 1 requested stop, 2 entering pitlane, 3 stopped at pitspot, 4 exiting — the numbering
    /// <see cref="PitLaneState"/> was built to match.
    /// <para>
    /// Its "none" is not a claim about the pit lane, only about a stop: a car driving the pit lane
    /// with nothing scheduled reports 0 while sitting squarely in it. So 0 and the sentinel both
    /// defer to <paramref name="inferred"/>, and only a positive stage overrides it.
    /// </para>
    /// </remarks>
    public static PitLaneState FromLocalPitState(int rawPitState, PitLaneState inferred) =>
        rawPitState > 0 && Enum.IsDefined((PitLaneState)rawPitState)
            ? (PitLaneState)rawPitState
            : inferred;

    /// <summary>
    /// Whether a driver array entry is the car this machine is driving, by the same identity ladder
    /// <see cref="R3ETelemetryMapper"/> keys standings on.
    /// </summary>
    internal static bool IsLocalCar(in R3EDriverData driver, in R3ESharedRaw raw)
    {
        int localUserId = raw.VehicleInfo.UserId > 0 ? raw.VehicleInfo.UserId : raw.Player.UserId;
        if (localUserId > 0 && driver.DriverInfo.UserId > 0)
        {
            return driver.DriverInfo.UserId == localUserId;
        }

        // Offline, where RaceRoom issues no account id at all — the slot is the only thing left
        // that distinguishes the local car from the AI sharing the track with it.
        return raw.VehicleInfo.SlotId >= 0 && driver.DriverInfo.SlotId == raw.VehicleInfo.SlotId;
    }

    private void Forget(int? slotId)
    {
        if (slotId is not null)
        {
            _stoppedThisVisit.Remove(slotId.Value);
        }
    }
}
