using System.Text.Json;
using System.Text.RegularExpressions;
using RaceIntelligence.RaceRoom.Telemetry;
using RaceIntelligence.Read.Api.Contracts;
using Shouldly;

namespace RaceIntelligence.Read.Api.Tests;

/// <summary>
/// Pins the JSON the dashboard parses off the read API, field by field.
/// </summary>
/// <remarks>
/// <para>
/// <b>The live wire has had this since it was written and the read wire has had nothing.</b>
/// <c>LiveViewContractShapeTests</c> exists because the dashboard's TypeScript is hand-written and
/// nothing in either toolchain connects it to the C# records: a rename compiles, passes every other
/// test, and reaches a race engineer as a column that has quietly gone blank, because JavaScript
/// reads a missing property as <c>undefined</c> rather than failing. The read contracts added in
/// #69 and #106 had exactly the same exposure and no such seam. This is it (#109).
/// </para>
/// <para>
/// Needs no database and no host: these are the response records and the file on disk. Both are
/// facts about the source tree, which is what makes this suite's other, heavier tests worth
/// leaving alone.
/// </para>
/// </remarks>
public sealed class ReadContractShapeTests
{
    /// <summary>The dashboard's hand-written mirror of these records.</summary>
    private static string ContractsTypeScript()
    {
        // Up from tests/<suite>/bin/<config>/<tfm> to the repository root. Brittle-looking and
        // deliberately not made clever: the alternative is copying the file into the build output,
        // which would let the test pass against a stale copy of the thing it is checking.
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RaceIntelligence.Dashboard", "app", "shared", "live", "contracts.ts"));

        File.Exists(path).ShouldBeTrue($"the dashboard's live contracts should be at {path}");
        return File.ReadAllText(path);
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name);

    private static JsonElement Serialize<T>(T value) =>
        JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static TelemetrySampleResponse Sample(IReadOnlyDictionary<string, object?>? channels = null) => new(
        SequenceNumber: 7,
        TimestampUtc: DateTimeOffset.UnixEpoch,
        SimulationTime: 12.5,
        LapNumber: 3,
        Sector: 2,
        Speed: 58.5f,
        Throttle: 0.75f,
        Brake: null,
        Clutch: 0.25f,
        Steering: -0.5f,
        Gear: 4,
        EngineRpm: 7200f,
        FuelLeft: 42.5f,
        Position: 3,
        TrackPositionFraction: 0.42f,
        Channels: channels);

    [Fact]
    public void A_stored_sample_carries_the_names_the_dashboard_reads()
    {
        var json = Serialize(Sample());

        PropertyNames(json).ShouldBe(
            [
                "sequenceNumber", "timestampUtc", "simulationTime", "lapNumber", "sector",
                "speed", "throttle", "brake", "clutch", "steering", "gear", "engineRpm",
                "fuelLeft", "position", "trackPositionFraction", "channels",
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void A_lap_and_its_page_carry_the_names_the_dashboard_reads()
    {
        PropertyNames(Serialize(new LapResponse(7, 104_500, 3.2f, 55f, 71f, IsValid: true))).ShouldBe(
            ["lapNumber", "lapTimeMs", "fuelUsed", "avgSpeed", "maxSpeed", "isValid"],
            ignoreOrder: true);

        PropertyNames(Serialize(new LapSamplesResponse(3, [Sample()]))).ShouldBe(
            ["lapNumber", "samples"],
            ignoreOrder: true);

        PropertyNames(Serialize(new TelemetryResponse(Guid.Empty, [new LapSamplesResponse(3, [Sample()])])))
            .ShouldBe(["sessionId", "laps"], ignoreOrder: true);
    }

    /// <summary>
    /// The extra channels cross as a map keyed by channel name, so a caller reads back exactly the
    /// names it asked for.
    /// </summary>
    [Fact]
    public void Requested_channels_cross_under_their_own_names()
    {
        var json = Serialize(Sample(new Dictionary<string, object?>
        {
            ["tyreGripFl"] = 0.97f,
            ["camberFl"] = -0.06f,
        }));

        var channels = json.GetProperty("channels");
        channels.GetProperty("tyreGripFl").GetSingle().ShouldBe(0.97f);
        channels.GetProperty("camberFl").GetSingle().ShouldBe(-0.06f);
    }

    /// <summary>
    /// Every channel the dashboard types is one the manifest declares.
    /// </summary>
    /// <remarks>
    /// The dashboard mirrors a subset of the manifest by hand — a wall does not draw a hundred and
    /// seventy-five channels — so the check runs one way: a name typed there must exist here. A
    /// rename in the manifest then fails this test rather than blanking a tile, which is the failure
    /// this whole file exists to convert.
    /// </remarks>
    [Fact]
    public void Every_channel_the_dashboard_names_is_one_the_manifest_declares()
    {
        var source = ContractsTypeScript();

        var body = source[source.IndexOf("export interface RaceRoomSample {", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n}", StringComparison.Ordinal)];

        var declared = Regex.Matches(body, @"^\s{2}(\w+)\?:", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToList();

        declared.Count.ShouldBeGreaterThan(40, "the dashboard should be typing the channels its widgets read");

        foreach (var name in declared)
        {
            RaceRoomChannels.ByName.ShouldContainKey(
                name,
                $"'{name}' is typed in the dashboard but is not a channel the manifest declares");
        }
    }

    /// <summary>
    /// The corner order the dashboard indexes positionally, asserted against the generated columns.
    /// </summary>
    /// <remarks>
    /// FL, FR, RL, RR everywhere. The dashboard's `WHEELS` constant is a hand-written mirror of an
    /// order the manifest fixes by generating four columns per per-wheel channel in exactly that
    /// sequence, and getting it wrong swaps one side of the car for the other on every chart.
    /// </remarks>
    [Fact]
    public void The_manifest_orders_every_per_wheel_channel_front_left_first()
    {
        var grip = RaceRoomChannels.All
            .Where(channel => channel.Name.StartsWith("tyreGrip", StringComparison.Ordinal))
            .Select(channel => channel.Column)
            .ToList();

        grip.ShouldBe(["tyre_grip_fl", "tyre_grip_fr", "tyre_grip_rl", "tyre_grip_rr"]);

        ContractsTypeScript().ShouldContain("export const WHEELS = ['FL', 'FR', 'RL', 'RR'] as const;");
    }
}
