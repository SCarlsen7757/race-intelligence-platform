using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests.Interop;

/// <summary>
/// Exercises the real <see cref="MappedFileSharedMemoryView"/> against a stand-in <c>$R3E</c>
/// section this test creates itself. Everything else in the suite runs against the in-memory fake,
/// which means the one class that actually touches Windows shared memory — including how it reports
/// "RaceRoom is not running" and, critically, what <c>IsValid</c> does and does not promise — had no
/// coverage at all.
/// </summary>
/// <remarks>
/// All tests live in one class deliberately: they create and open the same well-known
/// <c>"$R3E"</c> name, and xUnit runs tests within a class sequentially, so they cannot collide with
/// each other. (They will collide with an actually-running RaceRoom, which is not a supported
/// configuration for running this suite.)
/// </remarks>
public class MappedFileSharedMemoryViewTests
{
    private static MemoryMappedFile CreatePublishedBlock(R3ESharedRaw contents)
    {
        byte[] bytes = contents.ToBytes();
        var file = MemoryMappedFile.CreateNew(MappedFileSharedMemoryView.SharedMemoryName, bytes.Length);
        try
        {
            using var accessor = file.CreateViewAccessor();
            accessor.WriteArray(0, bytes, 0, bytes.Length);
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    [Fact]
    public void Open_WhenNoSectionExists_ThrowsFileNotFound()
    {
        // The documented contract: RaceRoom not running is a FileNotFoundException, which
        // RaceRoomTelemetrySource catches specifically as "not connected yet" rather than fatal.
        Should.Throw<FileNotFoundException>(() => MappedFileSharedMemoryView.Open());
    }

    [Fact]
    public void Open_ReadsBackTheStructTheSectionContains()
    {
        var published = new R3ESharedRawBuilder()
            .InRaceSession("Mapped Track", "Mapped Layout")
            .WithTicks(4242)
            .WithSpeed(55.5f)
            .WithGear(3)
            .Build();

        using var file = CreatePublishedBlock(published);
        using var view = MappedFileSharedMemoryView.Open();

        var read = view.Read<R3ESharedRaw>();

        read.Player.GameSimulationTicks.ShouldBe(4242);
        read.CarSpeed.ShouldBe(55.5f);
        read.Gear.ShouldBe(3);
        R3ETelemetryMapper.DecodeUtf8Name(read.TrackName).ShouldBe("Mapped Track");
    }

    [Fact]
    public void Read_AtAnOffset_ReadsThatFieldOnly()
    {
        // The version gate reads the two header int32s this way before trusting anything else.
        var published = new R3ESharedRawBuilder().WithVersion(3, 9).Build();

        using var file = CreatePublishedBlock(published);
        using var view = MappedFileSharedMemoryView.Open();

        view.Read<int>(0).ShouldBe(3);
        view.Read<int>(sizeof(int)).ShouldBe(9);
    }

    [Fact]
    public void Length_IsAtLeastTheMappedStructSize()
    {
        using var file = CreatePublishedBlock(new R3ESharedRawBuilder().Build());
        using var view = MappedFileSharedMemoryView.Open();

        // The OS rounds a mapping up to a page boundary, so this is ">=", not "==" -- which is
        // exactly why callers compare Length against the struct size rather than for equality.
        view.Length.ShouldBeGreaterThanOrEqualTo(Unsafe.SizeOf<R3ESharedRaw>());
    }

    [Fact]
    public void IsValid_StaysTrueAfterTheWriterCloses_WhichIsWhyLivenessCannotRelyOnIt()
    {
        // This is the behaviour behind the "game exited but the collector never noticed" bug.
        // Windows keeps a section object alive while any handle to it is open, and this view holds
        // one -- so the mapping stays valid and keeps serving the last frame written even after the
        // publisher is gone. IsValid therefore only ever reports our own disposal, and detecting an
        // exited game has to come from the data (a frozen tick counter), not from here.
        var published = new R3ESharedRawBuilder().WithTicks(777).Build();

        var file = CreatePublishedBlock(published);
        using var view = MappedFileSharedMemoryView.Open();

        file.Dispose(); // the "game" exits.

        view.IsValid.ShouldBeTrue();
        view.Read<R3ESharedRaw>().Player.GameSimulationTicks.ShouldBe(777);
    }

    [Fact]
    public void IsValid_BecomesFalseOnceDisposed_AndReadingThrows()
    {
        using var file = CreatePublishedBlock(new R3ESharedRawBuilder().Build());
        var view = MappedFileSharedMemoryView.Open();

        view.IsValid.ShouldBeTrue();
        view.Dispose();

        view.IsValid.ShouldBeFalse();
        Should.Throw<ObjectDisposedException>(() => view.Read<R3ESharedRaw>());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        using var file = CreatePublishedBlock(new R3ESharedRawBuilder().Build());
        var view = MappedFileSharedMemoryView.Open();

        view.Dispose();
        Should.NotThrow(view.Dispose);
    }
}
