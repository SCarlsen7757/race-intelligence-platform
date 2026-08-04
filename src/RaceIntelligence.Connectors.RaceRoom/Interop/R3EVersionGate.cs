using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>
/// Validates a raw shared-memory view's version header before anything else in it is trusted.
/// </summary>
/// <remarks>
/// The official r3e-api samples read the shared memory unconditionally, with no version check at
/// all. That is not safe for a connector whose only defense against a silent layout change is a
/// struct transcribed by hand from the header: a future major version could reorder or resize
/// fields, and reading it as <see cref="R3ESharedRaw"/> would then produce plausible-looking but
/// wrong numbers instead of an obvious failure. This connector always checks
/// <c>version_major</c>/<c>version_minor</c> — the first 8 bytes of the block — before reading
/// anything else.
/// </remarks>
internal static class R3EVersionGate
{
    /// <summary>The only major version this connector's struct layout is known to match (<c>R3E_VERSION_MAJOR</c>).</summary>
    internal const int RequiredMajor = 3;

    /// <summary>The minimum minor version this connector was written against (<c>R3E_VERSION_MINOR</c>). Newer minors are accepted — trailing fields they add are simply ignored.</summary>
    internal const int MinimumMinor = 5;

    /// <summary>
    /// Checks that <paramref name="view"/> is large enough to hold a version header, that
    /// <c>version_major</c> equals exactly <see cref="RequiredMajor"/>, that <c>version_minor</c>
    /// is at least <see cref="MinimumMinor"/>, and that the view is at least as large as
    /// <see cref="R3ESharedRaw"/>.
    /// </summary>
    /// <param name="view">The shared-memory view to validate.</param>
    /// <param name="major">The <c>version_major</c> value read, or 0 if <paramref name="view"/> was too small to contain it.</param>
    /// <param name="minor">The <c>version_minor</c> value read, or 0 if <paramref name="view"/> was too small to contain it.</param>
    /// <param name="failureReason">A human-readable explanation when this method returns <see langword="false"/>; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the view is safe to read as <see cref="R3ESharedRaw"/>.</returns>
    public static bool TryValidate(ISharedMemoryView view, out int major, out int minor, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(view);

        const int VersionHeaderSize = sizeof(int) * 2; // version_major + version_minor

        if (view.Length < VersionHeaderSize)
        {
            major = 0;
            minor = 0;
            failureReason = $"shared memory view is only {view.Length} byte(s), too small to contain a version header ({VersionHeaderSize} bytes).";
            return false;
        }

        major = view.Read<int>(0);
        minor = view.Read<int>(sizeof(int));

        if (major != RequiredMajor)
        {
            failureReason = $"unsupported RaceRoom shared-memory major version {major} (this connector's struct layout matches major {RequiredMajor} only).";
            return false;
        }

        if (minor < MinimumMinor)
        {
            failureReason = $"RaceRoom shared-memory minor version {minor} is older than the minimum this connector supports ({MinimumMinor}).";
            return false;
        }

        int requiredSize = Unsafe.SizeOf<R3ESharedRaw>();
        if (view.Length < requiredSize)
        {
            failureReason = $"shared memory view is {view.Length} byte(s), smaller than the expected struct size ({requiredSize} bytes).";
            return false;
        }

        failureReason = null;
        return true;
    }
}
