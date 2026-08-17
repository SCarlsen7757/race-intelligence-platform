using System.Text.Json;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;
using RaceIntelligence.Core.Sessions;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Verifies <see cref="R3ETelemetryMapper.ToSessionInfo"/>'s driver identity and rate-setting
/// fields. <see cref="SessionInfo.SimDriverId"/> must be a real account id or nothing at all —
/// RaceRoom reports <c>0</c>/<c>-1</c> for offline/unauthenticated sessions, and letting <c>0</c>
/// through would merge every offline session of every driver into one identity. The fuel/tyre
/// rate codes go the other way: like <see cref="SessionInfo.SessionType"/> they are carried through
/// raw (<c>-1</c> = N/A, <c>0</c> = off, <c>1</c>-<c>4</c> = 1x-4x), because normalizing a
/// sim-specific code belongs to the analysis layer, not the collector.
/// </summary>
public class R3ETelemetryMapperSessionIdentityTests
{
    private static readonly GameVersionIdentity TestGameVersion = new()
    {
        Game = WellKnownGames.RaceRoom,
        GameVersion = "0.0.0.0",
        ApiVersionMajor = R3EVersionGate.RequiredMajor,
        ApiVersionMinor = R3EVersionGate.MinimumMinor,
        ConnectorVersion = "1.0.0",
    };

    private static SessionInfo MapSessionInfo(Action<R3ESharedRawBuilder> configure)
    {
        var builder = new R3ESharedRawBuilder().InRaceSession("Identity Test Track", "Identity Test Layout");
        configure(builder);
        var raw = builder.Build();
        return R3ETelemetryMapper.ToSessionInfo(
            in raw,
            Guid.NewGuid(),
            TestGameVersion,
            SimCapabilities.TyreWear | SimCapabilities.FuelFlow,
            DateTimeOffset.UtcNow);
    }

    private static SessionInfo MapSessionInfoWithUserIds(int vehicleUserId, int playerUserId) =>
        MapSessionInfo(b => b.Configure((ref R3ESharedRaw raw) =>
        {
            raw.VehicleInfo.UserId = vehicleUserId;
            raw.Player.UserId = playerUserId;
        }));

    // --- SimDriverId ---

    [Fact]
    public void SimDriverId_PositiveVehicleUserId_MapsToItsStringForm()
    {
        var session = MapSessionInfoWithUserIds(vehicleUserId: 1234567, playerUserId: 7654321);

        session.SimDriverId.ShouldBe("1234567");
    }

    [Fact]
    public void SimDriverId_VehicleUserIdZero_FallsBackToPlayerUserId()
    {
        var session = MapSessionInfoWithUserIds(vehicleUserId: 0, playerUserId: 4242);

        session.SimDriverId.ShouldBe("4242");
    }

    [Fact]
    public void SimDriverId_VehicleUserIdNegativeSentinel_FallsBackToPlayerUserId()
    {
        var session = MapSessionInfoWithUserIds(vehicleUserId: -1, playerUserId: 4242);

        session.SimDriverId.ShouldBe("4242");
    }

    [Fact]
    public void SimDriverId_BothUserIdsZero_MapsToNull_NotTheStringZero()
    {
        var session = MapSessionInfoWithUserIds(vehicleUserId: 0, playerUserId: 0);

        // 0 is RaceRoom's "no account" marker, not an account id. Mapping it to "0" would make
        // every offline session from every driver share a single identity.
        session.SimDriverId.ShouldBeNull();
    }

    [Fact]
    public void SimDriverId_BothUserIdsNegativeSentinel_MapsToNull()
    {
        var session = MapSessionInfoWithUserIds(vehicleUserId: -1, playerUserId: -1);

        session.SimDriverId.ShouldBeNull();
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(-1, 0)]
    public void SimDriverId_MixedNonPositiveUserIds_MapsToNull(int vehicleUserId, int playerUserId)
    {
        var session = MapSessionInfoWithUserIds(vehicleUserId, playerUserId);

        session.SimDriverId.ShouldBeNull();
    }

    [Fact]
    public void SimDriverId_IsIndependentOfPlayerName()
    {
        var session = MapSessionInfo(b => b
            .WithPlayerName("Renamed Yesterday")
            .Configure((ref R3ESharedRaw raw) => raw.VehicleInfo.UserId = 987654));

        session.PlayerName.ShouldBe("Renamed Yesterday");
        session.SimDriverId.ShouldBe("987654");
    }

    // --- SimSlotId: the fallback identity, and the only one an offline session has. ---

    /// <summary>
    /// Slot <c>0</c> is the first car in the field, not a missing value.
    /// </summary>
    /// <remarks>
    /// The opposite treatment to <see cref="SessionInfo.SimDriverId"/> directly above, and
    /// deliberately so: <c>0</c> is RaceRoom's filler for an account id it does not have, but a
    /// genuine slot for a car. Filtering it out the way user ids are filtered would leave pole
    /// position as the one grid slot whose driver gets no live telemetry.
    /// </remarks>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(23, 23)]
    public void SimSlotId_NonNegativeSlot_MapsToItself(int slotId, int? expected)
    {
        var session = MapSessionInfo(b => b.Configure((ref R3ESharedRaw raw) =>
            raw.VehicleInfo.SlotId = slotId));

        session.SimSlotId.ShouldBe(expected);
    }

