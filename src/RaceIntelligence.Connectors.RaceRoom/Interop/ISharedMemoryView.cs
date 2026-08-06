namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>
/// A source of raw bytes shaped like RaceRoom's <c>$R3E</c> shared memory block. This seam is
/// what lets <see cref="RaceIntelligence.Connectors.RaceRoom.RaceRoomTelemetrySource"/> be
/// exercised in tests without RaceRoom installed: production code uses
/// <see cref="MappedFileSharedMemoryView"/>, tests use <see cref="FakeSharedMemoryView"/>.
/// </summary>
/// <remarks>
/// The view deliberately exposes a typed <see cref="Read{T}"/> rather than a
/// <see cref="ReadOnlySpan{T}"/> over its bytes. Producing a span over a memory-mapped region
/// requires acquiring an unmanaged pointer, which would force this assembly to enable unsafe
/// code. Going through a typed read keeps every implementation fully verifiable while still
/// avoiding an intermediate copy of the ~44 KB block on each poll.
/// </remarks>
internal interface ISharedMemoryView : IDisposable
{
    /// <summary>
    /// <see langword="false"/> once this view can no longer be read at all — it has been disposed,
    /// or the implementation has some positive signal that the mapping is gone. A telemetry source
    /// should treat that the same way it treats a read throwing: tear the connection down and go
    /// back to <c>Disconnected</c>.
    /// </summary>
    /// <remarks>
    /// <b>This is not a liveness check for the game.</b> A Windows section object stays mapped and
    /// fully readable for as long as any handle to it is open, so
    /// <see cref="MappedFileSharedMemoryView"/> keeps returning <see langword="true"/> — and keeps
    /// serving the last frame the game wrote — long after RaceRoom exits. Detecting that the game
    /// is gone is the *reader's* job, and
    /// <see cref="RaceIntelligence.Connectors.RaceRoom.RaceRoomTelemetrySource"/> does it by
    /// watching the simulation tick counter stop advancing (see its
    /// <c>RaceRoomConnectorOptions.StaleFrameTimeout</c>).
    /// </remarks>
    bool IsValid { get; }

    /// <summary>The size of the view in bytes. Used to reject a block too small to hold the struct being read.</summary>
    long Length { get; }

    /// <summary>
    /// Reinterprets the bytes at <paramref name="position"/> as <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">An unmanaged struct matching the shared-memory layout.</typeparam>
    /// <param name="position">Byte offset into the block.</param>
    /// <exception cref="ArgumentException">The view is too small to contain a <typeparamref name="T"/> at that offset.</exception>
    T Read<T>(long position = 0)
        where T : struct;
}
