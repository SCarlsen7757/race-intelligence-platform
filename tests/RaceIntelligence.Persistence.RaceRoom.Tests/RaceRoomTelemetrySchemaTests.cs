using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RaceIntelligence.Persistence.RaceRoom.Entities;
using RaceIntelligence.RaceRoom.Telemetry;
using Shouldly;

namespace RaceIntelligence.Persistence.RaceRoom.Tests;

/// <summary>
/// Checks the EF Core model against the channel manifest, channel by channel.
/// </summary>
/// <remarks>
/// <para>
/// This used to be a hand-kept table of twelve promoted columns, restating their names, CLR types
/// and store types beside the configuration that already declared them. Both come from
/// <c>channels/raceroom-telemetry.channels</c> now, so restating them would be copying the manifest
/// and calling the copy a test.
/// </para>
/// <para>
/// What is still worth asserting is that the generated configuration <b>reached the model</b>: a
/// generator that emitted a <c>ConfigureChannels</c> nobody called, or an entity registered under a
/// different table, compiles and passes everything else. Needs no database.
/// </para>
/// </remarks>
public sealed class RaceRoomTelemetrySchemaTests
{
    private static IEntityType TelemetryEntity()
    {
        var options = new DbContextOptionsBuilder<RaceRoomDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;

        using var db = new RaceRoomDbContext(options);
        return db.Model.FindEntityType(typeof(TelemetrySample)).ShouldNotBeNull();
    }

    [Fact]
    public void Every_manifest_channel_is_mapped_to_its_column_with_its_declared_type()
    {
        var entity = TelemetryEntity();
        var table = StoreObjectIdentifier.Table("telemetry_samples");

        RaceRoomChannels.All.Count.ShouldBeGreaterThan(150, "the manifest should describe every channel");

        foreach (var channel in RaceRoomChannels.All)
        {
            var propertyName = char.ToUpperInvariant(channel.Name[0]) + channel.Name[1..];
            var property = entity.FindProperty(propertyName);

            property.ShouldNotBeNull($"channel '{channel.Name}' has no property '{propertyName}' on the entity");
            property.GetColumnName(table).ShouldBe(channel.Column);
            property.GetColumnType().ShouldBe(channel.StoreType);
            property.IsNullable.ShouldBe(
                channel.IsNullable,
                $"{channel.Column} must be {(channel.IsNullable ? "nullable" : "NOT NULL")}: an absent reading is not a zero");
        }
    }

    /// <summary>
    /// The entity declares nothing the manifest does not. A property added by hand beside the
    /// generated ones would get a convention-derived column name and quietly join the table.
    /// </summary>
    [Fact]
    public void The_entity_declares_no_column_the_manifest_does_not()
    {
        var entity = TelemetryEntity();
        var declared = RaceRoomChannels.All.Select(channel => channel.Column).ToHashSet(StringComparer.Ordinal);
        var table = StoreObjectIdentifier.Table("telemetry_samples");

        foreach (var property in entity.GetProperties())
        {
            declared.ShouldContain(
                property.GetColumnName(table),
                $"'{property.Name}' is mapped but is not a manifest channel");
        }
    }

    /// <summary>
    /// None of the channels is indexed. A hundred and seventy-five B-trees over an insert-only table
    /// that takes rows at 58 Hz would cost more on every write than they could return on a query
    /// nobody has asked yet; the two indexes this table has are deliberate and named.
    /// </summary>
    [Fact]
    public void No_channel_carries_an_index_of_its_own()
    {
        var entity = TelemetryEntity();

        var indexNames = entity.GetIndexes()
            .Select(index => index.GetDatabaseName())
            .ToList();

        indexNames.ShouldBe(["ix_telemetry_timestamp_brin", "ix_telemetry_session_lap"], ignoreOrder: true);
    }
}
