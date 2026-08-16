using System.Runtime.CompilerServices;
using RaceIntelligence.Connectors.RaceRoom.Interop;

namespace RaceIntelligence.Connectors.RaceRoom;

/// <summary>The result of attempting to read the driver array out of the shared memory block.</summary>
internal enum DriverArrayReadOutcome
{
    /// <summary>The array was read cleanly and describes a single simulation frame.</summary>
    Ok,

    /// <summary>The game rewrote the block mid-read; the entries mix two frames and must be discarded.</summary>
    Torn,

    /// <summary>The array could not be read at all — the view is gone, or the block is too small to hold it.</summary>
    Failed,
}

/// <summary>
/// Reads RaceRoom's <c>all_drivers_data_1</c> array — the per-car scoring view of everyone in the
/// session — out of the shared memory block.
/// </summary>
/// <remarks>
/// <para>
/// The array is deliberately <b>not</b> a field of <see cref="R3ESharedRaw"/>: it is 128 entries
/// and 41,984 of the block's 43,996 bytes, and mapping it would copy all of it on every poll. It is
/// the block's final field, so reading it separately at <see cref="R3ESharedRaw.AllDriversOffset"/>
/// costs nothing structurally — see the trailing comment in <c>R3ESharedRaw.cs</c>.
/// </para>
/// <para>
/// Only the first <see cref="R3ESharedRaw.NumCars"/> entries are read. The rest are stale entries
/// from whatever session last filled them, and a 20-car grid is ~6.5 KB rather than the full 42 KB.
/// </para>
/// <para>
/// <see cref="R3EVersionGate"/> has already checked the game's self-reported
/// <c>driver_data_size</c> and <c>all_drivers_offset</c> against these structs before any of this
/// runs, so the offset arithmetic here rests on a verified layout rather than an assumed one. The
/// bounds check below is belt-and-braces against a truncated mapping.
/// </para>
/// </remarks>
internal static class R3EDriverDataReader
{
    /// <summary>
    /// The largest field count the caller will accept, matching RaceRoom's own
    /// <c>R3E_NUM_DRIVERS_MAX</c>. A block reporting more than this is describing something we do
    /// not understand, and is rejected rather than trusted.
    /// </summary>
    internal const int MaxDrivers = 128;

    /// <summary>
    /// Reads the driver array for the field described by <paramref name="raw"/>.
    /// </summary>
    /// <param name="view">The shared memory block.</param>
    /// <param name="raw">
    /// The snapshot that supplied <see cref="R3ESharedRaw.NumCars"/> and
    /// <see cref="R3ESharedRaw.AllDriversOffset"/>, and whose
    /// <c>Player.GameSimulationTicks</c> the torn-frame check is made against.
    /// </param>
    /// <param name="drivers">The entries read, valid only when the outcome is <see cref="DriverArrayReadOutcome.Ok"/>.</param>
    /// <remarks>
    /// Torn-frame detection mirrors <c>RaceRoomTelemetrySource.TryReadRaw</c>: RaceRoom publishes
    /// no seqlock, so the only available signal is that the simulation tick counter still holds the
    /// value it had when <paramref name="raw"/> was captured. This read spans many more bytes than
    /// a single struct read, so it is <i>more</i> likely to tear, not less — discarding a torn
    /// snapshot is free, since the next poll follows within milliseconds.
    /// </remarks>
    public static DriverArrayReadOutcome TryRead(
        ISharedMemoryView view,
        in R3ESharedRaw raw,
        out R3EDriverData[] drivers)
    {
        drivers = [];

        int count = raw.NumCars;
        if (count is <= 0 or > MaxDrivers)
        {
            // Not an error: between sessions RaceRoom reports no cars at all. A count above the
            // documented maximum is an error, but the caller's recovery is identical — report no
            // standings this tick — so both take this path.
            return count == 0 ? DriverArrayReadOutcome.Ok : DriverArrayReadOutcome.Failed;
        }

        int entrySize = Unsafe.SizeOf<R3EDriverData>();

        // The frame's own all_drivers_offset is checked against the layout this connector compiles
        // to, not merely for being positive. R3EVersionGate validated it once at connect; this
        // value is re-read from every frame, so a torn or garbage header could carry anything —
        // including a value that overflows when four is added to it, which is why the widening cast
        // below happens before the addition rather than after.
        if (raw.AllDriversOffset != R3EVersionGate.ExpectedAllDriversOffset)
        {
            return DriverArrayReadOutcome.Failed;
        }

        // all_drivers_offset points at num_cars, NOT at the array — the array begins immediately
        // after it. Reading at the reported offset directly would take num_cars as the first four
        // bytes of the first driver's name and shift every entry by four bytes. The layout tests
        // pin both halves of this: num_cars sits at 2008, and this struct is 2012 bytes.
        long offset = (long)raw.AllDriversOffset + sizeof(int);
        if (view.Length < offset + ((long)count * entrySize))
        {
            return DriverArrayReadOutcome.Failed;
        }

        var buffer = new R3EDriverData[count];
        try
        {
            // Re-read the tick counter immediately before the array rather than reusing the value
            // captured with `raw`. Between that capture and here the caller has mapped a whole
            // telemetry sample, including two JSON writer passes — easily longer than RaceRoom's
            // ~16.7 ms write interval on a loaded machine. Comparing against the stale value would
            // report a tear whenever the game wrote at any point in the tick, which on a slow tick
            // is every time: standings would stop entirely, silently, with nothing to see in the
            // logs. Bracketing only the copy asks the question actually worth asking.
            int ticksBeforeRead = view.Read<int>(RaceRoomTelemetrySource.SimulationTicksOffset);

            for (int i = 0; i < count; i++)
            {
                buffer[i] = view.Read<R3EDriverData>(offset + ((long)i * entrySize));
            }

            int ticksAfterRead = view.Read<int>(RaceRoomTelemetrySource.SimulationTicksOffset);
            if (ticksAfterRead != ticksBeforeRead)
            {
                return DriverArrayReadOutcome.Torn;
            }
        }
        catch (ObjectDisposedException)
        {
            return DriverArrayReadOutcome.Failed;
        }
        catch (ArgumentException)
        {
            // The view rejected the offset — the mapping is smaller than the header claimed.
            return DriverArrayReadOutcome.Failed;
        }

        drivers = buffer;
        return DriverArrayReadOutcome.Ok;
    }
}