    [Fact]
    public void SimSlotId_NotAvailable_MapsToNull()
    {
        var session = MapSessionInfo(b => b.Configure((ref R3ESharedRaw raw) =>
            raw.VehicleInfo.SlotId = -1));

        session.SimSlotId.ShouldBeNull();
    }

    /// <summary>
    /// The offline case, end to end through the mapper: no account id anywhere, but a slot.
    /// </summary>
    /// <remarks>
    /// This is what the live hub attaches the focus and extras channels to when RaceRoom reports no
    /// identity, which it does for every unauthenticated session. Without it the driver's own car
    /// reaches the dashboard with no pedal inputs, no tyre data and no damage.
    /// </remarks>
    [Fact]
    public void An_offline_session_has_no_identity_but_still_reports_its_slot()
    {
        var session = MapSessionInfo(b => b.Configure((ref R3ESharedRaw raw) =>
        {
            raw.VehicleInfo.UserId = -1;
            raw.Player.UserId = 0;
            raw.VehicleInfo.SlotId = 5;
        }));

        session.SimDriverId.ShouldBeNull();
        session.SimSlotId.ShouldBe(5);
    }

    // --- FuelUsageRate / TyreWearRate: raw pass-through, no sentinel filtering. ---

    [Theory]
    [InlineData(-1)] // N/A
    [InlineData(0)] // consumption off
    [InlineData(1)] // 1x
    [InlineData(4)] // 4x
    public void FuelUsageRate_PassesRawRateCodeThroughUnchanged(int rateCode)
    {
        var session = MapSessionInfo(b => b.Configure((ref R3ESharedRaw raw) => raw.FuelUseActive = rateCode));

        session.FuelUsageRate.ShouldBe(rateCode);
    }

    [Theory]
    [InlineData(-1)] // N/A
    [InlineData(0)] // wear off
    [InlineData(1)] // 1x
    [InlineData(4)] // 4x
    public void TyreWearRate_PassesRawRateCodeThroughUnchanged(int rateCode)
    {
        var session = MapSessionInfo(b => b.Configure((ref R3ESharedRaw raw) => raw.TireWearActive = rateCode));

        session.TyreWearRate.ShouldBe(rateCode);
    }

    [Theory]
    [InlineData(3, 0)] // accelerated tyre wear with fuel consumption switched off entirely
    [InlineData(0, 4)] // no tyre wear, 4x fuel usage
    [InlineData(-1, 2)] // tyre wear N/A, 2x fuel usage
    [InlineData(4, 4)]
    public void FuelUsageRate_AndTyreWearRate_AreIndependentOfEachOther(int tyreWearRate, int fuelUsageRate)
    {
        var session = MapSessionInfo(b => b.Configure((ref R3ESharedRaw raw) =>
        {
            raw.TireWearActive = tyreWearRate;
            raw.FuelUseActive = fuelUsageRate;
        }));

        session.TyreWearRate.ShouldBe(tyreWearRate);
        session.FuelUsageRate.ShouldBe(fuelUsageRate);
    }

    // --- Extras: the raw values stay put; the typed fields are a projection, not a replacement. ---

    [Fact]
    public void Extras_StillCarryRawTyreWearAndFuelUseActive_AlongsideTheTypedFields()
    {
        var session = MapSessionInfo(b => b.Configure((ref R3ESharedRaw raw) =>
        {
            raw.TireWearActive = 3;
            raw.FuelUseActive = 0;
        }));

        session.Extras.GetProperty("tireWearActive").GetInt32().ShouldBe(3);
        session.Extras.GetProperty("fuelUseActive").GetInt32().ShouldBe(0);
        session.TyreWearRate.ShouldBe(3);
        session.FuelUsageRate.ShouldBe(0);
    }

    [Fact]
    public void Extras_Vehicle_CarriesUserIdAndSlotId()
    {
        var session = MapSessionInfo(b => b.Configure((ref R3ESharedRaw raw) =>
        {
            raw.VehicleInfo.UserId = 1234567;
            raw.VehicleInfo.SlotId = 17;
        }));

        JsonElement vehicle = session.Extras.GetProperty("vehicle");
        vehicle.GetProperty("userId").GetInt32().ShouldBe(1234567);
        vehicle.GetProperty("slotId").GetInt32().ShouldBe(17);
    }

    [Fact]
    public void Extras_Vehicle_CarriesRawNonPositiveUserId_EvenWhenSimDriverIdIsNull()
    {
        // The typed field is a projection; the raw block is permanent and keeps the -1 verbatim.
        var session = MapSessionInfoWithUserIds(vehicleUserId: -1, playerUserId: -1);

        session.SimDriverId.ShouldBeNull();
        session.Extras.GetProperty("vehicle").GetProperty("userId").GetInt32().ShouldBe(-1);
    }
}
