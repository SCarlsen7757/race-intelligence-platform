using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>
/// Validates a raw shared-memory view's header before anything else in it is trusted, so this
/// connector reads a block whose layout it has actually confirmed rather than one whose version
/// number merely looks familiar.
/// </summary>
/// <remarks>
/// <para>
/// The official r3e-api samples read the shared memory unconditionally, with no version check at
/// all. That is not safe for a connector whose only defense against a silent layout change is a
/// struct transcribed by hand from
/// <see href="https://github.com/kwstudios-sweden/r3e-api/blob/master/sample-csharp/src/R3E.cs">R3E.cs</see>:
/// a future version could reorder or resize fields, and reading it as <see cref="R3ESharedRaw"/>
/// would then produce plausible-looking but wrong numbers instead of an obvious failure.
/// </para>
/// <para>
/// <b>Version numbers alone are not the gate.</b> Upstream publishes no changelog mapping minor
/// versions to layout changes, so "minor &gt;= the one we transcribed" is a guess in both
/// directions: it refuses older minors that are in fact byte-identical over the prefix this
/// connector reads, and it accepts newer minors that may have inserted a field ahead of one we do
/// read. Instead, the primary check is <i>structural</i> — the block describes its own layout in
/// its header, and that self-description is compared against this connector's compiled structs:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>all_drivers_offset</c> — the byte offset of <see cref="R3ESharedRaw.NumCars"/>, which is
///     the end of the prefix this connector maps. Equal means every field ahead of it occupies
///     exactly the bytes this connector's transcription expects.
///   </description></item>
///   <item><description>
///     <c>driver_data_size</c> — <c>sizeof(r3e_driverdata)</c> as the running game reports it,
///     compared against <see cref="R3EDriverData"/>. This is what catches a growth <i>inside</i>
///     the trailing driver array, which <c>all_drivers_offset</c> cannot see.
///   </description></item>
/// </list>
/// <para>
/// Any <see cref="RequiredMajor"/> block whose self-description matches is accepted whatever its
/// minor version says — that is how several RaceRoom versions are supported from one transcription.
/// Any block that disagrees is refused with the two numbers in the message, so a layout change
/// shows up as a named connect failure rather than as silently shifted telemetry. The major version
/// is still checked outright: upstream reserves majors for exactly the incompatible reshuffle this
/// connector cannot absorb.
/// </para>
/// <para>
/// <see cref="MinimumMinor"/> survives only as the fallback for a block that does not self-describe
/// (either field zero or negative). No shipped major-3 build is known to do that; the fallback
/// exists so an unexpected one degrades to the old, weaker version-number rule rather than being
/// refused outright.
/// </para>
/// </remarks>
internal static class R3EVersionGate
{
    /// <summary>The only major version this connector's struct layout is known to match (<c>R3E_VERSION_MAJOR</c>).</summary>
    internal const int RequiredMajor = 3;

    /// <summary>
    /// The minor version this connector's structs were transcribed from (<c>R3E_VERSION_MINOR</c>),
    /// used only as the fallback floor for a block that does not report its own layout. A block
    /// that does self-describe is judged on that description instead, so both older and newer
    /// minors are accepted when they are genuinely layout-compatible.
    /// </summary>
    internal const int MinimumMinor = 5;

    /// <summary>Byte offset of <c>version_major</c>, <c>version_minor</c>, <c>all_drivers_offset</c> and <c>driver_data_size</c>.</summary>
    private const int VersionMajorOffset = 0;
    private const int VersionMinorOffset = 4;
    private const int AllDriversOffsetOffset = 8;
    private const int DriverDataSizeOffset = 12;

    /// <summary>Just <c>version_major</c> + <c>version_minor</c> — enough to report a version even when nothing else is readable.</summary>
    private const int VersionHeaderSize = sizeof(int) * 2;

