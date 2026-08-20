using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RaceIntelligence.Persistence.Core.Entities;
using Shouldly;

namespace RaceIntelligence.Persistence.RaceRoom.Tests;

public sealed class RaceRoomTelemetrySchemaTests
{
    private static readonly (string Property, Type ClrType, string Column, string StoreType)[] PromotedProperties =
    [
        ("PushToPassAvailable", typeof(int?), "push_to_pass_available", "integer"),
        ("PushToPassEngaged", typeof(int?), "push_to_pass_engaged", "integer"),
        ("PushToPassAmountLeft", typeof(int?), "push_to_pass_amount_left", "integer"),
        ("PushToPassEngagedTimeLeftSeconds", typeof(float?), "push_to_pass_engaged_time_left_seconds", "real"),
        ("PushToPassWaitTimeLeftSeconds", typeof(float?), "push_to_pass_wait_time_left_seconds", "real"),
        ("TyreSubtypeFront", typeof(int?), "tyre_subtype_front", "integer"),
        ("TyreSubtypeRear", typeof(int?), "tyre_subtype_rear", "integer"),
        ("CutTrackWarnings", typeof(int?), "cut_track_warnings", "integer"),
        ("DamageEngine", typeof(float?), "damage_engine", "real"),
        ("DamageTransmission", typeof(float?), "damage_transmission", "real"),
        ("DamageAerodynamics", typeof(float?), "damage_aerodynamics", "real"),
        ("DamageSuspension", typeof(float?), "damage_suspension", "real"),
    ];

    [Fact]
    public void Promoted_extras_are_nullable_RaceRoom_shadow_properties_with_expected_column_types()
    {
        var options = new DbContextOptionsBuilder<RaceRoomDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;

        using var db = new RaceRoomDbContext(options);
        var entity = db.Model.FindEntityType(typeof(TelemetrySample));
        entity.ShouldNotBeNull();
        var table = StoreObjectIdentifier.Table("telemetry_samples");

        foreach (var expected in PromotedProperties)
        {
            var property = entity.FindProperty(expected.Property);
            property.ShouldNotBeNull();
            property.IsShadowProperty().ShouldBeTrue();
            property.IsNullable.ShouldBeTrue();
            property.ClrType.ShouldBe(expected.ClrType);
            property.GetColumnName(table).ShouldBe(expected.Column);
            property.GetColumnType().ShouldBe(expected.StoreType);
        }

        var indexedProperties = entity.GetIndexes()
            .SelectMany(index => index.Properties)
            .Select(property => property.Name)
            .ToHashSet();
        indexedProperties.Intersect(PromotedProperties.Select(expected => expected.Property))
            .ShouldBeEmpty();
    }
}
