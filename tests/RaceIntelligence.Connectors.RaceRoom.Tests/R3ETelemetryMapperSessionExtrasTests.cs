using System.Text.Json;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Covers the hand-rolled JSON written into a session's <c>Extras</c>.
/// </summary>
/// <remarks>
/// A sample's channels are typed columns now, but a session's are still a document, and for a reason
/// that survives #109: this runs once per session rather than sixty times a second, and what it
/// carries is a bag of simulator identifiers and session settings nothing queries by. The
/// by-hand <see cref="Utf8JsonWriter"/> calls are still worth pinning — a mismatched
/// WriteStartObject/WriteEndObject pair or a field wired to the wrong source would be stored
/// permanently.
/// </remarks>
public class R3ETelemetryMapperSessionExtrasTests
{
    private static JsonElement SessionExtras(Action<R3ESharedRawBuilder>? configure = null)
    {
        var builder = new R3ESharedRawBuilder().InRaceSession("Extras Track", "Extras Layout");
        configure?.Invoke(builder);
        var raw = builder.Build();

        var gameVersion = new GameVersionIdentity
        {
            Game = WellKnownGames.RaceRoom,
            ApiVersionMajor = 3,
            ApiVersionMinor = 5,
            ConnectorVersion = "test",
        };

        return R3ETelemetryMapper.ToSessionInfo(in raw, Guid.NewGuid(), gameVersion, SimCapabilities.None, DateTimeOffset.UtcNow).Extras;
    }

    [Fact]
    public void SessionExtras_IsAWellFormedObjectCarryingTheVehicleIds()
    {
        var extras = SessionExtras(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.VehicleInfo.ClassId = 1601;
            raw.VehicleInfo.ModelId = 2922;
            raw.VehicleInfo.ManufacturerId = 3301;
            raw.NumberOfLaps = 12;
        }));

        extras.ValueKind.ShouldBe(JsonValueKind.Object);
        extras.GetProperty("numberOfLaps").GetInt32().ShouldBe(12);
        extras.GetProperty("vehicle").GetProperty("classId").GetInt32().ShouldBe(1601);
        extras.GetProperty("vehicle").GetProperty("modelId").GetInt32().ShouldBe(2922);
        extras.GetProperty("vehicle").GetProperty("manufacturerId").GetInt32().ShouldBe(3301);
    }

    /// <summary>
    /// The scratch buffer and the writer are reused between calls, so a second session must not see
    /// the first one's bytes.
    /// </summary>
    [Fact]
    public void ConsecutiveSessions_DoNotShareOrLeakTheReusedJsonBuffer()
    {
        var first = SessionExtras(builder => builder.Configure((ref R3ESharedRaw raw) => raw.NumberOfLaps = 11));
        var second = SessionExtras(builder => builder.Configure((ref R3ESharedRaw raw) => raw.NumberOfLaps = 22));

        first.GetProperty("numberOfLaps").GetInt32().ShouldBe(11);
        second.GetProperty("numberOfLaps").GetInt32().ShouldBe(22);
    }
}
