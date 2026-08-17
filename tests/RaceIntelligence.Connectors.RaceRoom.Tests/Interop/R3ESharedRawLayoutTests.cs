using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests.Interop;

/// <summary>
/// Golden layout tests for <see cref="R3ESharedRaw"/>. This is the only automated defense against
/// a silent field-order/type transcription error against r3e.h: every offset below was computed
/// by hand, field-by-field, directly from the header (API version 3.5,
/// <c>#pragma pack(push, 1)</c>, no compiler padding anywhere), independent of the struct's actual
/// compiled layout. If a field's type or position is wrong, the *actual* offset the CLR computes
/// for <see cref="R3ESharedRaw"/>'s [StructLayout(Pack = 1)] layout will diverge from these
/// hand-computed values and these tests will fail.
/// </summary>
public class R3ESharedRawLayoutTests
{
    /// <summary>Bytes RaceRoom actually publishes in the <c>$R3E</c> block (<c>sizeof(r3e_shared)</c>).</summary>
    private const int PublishedBlockSize = 43_996;

    /// <summary>
    /// Bytes of that block this connector maps. The header's trailing 128-entry driver array is
    /// deliberately omitted (see <see cref="R3ESharedRaw"/>), so this is smaller on purpose.
    /// </summary>
    private const int MappedPrefixSize = 2_012;

    [Fact]
    public void MappedPrefix_Size_MatchesHandComputedByteCount()
    {
        // Sum of every field this connector mirrors, computed by hand from r3e.h: R3EPlayerData
        // (560) + R3EFlags (56) + R3ECutTrackPenalties (20, x2) + R3EDriverInfo (128, x2) + every
        // scalar/array field in between, up to and including num_cars.
        Unsafe.SizeOf<R3ESharedRaw>().ShouldBe(MappedPrefixSize);
    }

    [Fact]
    public void MappedPrefix_PlusTheOmittedDriverArray_AccountsForTheWholePublishedBlock()
    {
        // The one assertion that keeps the omission honest: what we map plus what we deliberately
        // skip must still add up to the block RaceRoom publishes. If a field were accidentally
        // dropped from the middle of the struct, this fails even though the "prefix size" pin above
        // could be talked into agreeing with it.
        const int MaxDrivers = 128; // R3E_NUM_DRIVERS_MAX

        (MappedPrefixSize + (Unsafe.SizeOf<R3EDriverData>() * MaxDrivers)).ShouldBe(PublishedBlockSize);
    }

    [Fact]
    public void MappedPrefix_EndsExactlyWhereTheDriverArrayBegins()
    {
        // all_drivers_data_1 starts immediately after num_cars, so the struct must stop there --
        // proving the omitted array really is trailing and shifts nothing before it.
        int numCarsOffset = Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.NumCars)).ToInt32();

        (numCarsOffset + sizeof(int)).ShouldBe(Unsafe.SizeOf<R3ESharedRaw>());
    }

    [Fact]
    public void AMaximalGrid_EndsExactlyAtTheEndOfThePublishedBlock()
    {
        // What R3EDriverDataReader's bounds check has to be true for. Deliberately expressed
        // through the reader's own MaxDrivers rather than a local constant, so the two cannot
        // drift: if the reader ever raised its cap, this is what would notice that the block has
        // no room for the extra entries.
        long lastEntryEnd =
            R3EVersionGate.ExpectedAllDriversOffset
            + sizeof(int)
            + ((long)R3EDriverDataReader.MaxDrivers * Unsafe.SizeOf<R3EDriverData>());

        lastEntryEnd.ShouldBe(PublishedBlockSize);
    }

    [Fact]
    public void VersionMajor_IsFirstField()
    {
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.VersionMajor)).ToInt32().ShouldBe(0);
    }

    [Fact]
    public void VersionMinor_FollowsVersionMajor()
    {
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.VersionMinor)).ToInt32().ShouldBe(4);
    }

    [Fact]
    public void AllDriversOffset_FollowsVersionMinor()
    {
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.AllDriversOffset)).ToInt32().ShouldBe(8);
    }

    [Fact]
    public void DriverDataSize_FollowsAllDriversOffset()
    {
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.DriverDataSize)).ToInt32().ShouldBe(12);
    }

    [Fact]
    public void Player_StartsAfterTheSixGameStateInt32s()
    {
        // 16 (version block) + 6 * 4 (game_mode..game_player_in_garage) = 40.
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.Player)).ToInt32().ShouldBe(40);
    }

    [Fact]
    public void TrackName_FollowsThe560ByteRaceRoomPlayerDataBlock()
    {
        // 40 (player start) + 560 (sizeof r3e_playerdata) = 600.
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.TrackName)).ToInt32().ShouldBe(600);
    }

    [Fact]
    public void LayoutName_Follows64ByteTrackName()
    {
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.LayoutName)).ToInt32().ShouldBe(664);
    }

    [Fact]
    public void TireWear_StartsTheFourthTyreFloatArray()
    {
        // 1632 (tire_type + start of tire arrays) + 16 (tire_rps) + 16 (tire_speed) + 16 (tire_grip) = 1680.
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.TireWear)).ToInt32().ShouldBe(1680);
    }

    [Fact]
    public void TirePressure_FollowsTireFlatspot()
    {
        // 1680 (tire_wear) + 16 (tire_wear) + 16 (tire_flatspot, int32 x4) = 1712.
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.TirePressure)).ToInt32().ShouldBe(1712);
    }

    [Fact]
    public void TireTemp_FollowsTireDirt()
    {
        // 1712 (tire_pressure) + 16 (tire_pressure) + 16 (tire_dirt) = 1744.
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.TireTemp)).ToInt32().ShouldBe(1744);
    }

    [Fact]
    public void NumCars_ImmediatelyPrecedesTheDriverArray()
    {
        Marshal.OffsetOf<R3ESharedRaw>(nameof(R3ESharedRaw.NumCars)).ToInt32().ShouldBe(2008);
    }

    [Fact]
    public void DriverData_Size_MatchesHandComputedByteCount()
    {
        // driver_info (128) + 26 scalar int32/float32 fields (104) + position/orientation vec3_f32
        // (12 x2) + 3 x Float3 sector-time triples (12 x3) + cut_track_penalties (20) = 328.
        Unsafe.SizeOf<R3EDriverData>().ShouldBe(328);
    }

    [Fact]
    public void PlayerData_Size_MatchesHandComputedByteCount()
    {
        Unsafe.SizeOf<R3EPlayerData>().ShouldBe(560);
    }

    [Fact]
    public void DriverInfo_Size_MatchesHandComputedByteCount()
    {
        Unsafe.SizeOf<R3EDriverInfo>().ShouldBe(128);
    }

    [Fact]
    public void EverySingleFieldIsPacked_NoStructPaddingAnywhere()
    {
        // Pack = 1 throughout means the struct's own alignment requirement collapses to 1 byte,
        // so its size should be exactly the byte sum with zero end-of-struct padding.
        StructLayoutAttribute? attribute = typeof(R3ESharedRaw).StructLayoutAttribute;
        attribute.ShouldNotBeNull();
        attribute.Value.ShouldBe(LayoutKind.Sequential);
        attribute.Pack.ShouldBe(1);
    }
}
