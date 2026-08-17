using System.Runtime.CompilerServices;
using RaceIntelligence.Connectors.RaceRoom;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Covers <see cref="R3EDriverDataReader"/>, which reads the trailing driver array the mapped
/// struct deliberately leaves out. Its offset arithmetic is the one place in the connector that
/// reaches into the block by hand rather than letting the runtime lay a struct out, so it is
/// tested against blocks built to the real layout rather than against itself.
/// </summary>
public sealed class R3EDriverDataReaderTests
{
    private static R3EDriverData Driver(string name, int userId, int place) =>
        new R3EDriverDataBuilder().WithName(name).WithUserId(userId).WithPlace(place, place).Build();

    [Fact]
    public void Reads_exactly_num_cars_entries_in_block_order()
    {
        var builder = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .WithTicks(100)
            .WithDrivers(Driver("Alice", 11, 1), Driver("Bob", 22, 2), Driver("Cara", 33, 3));

        var view = new FakeSharedMemoryView(builder.BuildBytes());
        var raw = builder.Build();

        R3EDriverDataReader.TryRead(view, in raw, out var drivers).ShouldBe(DriverArrayReadOutcome.Ok);

        drivers.Length.ShouldBe(3);
        drivers.Select(d => R3ETelemetryMapper.DecodeUtf8Name(d.DriverInfo.Name))
            .ShouldBe(["Alice", "Bob", "Cara"]);
        drivers.Select(d => d.DriverInfo.UserId).ShouldBe([11, 22, 33]);
    }

    /// <summary>
    /// The regression this reader is most likely to suffer: <c>all_drivers_offset</c> points at
    /// <c>num_cars</c>, not at the array, so reading at the reported offset directly shifts every
    /// entry four bytes and turns num_cars into the first bytes of the first driver's name.
    /// </summary>
    [Fact]
    public void Reads_from_after_num_cars_not_from_the_reported_offset()
    {
        var builder = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .WithTicks(100)
            .WithDrivers(Driver("Alice", 11, 1));

        var view = new FakeSharedMemoryView(builder.BuildBytes());
        var raw = builder.Build();

        R3EDriverDataReader.TryRead(view, in raw, out var drivers).ShouldBe(DriverArrayReadOutcome.Ok);

        // A four-byte shift would put num_cars (1) into the name buffer and leave the name decoding
        // as something other than the exact string written.
        R3ETelemetryMapper.DecodeUtf8Name(drivers[0].DriverInfo.Name).ShouldBe("Alice");
        drivers[0].DriverInfo.UserId.ShouldBe(11);
    }

    [Fact]
    public void Ignores_stale_entries_beyond_num_cars()
    {
        var builder = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .WithTicks(100)
            .WithDrivers(Driver("Alice", 11, 1), Driver("Ghost", 99, 2));

        // The block still contains both entries — the previous session left the second one behind —
        // but the header now reports a single car.
        builder.WithNumCars(1);

        var view = new FakeSharedMemoryView(builder.BuildBytes());
        var raw = builder.Build();

        R3EDriverDataReader.TryRead(view, in raw, out var drivers).ShouldBe(DriverArrayReadOutcome.Ok);

        drivers.Length.ShouldBe(1);
        R3ETelemetryMapper.DecodeUtf8Name(drivers[0].DriverInfo.Name).ShouldBe("Alice");
    }

    [Fact]
    public void No_cars_is_an_empty_read_not_a_failure()
    {
        var builder = new R3ESharedRawBuilder().InRaceSession("Spa", "Grand Prix").WithTicks(100);
        var view = new FakeSharedMemoryView(builder.BuildBytes());
        var raw = builder.Build();

        raw.NumCars.ShouldBe(0);
        R3EDriverDataReader.TryRead(view, in raw, out var drivers).ShouldBe(DriverArrayReadOutcome.Ok);
        drivers.ShouldBeEmpty();
    }

