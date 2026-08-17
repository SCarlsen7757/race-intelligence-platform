using RaceIntelligence.Connectors.RaceRoom.Interop;

namespace RaceIntelligence.Connectors.RaceRoom;

/// <summary>
/// How RaceRoom fills a <c>sector_time_*[3]</c> triple.
/// </summary>
/// <remarks>
/// <para>
/// <b>r3e.h does not say.</b> It documents the unit and the <c>-1.0 = N/A</c> sentinel for these
/// fields and nothing else, and neither the C nor the C# sample reads them. The two possibilities
/// produce values that both look entirely plausible — under one reading
/// <c>sector_time_previous_self[2]</c> is a ~92 second lap, under the other it is a ~30 second
/// sector — so guessing wrong would not announce itself. It would simply put a wrong lap time in
/// front of a race engineer.
/// </para>
/// <para>
/// So the connector does not guess: it works the answer out from the block itself. The root struct
/// carries <b>both</b> <c>lap_time_previous_self</c> and <c>sector_time_previous_self</c> for the
/// local car, and those two agree under exactly one of the readings. One completed lap settles it
/// for every car in the session.
/// </para>
/// </remarks>
internal enum R3ESectorTimeConvention
{
    /// <summary>
    /// Each entry is the elapsed time from the start of the lap to the end of that sector, so the
    /// last entry is the lap time. This is the assumption held until a lap proves otherwise —
    /// see <see cref="R3ESectorTimeConventionDetector"/> for why it is the more likely of the two.
    /// </summary>
    Cumulative,

    /// <summary>Each entry is that sector's own duration, so the lap time is their sum.</summary>
    PerSector,
}

/// <summary>
/// Works out which <see cref="R3ESectorTimeConvention"/> a running game uses, from a frame in which
/// the local car has completed a lap.
/// </summary>
/// <remarks>
/// <see cref="R3ESectorTimeConvention.Cumulative"/> is the starting assumption rather than a coin
/// toss. The root struct exposes <c>best_individual_sector_time_self</c> separately, documented as
/// "best time for each individual sector no matter lap" — a field that would be entirely redundant
/// if <c>sector_time_best_self</c> already held per-sector values. It is an inference, not a
/// guarantee, which is why it is still checked.
/// </remarks>
internal static class R3ESectorTimeConventionDetector
{
    /// <summary>
    /// How close the two candidate lap totals must be to the reported lap time to count as a
    /// match, in seconds.
    /// </summary>
    /// <remarks>
    /// Generous on purpose. These are 32-bit floats holding values around 90, where a single unit
    /// in the last place is already ~1e-5, and the game rounds sector and lap times independently.
    /// The two hypotheses differ by tens of seconds, so a loose tolerance costs no discrimination
    /// at all while making the test immune to representation noise.
    /// </remarks>
    private const float ToleranceSeconds = 0.05f;

    /// <summary>
    /// Returns the convention <paramref name="raw"/> demonstrates, or <paramref name="current"/>
    /// when this frame cannot settle it.
    /// </summary>
    /// <param name="raw">A snapshot to inspect.</param>
    /// <param name="current">What the caller currently believes, returned unchanged when this frame is inconclusive.</param>
    /// <remarks>
    /// Inconclusive frames are the common case and are not a problem: the check runs on every
    /// standings snapshot, and the first completed, fully-timed lap of the session resolves it.
    /// A frame is inconclusive when the previous lap or any of its splits is unreported — before
    /// the first lap, after a reset, or on an invalidated lap.
    /// </remarks>
    public static R3ESectorTimeConvention Detect(in R3ESharedRaw raw, R3ESectorTimeConvention current)
    {
        float lapTime = raw.LapTimePreviousSelf;
        float first = raw.SectorTimesPreviousSelf[0];
        float second = raw.SectorTimesPreviousSelf[1];
        float third = raw.SectorTimesPreviousSelf[2];

        if (lapTime <= 0f || first <= 0f || second <= 0f || third <= 0f)
        {
            return current;
        }

        // The two tests cannot both pass. Cumulative needs third == lapTime; per-sector needs
        // first + second + third == lapTime. Both would require first + second == 0, which the
        // guard above has already excluded.
        if (MathF.Abs(third - lapTime) <= ToleranceSeconds)
        {
            return R3ESectorTimeConvention.Cumulative;
        }

        if (MathF.Abs(first + second + third - lapTime) <= ToleranceSeconds)
        {
            return R3ESectorTimeConvention.PerSector;
        }

        // Neither fits — a partially updated frame, or a game that has changed the layout again.
        // Keep believing what we did rather than flip-flopping on one odd sample.
        return current;
    }
}
