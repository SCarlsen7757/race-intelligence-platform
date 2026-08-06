using System.Text.Json;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Covers the hand-rolled nested JSON written into every sample's (and session's) <c>Extras</c>.
/// This is ~100 lines of by-hand <see cref="Utf8JsonWriter"/> calls executed 60 times a second and
/// stored permanently, so a mismatched WriteStartObject/WriteEndObject pair, a duplicated property
/// name, or a field wired to the wrong source would corrupt every sample ever recorded — and none
/// of it was exercised. Also pins the buffer reuse: the writer and scratch buffer are shared
/// between calls, so a second call must not see the first call's bytes.
/// </summary>
public class R3ETelemetryMapperExtrasTests
{
    private static JsonElement SampleExtras(Action<R3ESharedRawBuilder>? configure = null)
    {
        var builder = new R3ESharedRawBuilder().InRaceSession("Extras Track", "Extras Layout");
        configure?.Invoke(builder);
        var raw = builder.Build();

        // Sample extras are raw JSON text now; parsing here keeps these tests asserting on the
        // structure the connector produced, and proves that text is well-formed JSON.
        string text = R3ETelemetryMapper.ToSample(in raw, Guid.NewGuid(), sequenceNumber: 0, DateTimeOffset.UtcNow).Extras;
        return JsonDocument.Parse(text).RootElement.Clone();
    }

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
    public void SampleExtras_IsAWellFormedJsonObject()
    {
        var extras = SampleExtras();

        extras.ValueKind.ShouldBe(JsonValueKind.Object);

        // Re-parsing the raw text proves every WriteStartObject/WriteStartArray was closed: an
        // unbalanced writer would have thrown or produced text that will not round-trip.
        using var reparsed = JsonDocument.Parse(extras.GetRawText());
        reparsed.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    public void SampleExtras_CarriesEveryTopLevelGroupExactlyOnce()
    {
        var extras = SampleExtras();

        var propertyNames = extras.EnumerateObject().Select(property => property.Name).ToList();

        propertyNames.ShouldBe(propertyNames.Distinct().ToList(), "a duplicated Extras property name is silently accepted by the writer.");
        propertyNames.ShouldContain("pushToPass");
        propertyNames.ShouldContain("drs");
        propertyNames.ShouldContain("damage");
        propertyNames.ShouldContain("brakeTemperatureCelsius");
        propertyNames.ShouldContain("brakePressureKiloNewtons");
        propertyNames.ShouldContain("flags");
        propertyNames.ShouldContain("pit");
    }

    [Fact]
    public void SampleExtras_PerWheelArraysHaveFourEntriesInWheelOrder()
    {
        var extras = SampleExtras(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            // Asymmetric values so a transposed index cannot pass.
            raw.BrakeTemp[0].CurrentTemp = 101f;
            raw.BrakeTemp[1].CurrentTemp = 202f;
            raw.BrakeTemp[2].CurrentTemp = 303f;
            raw.BrakeTemp[3].CurrentTemp = 404f;

            raw.BrakePressure[0] = 1f;
            raw.BrakePressure[1] = 2f;
            raw.BrakePressure[2] = 3f;
            raw.BrakePressure[3] = 4f;
        }));

        extras.GetProperty("brakeTemperatureCelsius").EnumerateArray().Select(e => e.GetSingle())
            .ShouldBe([101f, 202f, 303f, 404f]);
        extras.GetProperty("brakePressureKiloNewtons").EnumerateArray().Select(e => e.GetSingle())
            .ShouldBe([1f, 2f, 3f, 4f]);
    }

    [Fact]
    public void SampleExtras_NestedGroupsCarryTheRawValuesTheyName()
    {
        var extras = SampleExtras(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.PushToPass.Available = 1;
            raw.PushToPass.Engaged = 0;
            raw.PushToPass.AmountLeft = 7;
            raw.Drs.Equipped = 1;
            raw.Drs.NumActivationsLeft = 3;
            raw.CarDamage.Engine = 0.25f;
            raw.Flags.Yellow = 1;
            raw.Flags.Blue = 0;
            raw.PitWindowStatus = 2;
            raw.NumPitstopsPerformed = 1;
        }));

        extras.GetProperty("pushToPass").GetProperty("available").GetInt32().ShouldBe(1);
        extras.GetProperty("pushToPass").GetProperty("amountLeft").GetInt32().ShouldBe(7);
        extras.GetProperty("drs").GetProperty("equipped").GetInt32().ShouldBe(1);
        extras.GetProperty("drs").GetProperty("numActivationsLeft").GetInt32().ShouldBe(3);
        extras.GetProperty("damage").GetProperty("engine").GetSingle().ShouldBe(0.25f);
        extras.GetProperty("flags").GetProperty("yellow").GetInt32().ShouldBe(1);
        extras.GetProperty("flags").GetProperty("blue").GetInt32().ShouldBe(0);
        extras.GetProperty("pit").GetProperty("windowStatus").GetInt32().ShouldBe(2);
        extras.GetProperty("pit").GetProperty("numPitstopsPerformed").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void SampleExtras_KeepsRawSentinelsRatherThanNullingThem()
    {
        // Extras is the raw, uninterpreted passthrough channel: unlike the canonical fields, a -1
        // here means "the sim said -1" and translating it would lose information the analysis layer
        // may want. The builder defaults every documented N/A field to its sentinel.
        var extras = SampleExtras();

        extras.GetProperty("damage").GetProperty("engine").GetSingle().ShouldBe(-1f);
        extras.GetProperty("batteryStateOfChargePercent").GetSingle().ShouldBe(-1f);
        extras.GetProperty("tractionControlSetting").GetInt32().ShouldBe(-1);
    }

    [Fact]
    public void ConsecutiveSamples_DoNotShareOrLeakTheReusedJsonBuffer()
    {
        // The scratch buffer and Utf8JsonWriter are reused per thread. If the buffer were not
        // rewound (or the element not detached from it), the second sample would either see the
        // first sample's bytes appended or observe the first element mutating underneath it.
        var first = SampleExtras(builder => builder.Configure((ref R3ESharedRaw raw) => raw.PushToPass.AmountLeft = 11));
        string firstTextBefore = first.GetRawText();

        var second = SampleExtras(builder => builder.Configure((ref R3ESharedRaw raw) => raw.PushToPass.AmountLeft = 22));

        first.GetProperty("pushToPass").GetProperty("amountLeft").GetInt32().ShouldBe(11);
        second.GetProperty("pushToPass").GetProperty("amountLeft").GetInt32().ShouldBe(22);
        first.GetRawText().ShouldBe(firstTextBefore, "an already-produced Extras element must not change when the next sample is mapped.");
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
}
