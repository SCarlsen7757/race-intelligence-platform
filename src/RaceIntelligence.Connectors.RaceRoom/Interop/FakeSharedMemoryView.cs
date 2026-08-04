using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>
/// An in-memory <see cref="ISharedMemoryView"/> used by tests to drive
/// <see cref="RaceIntelligence.Connectors.RaceRoom.RaceRoomTelemetrySource"/> through a scripted
/// sequence of raw frames without RaceRoom (or any shared memory) being present.
/// </summary>
/// <remarks>
/// Mirrors how the real shared memory behaves: it is a single mutable buffer that the "game"
/// (the test) overwrites in place between polls, rather than a queue the source consumes from.
/// Call <see cref="SetFrame(byte[])"/> between reads to advance the scenario, and
/// <see cref="Invalidate"/> to simulate the game process disappearing.
/// </remarks>
internal sealed class FakeSharedMemoryView : ISharedMemoryView
{
    private byte[] _frame;
    private bool _valid = true;

    public FakeSharedMemoryView(byte[] initialFrame)
    {
        _frame = initialFrame;
    }

    /// <summary>Replaces the bytes served by the next call to <see cref="Read{T}"/>.</summary>
    public void SetFrame(byte[] frame) => _frame = frame;

    /// <summary>Simulates the shared memory source becoming unavailable (e.g. the game exited).</summary>
    public void Invalidate() => _valid = false;

    /// <inheritdoc />
    public bool IsValid => _valid;

    /// <inheritdoc />
    public long Length => _frame.Length;

    /// <inheritdoc />
    public T Read<T>(long position = 0)
        where T : struct
    {
        var slice = _frame.AsSpan(checked((int)position));
        if (slice.Length < Unsafe.SizeOf<T>())
        {
            throw new ArgumentException(
                $"Frame is {_frame.Length} byte(s); too small to read {typeof(T).Name} ({Unsafe.SizeOf<T>()} bytes) at offset {position}.",
                nameof(position));
        }

        return MemoryMarshal.Read<T>(slice);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No unmanaged resources to release.
    }
}