    [Fact]
    public void A_block_too_small_for_the_reported_field_fails()
    {
        var builder = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .WithTicks(100)
            .WithDrivers(Driver("Alice", 11, 1))
            .WithNumCars(20); // The header claims 20 cars; the block holds one.

        var view = new FakeSharedMemoryView(builder.BuildBytes());
        var raw = builder.Build();

        R3EDriverDataReader.TryRead(view, in raw, out _).ShouldBe(DriverArrayReadOutcome.Failed);
    }

    /// <summary>
    /// The block is deliberately sized to hold every entry the header claims, so the bounds check
    /// cannot be what rejects it. Only the <c>MaxDrivers</c> guard can — delete that guard and this
    /// test fails, which is the point.
    /// </summary>
    [Fact]
    public void A_field_larger_than_the_documented_maximum_fails()
    {
        int overMaximum = R3EDriverDataReader.MaxDrivers + 1;

        var builder = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .WithTicks(100)
            .WithDrivers([.. Enumerable.Range(0, overMaximum).Select(i => Driver($"D{i}", i + 1, i + 1))]);

        builder.Build().NumCars.ShouldBe(overMaximum);

        var view = new FakeSharedMemoryView(builder.BuildBytes());
        var raw = builder.Build();

        // The block really does contain all 129 entries.
        view.Length.ShouldBeGreaterThanOrEqualTo(
            R3EVersionGate.ExpectedAllDriversOffset + sizeof(int) + ((long)overMaximum * Unsafe.SizeOf<R3EDriverData>()));

        R3EDriverDataReader.TryRead(view, in raw, out _).ShouldBe(DriverArrayReadOutcome.Failed);
    }

    /// <summary>
    /// The per-frame offset is re-read from the block on every tick, so a torn or garbage header
    /// can carry anything. It is checked against the layout this connector compiles to, not merely
    /// for being positive — <c>int.MaxValue</c> in particular overflows to a negative offset if the
    /// widening cast is placed after the addition rather than before it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(1024)]
    public void An_offset_disagreeing_with_the_compiled_layout_fails(int reportedOffset)
    {
        var builder = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .WithTicks(100)
            .WithDrivers(Driver("Alice", 11, 1))
            .WithLayoutSelfDescription(reportedOffset, R3EVersionGate.ExpectedDriverDataSize);

        var view = new FakeSharedMemoryView(builder.BuildBytes());
        var raw = builder.Build();

        R3EDriverDataReader.TryRead(view, in raw, out _).ShouldBe(DriverArrayReadOutcome.Failed);
    }

    [Fact]
    public void A_frame_rewritten_mid_read_is_reported_as_torn()
    {
        var first = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .WithTicks(100)
            .WithDrivers(Driver("Alice", 11, 1), Driver("Bob", 22, 2));

        var second = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .WithTicks(101)
            .WithDrivers(Driver("Alice", 11, 1), Driver("Bob", 22, 2));

        var view = new FakeSharedMemoryView(first.BuildBytes());
        view.RewriteAfterNextRead(second.BuildBytes());
        var raw = first.Build();

        R3EDriverDataReader.TryRead(view, in raw, out _).ShouldBe(DriverArrayReadOutcome.Torn);
    }

    [Fact]
    public void A_disposed_view_fails_rather_than_throwing()
    {
        var builder = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .WithTicks(100)
            .WithDrivers(Driver("Alice", 11, 1));

        var raw = builder.Build();
        var view = new ThrowingSharedMemoryView(new ObjectDisposedException("view"));

        R3EDriverDataReader.TryRead(view, in raw, out _).ShouldBe(DriverArrayReadOutcome.Failed);
    }

    /// <summary>An <see cref="ISharedMemoryView"/> that claims to be big enough and then throws on read.</summary>
    private sealed class ThrowingSharedMemoryView(Exception toThrow) : ISharedMemoryView
    {
        public bool IsValid => true;

        public long Length => long.MaxValue;

        public T Read<T>(long position = 0)
            where T : struct => throw toThrow;

        public void Dispose()
        {
        }
    }
}
