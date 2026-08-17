using System.Text.Json;
using RaceIntelligence.Live.Contracts.View;
using RaceIntelligence.Web.Live;
using RaceIntelligence.Web.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Live;

/// <summary>
/// Pins the JSON the dashboard parses, field by field.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard's TypeScript types in <c>ClientApp/src/live/contracts.ts</c> are hand-written, and
/// nothing in either toolchain connects them to these records. A rename on this side would compile,
/// pass every other test, and reach a race engineer as a column that had quietly gone blank —
/// JavaScript reads a missing property as <c>undefined</c> rather than failing.
/// </para>
/// <para>
/// So this is the seam that fails instead. Each name below appears verbatim in that file; changing
/// one here without changing it there breaks this test, which is the cheapest possible way to
/// discover it.
/// </para>
/// </remarks>
public sealed class LiveViewContractShapeTests
{
    private static JsonElement Serialize(LiveViewMessage message) =>
        JsonSerializer.SerializeToElement(message, LiveViewJson.Default);

    private static IEnumerable<string> PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name);

    [Fact]
    public void The_room_list_carries_the_names_the_dashboard_reads()
    {
        var message = new RoomListMessage([
            new LiveRoomSummary(
                "room-1", "raceroom", "Spa", "Grand Prix", 3, 20,
                [
                    new LivePublisherSummary(
                        Guid.Empty, "Gaming PC", "Mark", "4242",
                        DateTimeOffset.UnixEpoch, ["TyreWear"]),
                ],
                DateTimeOffset.UnixEpoch),
        ]);

        var json = Serialize(message);
        json.GetProperty("type").GetString().ShouldBe("roomList");

        PropertyNames(json.GetProperty("rooms")[0]).ShouldBe(
            [
                "roomId", "gameKey", "trackName", "layoutName", "sessionType",
                "driverCount", "publishers", "lastUpdatedAtUtc",
            ],
            ignoreOrder: true);

        PropertyNames(json.GetProperty("rooms")[0].GetProperty("publishers")[0]).ShouldBe(
            ["clientId", "clientName", "driverName", "simDriverId", "connectedAtUtc", "capabilities"],
            ignoreOrder: true);
    }

    /// <summary>
    /// Capabilities cross as names rather than a bitmask. A <see cref="ulong"/> above 2^53 cannot
    /// be represented exactly by a JavaScript number, and the enum is deliberately sized for 64
    /// flags — so a mask would eventually arrive subtly wrong rather than obviously broken.
    /// </summary>
    [Fact]
    public void Capabilities_cross_as_names_rather_than_a_bitmask()
    {
        var message = new RoomListMessage([
            new LiveRoomSummary(
                "room-1", "raceroom", "Spa", "Grand Prix", 3, 20,
                [
                    new LivePublisherSummary(
                        Guid.Empty, "Gaming PC", "Mark", "4242", DateTimeOffset.UnixEpoch,
                        ["TyreWear", "OpponentStandings"]),
                ],
                DateTimeOffset.UnixEpoch),
        ]);

        var capabilities = Serialize(message)
            .GetProperty("rooms")[0].GetProperty("publishers")[0].GetProperty("capabilities");

        capabilities.ValueKind.ShouldBe(JsonValueKind.Array);
        capabilities.EnumerateArray().Select(value => value.GetString())
            .ShouldBe(["TyreWear", "OpponentStandings"]);
    }

    [Fact]
    public void A_tower_row_carries_the_names_the_dashboard_reads()
    {
        var message = new TowerSnapshotMessage(
            "room-1",
            DateTimeOffset.UnixEpoch,
            LiveTowerProjector.Project(
                LiveDtoFactory.Standings(LiveDtoFactory.Standing(
                    simDriverId: "1", position: 1, bestLap: TimeSpan.FromSeconds(102))),
                new HashSet<string>()));

        var json = Serialize(message);
        json.GetProperty("type").GetString().ShouldBe("towerSnapshot");

        var row = json.GetProperty("drivers")[0];

        // Only the always-present names. Nulls are omitted by design, so an optional field is
        // absent here and must be optional in TypeScript too.
        PropertyNames(row).ShouldContain("driverKey");
        PropertyNames(row).ShouldContain("displayName");
        PropertyNames(row).ShouldContain("tier");
        PropertyNames(row).ShouldContain("bestLapMs");
        PropertyNames(row).ShouldContain("currentSectorMs");
        PropertyNames(row).ShouldContain("pitStopStatus");
        PropertyNames(row).ShouldContain("finishStatus");
    }

    /// <summary>
    /// Durations cross as milliseconds, not as <see cref="TimeSpan"/> strings. The dashboard does
    /// arithmetic on these — session-best comparisons, sector subtraction — and a string would
    /// concatenate rather than fail.
    /// </summary>
    [Fact]
    public void Durations_cross_as_numeric_milliseconds()
    {
        var message = new TowerSnapshotMessage(
            "room-1",
            DateTimeOffset.UnixEpoch,
            LiveTowerProjector.Project(
                LiveDtoFactory.Standings(LiveDtoFactory.Standing(
                    simDriverId: "1", position: 1, bestLap: TimeSpan.FromSeconds(102.5))),
                new HashSet<string>()));

        var bestLap = Serialize(message).GetProperty("drivers")[0].GetProperty("bestLapMs");

        bestLap.ValueKind.ShouldBe(JsonValueKind.Number);
        bestLap.GetDouble().ShouldBe(102_500);
    }

    /// <summary>
    /// The omission the dashboard's optional types depend on: an unreported value is an absent
    /// property, never a zero. A tower that rendered "unavailable" as a confident 0.0s gap would be
    /// actively misleading to the person making a pit call from it.
    /// </summary>
    [Fact]
    public void An_unreported_value_is_omitted_rather_than_written_as_zero()
    {
        var message = new TowerSnapshotMessage(
            "room-1",
            DateTimeOffset.UnixEpoch,
            LiveTowerProjector.Project(
                LiveDtoFactory.Standings(LiveDtoFactory.Standing(simDriverId: "1", position: 1)),
                new HashSet<string>()));

        var names = PropertyNames(Serialize(message).GetProperty("drivers")[0]).ToList();

        names.ShouldNotContain("bestLapMs");
        names.ShouldNotContain("gapToCarAheadMs");
        names.ShouldNotContain("completedLaps");
    }

    [Fact]
    public void The_tier_crosses_as_a_string_the_dashboard_can_switch_on()
    {
        var message = new TowerSnapshotMessage(
            "room-1",
            DateTimeOffset.UnixEpoch,
            LiveTowerProjector.Project(
                LiveDtoFactory.Standings(LiveDtoFactory.Standing(simDriverId: "1", position: 1)),
                new HashSet<string>(["id:1"])));

        Serialize(message).GetProperty("drivers")[0].GetProperty("tier").GetString().ShouldBe("Self");
    }

    [Fact]
    public void A_focus_frame_carries_the_names_the_dashboard_reads()
    {
        var message = new FocusFrameMessage(
            "room-1", "id:1", DateTimeOffset.UnixEpoch, 1.0, 2, 1, 0.25f, 55f,
            1f, 0f, 0.5f, 0.1f, 4, 7200f, 40f, [180f, 181f, 175f, 176f], [0.1f, 0.1f, 0.1f, 0.1f],
            [85f, 86f, 82f, 83f]);

        var json = Serialize(message);
        json.GetProperty("type").GetString().ShouldBe("focusFrame");

        PropertyNames(json).ShouldBe(
            [
                "type", "roomId", "driverKey", "capturedAtUtc", "simulationTime", "lapNumber",
                "sector", "trackPositionFraction", "speedMetersPerSecond", "throttle", "brake",
                "clutch", "steering", "gear", "engineRpm", "fuelLeftLiters", "tyrePressureKpa",
                "tyreWear", "tyreTemperatureCelsius",
            ],
            ignoreOrder: true);

        // FL, FR, RL, RR — the platform's wheel order, which the dashboard indexes positionally.
        json.GetProperty("tyrePressureKpa").GetArrayLength().ShouldBe(4);
    }

    /// <summary>
    /// The payload is a string, not an object. The hub does not parse it and neither does this
    /// contract — a simulator exposing a field nobody anticipated costs a connector and a dashboard
    /// panel, not a change here.
    /// </summary>
    [Fact]
    public void An_extras_frame_carries_the_names_the_dashboard_reads()
    {
        var message = new ExtrasFrameMessage(
            "room-1",
            "id:1",
            DateTimeOffset.UnixEpoch,
            """{"damage":{"engine":0.5,"transmission":-1.0}}""");

        var json = Serialize(message);
        json.GetProperty("type").GetString().ShouldBe("extrasFrame");

        PropertyNames(json).ShouldBe(
            ["type", "roomId", "driverKey", "capturedAtUtc", "extras"],
            ignoreOrder: true);

        // Verbatim, sentinel and all. A dashboard that reads -1 as zero damage says the car is fine
        // when the truth is that nobody knows.
        json.GetProperty("extras").GetString()
            .ShouldBe("""{"damage":{"engine":0.5,"transmission":-1.0}}""");
    }

    [Fact]
    public void A_lap_history_carries_the_names_the_dashboard_reads()
    {
        var message = new LapHistoryMessage(
            "room-1",
            "id:1",
            [new LapRecord(7, 104_500, [30_000, 70_000, 104_500], Valid: false)],
            Truncated: true);

        var json = Serialize(message);
        json.GetProperty("type").GetString().ShouldBe("lapHistory");

        PropertyNames(json).ShouldBe(
            ["type", "roomId", "driverKey", "laps", "truncated"],
            ignoreOrder: true);

        PropertyNames(json.GetProperty("laps")[0]).ShouldBe(
            ["lapNumber", "lapTimeMs", "sectorMs", "valid"],
            ignoreOrder: true);

        // Milliseconds as a JSON number, never a TimeSpan string — the same rule as everywhere else
        // on this wire.
        json.GetProperty("laps")[0].GetProperty("lapTimeMs").GetDouble().ShouldBe(104_500);
    }

    /// <summary>
    /// A lap whose time the hub never saw must not arrive as <c>0</c>. Nulls are dropped from this
    /// wire, so the property is simply absent and the dashboard reads it as unknown — where a zero
    /// would render as an impossibly quick lap at the top of the sheet.
    /// </summary>
    [Fact]
    public void A_lap_with_no_recorded_time_omits_it_rather_than_sending_zero()
    {
        var message = new LapHistoryMessage(
            "room-1", "id:1", [new LapRecord(7, LapTimeMs: null, SectorMs: [], Valid: null)], Truncated: false);

        var names = PropertyNames(Serialize(message).GetProperty("laps")[0]).ToList();

        names.ShouldContain("lapNumber");
        names.ShouldNotContain("lapTimeMs");
        names.ShouldNotContain("valid");
    }

    [Fact]
    public void An_error_carries_a_code_the_dashboard_can_branch_on()
    {
        var json = Serialize(new LiveErrorMessage(
            LiveErrorCodes.NoTelemetryForDriver, "no telemetry"));

        json.GetProperty("type").GetString().ShouldBe("error");
        json.GetProperty("code").GetString().ShouldBe("noTelemetryForDriver");
        json.GetProperty("message").GetString().ShouldBe("no telemetry");
    }

    /// <summary>
    /// The commands the dashboard sends, read back as the hub reads them. A discriminator mismatch
    /// here is silent in both directions — the hub answers an unrecognised command with an error
    /// the dashboard would show as a mysterious failure to subscribe.
    /// </summary>
    [Theory]
    [InlineData("""{"type":"watchRoom","roomId":"abc"}""", typeof(WatchRoomCommand))]
    [InlineData("""{"type":"focusDriver","driverKey":"id:2"}""", typeof(FocusDriverCommand))]
    [InlineData("""{"type":"watchRoom","roomId":null}""", typeof(WatchRoomCommand))]
    public void The_commands_the_dashboard_sends_deserialize(string json, Type expected)
    {
        JsonSerializer.Deserialize<LiveViewCommand>(json, LiveViewJson.Default)
            .ShouldNotBeNull()
            .ShouldBeOfType(expected);
    }
}
