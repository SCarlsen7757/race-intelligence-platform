using System.Text.Json;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Persistence.Converters;
using Shouldly;

namespace RaceIntelligence.Persistence.Tests;

/// <summary>
/// Pure unit tests for the EF Core value converters and comparers.
/// </summary>
/// <remarks>
/// These need no database, so unlike the Testcontainers-backed suites they run everywhere —
/// including CI without a container runtime. The behaviours they pin (structural jsonb comparison
/// and lossless capability round-tripping) are the ones whose failure modes are silent rather than
/// loud: a broken comparer produces spurious UPDATEs, and a lossy capability conversion silently
/// disables analysis algorithms.
/// </remarks>
public class ConverterTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    [Fact]
    public void JsonElement_round_trips_through_the_converter()
    {
        var original = Json("""{"ERSMode":3,"HybridBattery":0.72}""");

        var serialized = JsonElementConverter.Converter.ConvertToProvider(original) as string;
        var restored = (JsonElement)JsonElementConverter.Converter.ConvertFromProvider(serialized!)!;

        restored.GetProperty("ERSMode").GetInt32().ShouldBe(3);
        restored.GetProperty("HybridBattery").GetDouble().ShouldBe(0.72);
    }

    [Fact]
    public void Comparer_treats_structurally_equal_elements_as_equal()
    {
        // This is the regression guard for the "every entity looks permanently modified" bug:
        // materialization always produces a fresh JsonElement instance, so reference equality
        // would report a change on every load.
        var left = Json("""{"a":1,"b":[2,3]}""");
        var right = Json("""{"a":1,"b":[2,3]}""");

        // JsonElement has no value-based Equals override, so the comparer EF would use by default
        // does NOT see these as equal — which is precisely why the explicit comparer is required.
        EqualityComparer<JsonElement>.Default.Equals(left, right).ShouldBeFalse();
        JsonElementConverter.Comparer.Equals(left, right).ShouldBeTrue();
        JsonElementConverter.Comparer.GetHashCode(left).ShouldBe(JsonElementConverter.Comparer.GetHashCode(right));
    }

    [Fact]
    public void Comparer_detects_a_genuine_difference()
    {
        JsonElementConverter.Comparer
            .Equals(Json("""{"a":1}"""), Json("""{"a":2}"""))
            .ShouldBeFalse();
    }

    [Fact]
    public void Nullable_comparer_handles_null_on_either_side()
    {
        JsonElementConverter.NullableComparer.Equals(null, null).ShouldBeTrue();
        JsonElementConverter.NullableComparer.Equals(Json("""{"a":1}"""), null).ShouldBeFalse();
        JsonElementConverter.NullableComparer.Equals(null, Json("""{"a":1}""")).ShouldBeFalse();
    }

    [Fact]
    public void Nullable_converter_maps_null_to_null()
    {
        JsonElementConverter.NullableConverter.ConvertToProvider(null).ShouldBeNull();
        JsonElementConverter.NullableConverter.ConvertFromProvider(null).ShouldBeNull();
    }

    [Theory]
    [InlineData(SimCapabilities.None)]
    [InlineData(SimCapabilities.TyreWear)]
    [InlineData(SimCapabilities.TyreWear | SimCapabilities.FuelFlow | SimCapabilities.Damage)]
    public void SimCapabilities_round_trip_through_bigint(SimCapabilities capabilities)
    {
        var stored = (long)SimCapabilitiesConverter.Converter.ConvertToProvider(capabilities)!;
        var restored = (SimCapabilities)SimCapabilitiesConverter.Converter.ConvertFromProvider(stored)!;

        restored.ShouldBe(capabilities);
    }

    [Fact]
    public void SimCapabilities_preserves_the_top_bit()
    {
        // Bit 63 would overflow a checked conversion to long. The unchecked reinterpretation must
        // keep it, otherwise the highest-numbered capability silently vanishes on save/load.
        var topBit = (SimCapabilities)(1UL << 63);

        var stored = (long)SimCapabilitiesConverter.Converter.ConvertToProvider(topBit)!;
        var restored = (SimCapabilities)SimCapabilitiesConverter.Converter.ConvertFromProvider(stored)!;

        stored.ShouldBeLessThan(0L, "the top bit is expected to read as negative in a signed column");
        restored.ShouldBe(topBit);
    }

    [Fact]
    public void SimCapabilities_preserves_every_declared_flag_together()
    {
        var all = Enum.GetValues<SimCapabilities>().Aggregate(SimCapabilities.None, (acc, f) => acc | f);

        var stored = (long)SimCapabilitiesConverter.Converter.ConvertToProvider(all)!;
        var restored = (SimCapabilities)SimCapabilitiesConverter.Converter.ConvertFromProvider(stored)!;

        restored.ShouldBe(all);
    }
}
