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
        propertyNames.ShouldContain("flags");
        propertyNames.ShouldContain("pit");
        propertyNames.ShouldContain("tyreGrip");
        propertyNames.ShouldContain("tyreLoadNewtons");
        propertyNames.ShouldContain("tyreDirt");
        propertyNames.ShouldContain("tyreFlatspot");
        propertyNames.ShouldContain("tyreRotationRadiansPerSecond");
        propertyNames.ShouldContain("tyreSurfaceMaterial");
        propertyNames.ShouldContain("incidentPoints");
        propertyNames.ShouldContain("maxIncidentPoints");
    }

    [Fact]
    public void SampleExtras_CarryTheIncidentPointsAndTheServerLimit()
    {
        // Both are root-level in the shared block and describe the local car only, so sample extras
        // -- the one document that reaches a viewer, as `extrasFrame` -- is where they have to be.
        // Asymmetric values so a field wired to the wrong source cannot pass.
        var extras = SampleExtras(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.IncidentPoints = 4;
            raw.MaxIncidentPoints = 10;
        }));

        extras.GetProperty("incidentPoints").GetInt32().ShouldBe(4);
        extras.GetProperty("maxIncidentPoints").GetInt32().ShouldBe(10);
    }

    [Fact]
    public void SampleExtras_IncidentPointsKeepTheirNotAvailableSentinel()
    {
        // Offline, or on a server that sets no limit, both read -1. Extras carry it untranslated --
        // the panel is where -1 becomes "not reported", because only there is there a difference
        // between "no limit set" and a limit that happens to be zero.
        var extras = SampleExtras();

        extras.GetProperty("incidentPoints").GetInt32().ShouldBe(-1);
        extras.GetProperty("maxIncidentPoints").GetInt32().ShouldBe(-1);
    }

    [Fact]
    public void SampleExtras_CarryAZeroIncidentCountAsARealAnswer()
    {
        // A clean sheet is not the same as no reading, and the writer must not conflate them.
        var extras = SampleExtras(builder => builder.Configure((ref R3ESharedRaw raw) => raw.IncidentPoints = 0));

        extras.GetProperty("incidentPoints").GetInt32().ShouldBe(0);
    }

    [Fact]
    public void SampleExtras_CarryTheTyreChannelsADegradationModelNeeds()
    {
        // None of these can be backfilled: they are only ever observed live, and raw telemetry is
        // never rewritten. tyreGrip especially -- it is the one channel that measures grip loss
        // directly instead of inferring it from lap time.
        var extras = SampleExtras(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            // Asymmetric throughout, so a transposed index or a field wired to the wrong source
            // cannot pass.
            raw.TireGrip[0] = 0.91f;
            raw.TireGrip[1] = 0.92f;
            raw.TireGrip[2] = 0.93f;
            raw.TireGrip[3] = 0.94f;

            raw.TireLoad[0] = 1100f;
            raw.TireLoad[1] = 1200f;
            raw.TireLoad[2] = 1300f;
            raw.TireLoad[3] = 1400f;

            raw.TireDirt[0] = 0.01f;
            raw.TireDirt[1] = 0.02f;
            raw.TireDirt[2] = 0.03f;
            raw.TireDirt[3] = 0.04f;

            raw.TireFlatspot[0] = 0;
            raw.TireFlatspot[1] = 1;
            raw.TireFlatspot[2] = 0;
            raw.TireFlatspot[3] = 1;

            raw.TireRps[0] = 51f;
            raw.TireRps[1] = 52f;
            raw.TireRps[2] = 53f;
            raw.TireRps[3] = 54f;

            raw.TireOnMtrl[0] = 1;
            raw.TireOnMtrl[1] = 2;
            raw.TireOnMtrl[2] = 3;
            raw.TireOnMtrl[3] = 4;
        }));

        extras.GetProperty("tyreGrip").EnumerateArray().Select(e => e.GetSingle())
            .ShouldBe([0.91f, 0.92f, 0.93f, 0.94f]);
        extras.GetProperty("tyreLoadNewtons").EnumerateArray().Select(e => e.GetSingle())
            .ShouldBe([1100f, 1200f, 1300f, 1400f]);
        extras.GetProperty("tyreDirt").EnumerateArray().Select(e => e.GetSingle())
            .ShouldBe([0.01f, 0.02f, 0.03f, 0.04f]);
        extras.GetProperty("tyreFlatspot").EnumerateArray().Select(e => e.GetInt32())
            .ShouldBe([0, 1, 0, 1]);
        extras.GetProperty("tyreRotationRadiansPerSecond").EnumerateArray().Select(e => e.GetSingle())
            .ShouldBe([51f, 52f, 53f, 54f]);
        extras.GetProperty("tyreSurfaceMaterial").EnumerateArray().Select(e => e.GetInt32())
            .ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void SampleExtras_TyreChannelsKeepTheirNotAvailableSentinel()
    {
        // These stay raw, like every other value in this object: -1 means the sim did not report
        // it. Anything reading them has to know that, which is exactly the argument for promoting
        // tyreGrip to a canonical field, where the -1 -> null translation already has a home.
        var extras = SampleExtras();

        extras.GetProperty("tyreGrip").EnumerateArray().Select(e => e.GetSingle())
            .ShouldBe([-1f, -1f, -1f, -1f]);
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

            // The window rides beside the reading. Cold and hot differ per corner so a copied
            // index would show, and the front left's hot bound is the sentinel: a pad the
            // simulator named no ceiling for must not arrive as one at -1 degrees.
            raw.BrakeTemp[0].OptimalTemp = 410f;
            raw.BrakeTemp[1].OptimalTemp = 420f;
            raw.BrakeTemp[2].OptimalTemp = 430f;
            raw.BrakeTemp[3].OptimalTemp = 440f;
            raw.BrakeTemp[0].ColdTemp = 201f;
            raw.BrakeTemp[0].HotTemp = -1f;

            raw.BrakePressure[0] = 1f;
            raw.BrakePressure[1] = 2f;
            raw.BrakePressure[2] = 3f;
            raw.BrakePressure[3] = 4f;
        }));

        var brakes = extras.GetProperty("brakeTemperatureCelsius").EnumerateArray().ToList();

        brakes.Select(e => e.GetProperty("current").GetSingle())
            .ShouldBe([101f, 202f, 303f, 404f]);

        // The window the simulator names for these pads, which used to be dropped one line after
        // the reading was written. 380 degrees is cold on one car and cooking on another.
        brakes.Select(e => e.GetProperty("optimal").GetSingle())
            .ShouldBe([410f, 420f, 430f, 440f]);
        brakes[0].GetProperty("cold").GetSingle().ShouldBe(201f);

        // Raw, sentinel and all — the same rule the whole document follows. A consumer runs these
        // through its own sentinel check; the mapper does not decide for it.
        brakes[0].GetProperty("hot").GetSingle().ShouldBe(-1f);

        // Brake pressure is deliberately absent: it moved to the canonical sample, and so to the
        // full-rate wire, because it changes as fast as the pedal does. This document is written
        // once a second, which is one or two samples of a braking event.
        extras.EnumerateObject().Select(property => property.Name)
            .ShouldNotContain("brakePressureKiloNewtons");
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