    /// <summary>
    /// What a running game must report as <c>all_drivers_offset</c> for its layout to match this
    /// connector's: the offset of <see cref="R3ESharedRaw.NumCars"/>, the last field mapped before
    /// the deliberately omitted trailing driver array.
    /// </summary>
    internal static int ExpectedAllDriversOffset => Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.NumCars)).ToInt32();

    /// <summary>What a running game must report as <c>driver_data_size</c>: <c>sizeof</c> this connector's <see cref="R3EDriverData"/>.</summary>
    internal static int ExpectedDriverDataSize => Unsafe.SizeOf<R3EDriverData>();

    /// <summary>
    /// Checks that <paramref name="view"/> is large enough to hold a version header, that
    /// <c>version_major</c> equals exactly <see cref="RequiredMajor"/>, that the view is at least as
    /// large as <see cref="R3ESharedRaw"/>, and that the block's own layout self-description matches
    /// this connector's structs — falling back to <see cref="MinimumMinor"/> only for a block that
    /// does not self-describe.
    /// </summary>
    /// <param name="view">The shared-memory view to validate.</param>
    /// <param name="major">The <c>version_major</c> value read, or 0 if <paramref name="view"/> was too small to contain it.</param>
    /// <param name="minor">The <c>version_minor</c> value read, or 0 if <paramref name="view"/> was too small to contain it.</param>
    /// <param name="failureReason">A human-readable explanation when this method returns <see langword="false"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the view is safe to read as <see cref="R3ESharedRaw"/>.</returns>
    public static bool TryValidate(ISharedMemoryView view, out int major, out int minor, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (view.Length < VersionHeaderSize)
        {
            major = 0;
            minor = 0;
            failureReason = $"shared memory view is only {view.Length} byte(s), too small to contain a version header ({VersionHeaderSize} bytes).";
            return false;
        }

        major = view.Read<int>(VersionMajorOffset);
        minor = view.Read<int>(VersionMinorOffset);

        if (major != RequiredMajor)
        {
            failureReason = $"unsupported RaceRoom shared-memory major version {major} (this connector's struct layout matches major {RequiredMajor} only).";
            return false;
        }

        int requiredSize = Unsafe.SizeOf<R3ESharedRaw>();
        if (view.Length < requiredSize)
        {
            failureReason = $"shared memory view is {view.Length} byte(s), smaller than the expected struct size ({requiredSize} bytes).";
            return false;
        }

        return TryValidateLayout(view, minor, out failureReason);
    }

    /// <summary>
    /// Compares the block's self-reported layout against this connector's compiled structs, or —
    /// for a block that reports neither — falls back to the <see cref="MinimumMinor"/> floor.
    /// </summary>
    private static bool TryValidateLayout(ISharedMemoryView view, int minor, out string? failureReason)
    {
        // Guaranteed readable: the caller already established the view is at least sizeof(R3ESharedRaw),
        // which is far larger than this 16-byte header.
        int reportedAllDriversOffset = view.Read<int>(AllDriversOffsetOffset);
        int reportedDriverDataSize = view.Read<int>(DriverDataSizeOffset);

        if (reportedAllDriversOffset <= 0 || reportedDriverDataSize <= 0)
        {
            // The block does not describe its own layout, so there is nothing to compare against and
            // the version number is all that is left to go on.
            if (minor < MinimumMinor)
            {
                failureReason =
                    $"RaceRoom shared-memory minor version {minor} is older than the minimum this connector supports " +
                    $"({MinimumMinor}), and the block does not report its own layout " +
                    $"(all_drivers_offset={reportedAllDriversOffset}, driver_data_size={reportedDriverDataSize}) for a structural check instead.";
                return false;
            }

            failureReason = null;
            return true;
        }

        if (reportedAllDriversOffset != ExpectedAllDriversOffset)
        {
            failureReason =
                $"RaceRoom {RequiredMajor}.{minor} reports its driver data at byte offset {reportedAllDriversOffset}, but this " +
                $"connector's transcription puts it at {ExpectedAllDriversOffset} — the shared-memory layout has changed ahead of " +
                "that offset, so every field this connector reads would be misinterpreted. The Interop structs need re-syncing " +
                "against r3e-api's sample-csharp/src/R3E.cs.";
            return false;
        }

        if (reportedDriverDataSize != ExpectedDriverDataSize)
        {
            failureReason =
                $"RaceRoom {RequiredMajor}.{minor} reports a driver-data struct of {reportedDriverDataSize} bytes, but this " +
                $"connector's transcription is {ExpectedDriverDataSize} bytes — the shared-memory layout has changed. The Interop " +
                "structs need re-syncing against r3e-api's sample-csharp/src/R3E.cs.";
            return false;
        }

        failureReason = null;
        return true;
    }
}
