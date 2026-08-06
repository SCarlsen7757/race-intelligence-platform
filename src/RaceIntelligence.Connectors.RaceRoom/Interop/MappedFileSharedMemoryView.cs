using System.IO.MemoryMappedFiles;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>
/// Reads RaceRoom's real <c>$R3E</c> named shared memory block via
/// <see cref="MemoryMappedFile.OpenExisting(string, MemoryMappedFileRights)"/>.
/// </summary>
/// <remarks>
/// Reads go through <see cref="MemoryMappedViewAccessor.Read{T}(long, out T)"/>, which copies the
/// mapped bytes into a struct of the target type without interpreting them field by field. It is a
/// copy, not a reinterpretation in place — but a cheap one, and far cheaper at a 60 Hz poll than
/// the reflection-based
/// <see cref="System.Runtime.InteropServices.Marshal.PtrToStructure(nint, Type)"/> alternative. It
/// also keeps this class free of pointers, so the assembly needs no <c>AllowUnsafeBlocks</c> — the
/// only unsafe code involved is inside the BCL, where it is already vetted.
/// </remarks>
internal sealed class MappedFileSharedMemoryView : ISharedMemoryView
{
    /// <summary>The shared memory object name RaceRoom publishes (<c>R3E_SHARED_MEMORY_NAME</c> in r3e.h).</summary>
    internal const string SharedMemoryName = "$R3E";

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _accessor;
    private bool _disposed;

    private MappedFileSharedMemoryView(MemoryMappedFile file, MemoryMappedViewAccessor accessor)
    {
        _file = file;
        _accessor = accessor;
    }

    /// <summary>
    /// Opens the real RaceRoom shared memory block. Throws
    /// <see cref="FileNotFoundException"/> if RaceRoom is not currently running (the section does
    /// not exist) — callers should treat that specific exception as "not connected yet", not as a
    /// fatal error.
    /// </summary>
    public static MappedFileSharedMemoryView Open()
    {
        var file = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.Read);
        try
        {
            var accessor = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            try
            {
                return new MappedFileSharedMemoryView(file, accessor);
            }
            catch
            {
                accessor.Dispose();
                throw;
            }
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>
    /// <see langword="true"/> until this instance is disposed — and <b>only</b> that. Windows keeps
    /// a section object alive for as long as any handle to it is open, and this class holds one, so
    /// the mapping (and the last frame RaceRoom wrote into it) remains readable after the game
    /// exits. There is no cheap, reliable signal here that says otherwise; game liveness is decided
    /// by <see cref="RaceIntelligence.Connectors.RaceRoom.RaceRoomTelemetrySource"/> watching the
    /// simulation tick counter instead.
    /// </summary>
    public bool IsValid => !_disposed;

    /// <inheritdoc />
    public long Length => _accessor.Capacity;

    /// <inheritdoc />
    public T Read<T>(long position = 0)
        where T : struct
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _accessor.Read(position, out T value);
        return value;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _accessor.Dispose();
        _file.Dispose();
    }
}
