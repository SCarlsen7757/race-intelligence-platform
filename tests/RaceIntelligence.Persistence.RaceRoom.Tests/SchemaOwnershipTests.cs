using Microsoft.EntityFrameworkCore;
using RaceIntelligence.Persistence.Core;
using Shouldly;

namespace RaceIntelligence.Persistence.RaceRoom.Tests;

/// <summary>
/// The invariant the per-simulator split rests on: <b>Core declares no schema.</b>
/// </summary>
/// <remarks>
/// Storage is one database per simulator, and each is free to shape its schema to what that
/// simulator actually exposes (ADR 0001). That only holds while the shared half stays a set of
/// types — the moment one <c>ToTable</c> appears in Core, every simulator inherits a table it did
/// not choose, and the freedom the split was bought with is gone.
/// <para>
/// It is the kind of thing that erodes by accident rather than by decision: a configuration put in
/// the obvious-looking project compiles, passes every other test, and is only noticed when the
/// second simulator cannot express its own schema. So it is asserted rather than written down.
/// </para>
/// <para>
/// No database needed — these read the assemblies.
/// </para>
/// </remarks>
public sealed class SchemaOwnershipTests
{
    [Fact]
    public void Core_declares_no_entity_configurations()
    {
        var core = typeof(TelemetryDbContext).Assembly;

        var configurations = core.GetTypes()
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)))
            .Select(t => t.FullName)
            .ToList();

        configurations.ShouldBeEmpty(
            "Core owns the entity types; a simulator owns what they are called in its database.");
    }

    /// <summary>
    /// Core's context is abstract, so nothing can construct a store out of it and get tables by
    /// accident. A simulator has to say which schema it means.
    /// </summary>
    [Fact]
    public void Core_has_no_context_anyone_can_instantiate()
    {
        typeof(TelemetryDbContext).IsAbstract.ShouldBeTrue();

        typeof(TelemetryDbContext).Assembly.GetTypes()
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && !t.IsAbstract)
            .ShouldBeEmpty("a concrete context in Core would be a schema in Core");
    }

    /// <summary>And the other half of it: RaceRoom's project is where the mapping actually lives.</summary>
    [Fact]
    public void RaceRoom_owns_the_configurations()
    {
        var raceRoom = typeof(RaceRoomDbContext).Assembly;

        raceRoom.GetTypes()
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)))
            .ShouldNotBeEmpty();
    }

    /// <summary>
    /// Every entity set the shared shape names is mapped by this simulator.
    /// </summary>
    /// <remarks>
    /// A simulator may add tables of its own, but it cannot quietly drop one of the canonical ones —
    /// the mappers and repositories in Core assume all of them exist, and a missing mapping surfaces
    /// as a runtime failure on the first session rather than as a build error.
    /// </remarks>
    [Fact]
    public void RaceRoom_maps_every_canonical_entity()
    {
        var options = new DbContextOptionsBuilder<RaceRoomDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;

        using var db = new RaceRoomDbContext(options);
        var mapped = db.Model.GetEntityTypes().Select(t => t.ClrType).ToHashSet();

        var canonical = typeof(TelemetryDbContext)
            .GetProperties()
            .Where(p => p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToList();

        canonical.ShouldNotBeEmpty();

        foreach (var entity in canonical)
        {
            mapped.ShouldContain(entity, $"{entity.Name} is part of the canonical shape and must be mapped");
        }
    }
}
