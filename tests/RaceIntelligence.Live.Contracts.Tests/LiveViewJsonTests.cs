using System.Text.Json;
using RaceIntelligence.Live.Contracts.View;
using Shouldly;

namespace RaceIntelligence.Live.Contracts.Tests;

/// <summary>
/// Covers the browser-facing JSON contract. The consumer is TypeScript in another repository
/// directory with no compile-time link to these types, so the property names and the discriminator
/// are a real interface that nothing else in the build would catch a change to.
/// </summary>
public sealed class LiveViewJsonTests
{
    private static string Serialize(LiveViewMessage message) =>
        JsonSerializer.Serialize(message, LiveViewJson.Default);

    [Fact]
    public void Every_view_message_carries_its_discriminator()
    {
        Serialize(new RoomListMessage([])).ShouldContain("\"type\":\"roomList\"");
        Serialize(new TowerSnapshotMessage("room", DateTimeOffset.UnixEpoch, [])).ShouldContain("\"type\":\"towerSnapshot\"");
        Serialize(new LiveErrorMessage("unknownRoom", "no such room")).ShouldContain("\"type\":\"error\"");
    }

    [Fact]
    public void Properties_are_camelCase_for_the_browser()
    {
        string json = Serialize(new LiveErrorMessage("unknownRoom", "no such room"));

        json.ShouldContain("\"code\":\"unknownRoom\"");
        json.ShouldContain("\"message\":\"no such room\"");
    }

    /// <summary>
    /// Most fields on a tower row are absent most of the time — a car that has not set a lap has
    /// null times, gaps and sectors. Omitting them is the single largest saving on a message sent
    /// several times a second, and JavaScript cannot tell a missing property from an explicit null.
    /// </summary>
    [Fact]
    public void Null_fields_are_omitted_rather_than_written()
    {
        string json = Serialize(new TowerSnapshotMessage("room", DateTimeOffset.UnixEpoch, [EmptyRow()]));

        json.ShouldNotContain("bestLapMs");
        json.ShouldNotContain("gapToCarAheadMs");
        json.ShouldNotContain("position");

        // Non-null fields are still present, so the omission is selective rather than wholesale.
        json.ShouldContain("\"driverKey\":\"kimi\"");
        json.ShouldContain("\"completedLaps\":0");
    }

    /// <summary>
    /// The tier is what the dashboard uses to decide whether a row can be opened into a telemetry
    /// panel, so it has to be legible to the browser rather than an opaque integer.
    /// </summary>
    [Fact]
    public void The_data_tier_serializes_as_a_name_not_a_number()
    {
        string json = Serialize(new TowerSnapshotMessage("room", DateTimeOffset.UnixEpoch, [EmptyRow() with { Tier = LiveDataTier.Self }]));

        json.ShouldContain("\"tier\":\"Self\"");
    }

    [Fact]
    public void Durations_cross_as_numbers_of_milliseconds_not_TimeSpan_strings()
    {
        var row = EmptyRow() with
        {
            BestLapMs = TimeSpan.FromSeconds(91.125).TotalMilliseconds,
            GapToCarAheadMs = TimeSpan.FromSeconds(1.25).TotalMilliseconds,
        };

        string json = Serialize(new TowerSnapshotMessage("room", DateTimeOffset.UnixEpoch, [row]));

        json.ShouldContain("\"bestLapMs\":91125");
        json.ShouldContain("\"gapToCarAheadMs\":1250");
    }

    [Theory]
    [InlineData("""{"type":"watchRoom","roomId":"abc"}""", typeof(WatchRoomCommand))]
    [InlineData("""{"type":"focusDriver","driverKey":"kimi"}""", typeof(FocusDriverCommand))]
    public void Viewer_commands_deserialize_to_their_own_type(string json, Type expected)
    {
        var command = JsonSerializer.Deserialize<LiveViewCommand>(json, LiveViewJson.Default);

        command.ShouldNotBeNull();
        command.GetType().ShouldBe(expected);
    }

    [Fact]
    public void A_command_can_clear_its_subscription_with_a_null_target()
    {
        JsonSerializer.Deserialize<LiveViewCommand>("""{"type":"watchRoom"}""", LiveViewJson.Default)
            .ShouldBeOfType<WatchRoomCommand>().RoomId.ShouldBeNull();

        JsonSerializer.Deserialize<LiveViewCommand>("""{"type":"focusDriver","driverKey":null}""", LiveViewJson.Default)
            .ShouldBeOfType<FocusDriverCommand>().DriverKey.ShouldBeNull();
    }

    /// <summary>
    /// The viewing endpoint is open, so a command arrives from an unauthenticated source. An
    /// unknown discriminator must fail loudly at the parse rather than being silently ignored or
    /// landing on some default command.
    /// </summary>
    [Fact]
    public void An_unknown_command_type_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<LiveViewCommand>("""{"type":"shutdownEverything"}""", LiveViewJson.Default));
    }

    private static TowerRow EmptyRow() => new(
        DriverKey: "kimi",
        DisplayName: "Kimi",
        CarNumber: null,
        SimCarId: null,
        SimCarClassId: null,
        Position: null,
        PositionInClass: null,
        CompletedLaps: 0,
        TrackPositionFraction: null,
        Sector: null,
        SpeedMetersPerSecond: null,
        CurrentLapMs: null,
        PreviousLapMs: null,
        BestLapMs: null,
        CurrentLapValid: null,
        PreviousLapValid: null,
        CurrentSectorMs: [],
        PreviousSectorMs: [],
        BestSectorMs: [],
        GapToCarAheadMs: null,
        GapToCarBehindMs: null,
        InPitLane: null,
        PitLaneState: (int)Core.Sessions.PitLaneState.Unavailable,
        PitStopStatus: (int)Core.Sessions.PitStopStatus.Unavailable,
        PitStopCount: null,
        FinishStatus: (int)Core.Sessions.DriverFinishStatus.Unavailable,
        PenaltyCount: null,
        Tier: LiveDataTier.Observed);
}
