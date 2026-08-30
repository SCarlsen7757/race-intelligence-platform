using System.Reflection;
using MessagePack;
using RaceIntelligence.RaceRoom.Telemetry;
using Shouldly;

namespace RaceIntelligence.RaceRoom.Channels.Tests;

/// <summary>
/// Pins the properties of the channel manifest that the generator cannot enforce for itself.
/// </summary>
/// <remarks>
/// <para>
/// The generator refuses a duplicate name or column outright — those are compile errors, not test
/// failures. What it cannot check is whether the list it produced is <i>coherent</i>: whether the
/// MessagePack keys are contiguous, whether the corner order is the platform's, whether the sample
/// type and the manifest describe the same channels. Those are the ways a manifest edit goes wrong
/// quietly.
/// </para>
/// <para>
/// Needs nothing — no database, no container, no Windows. It reads generated metadata.
/// </para>
/// </remarks>
public sealed class ChannelManifestTests
{
    private static PropertyInfo[] SampleProperties() =>
        typeof(RaceRoomTelemetrySample).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<KeyAttribute>() is not null)
            .ToArray();

    [Fact]
    public void The_manifest_describes_every_channel_and_nothing_else()
    {
        var properties = SampleProperties();

        properties.Length.ShouldBe(RaceRoomChannels.All.Count);
        RaceRoomChannels.All.Count.ShouldBeGreaterThan(150, "RaceRoom exposes a great many channels");
    }

    /// <summary>
    /// Keys are the manifest's own ordering, assigned rather than written down.
    /// </summary>
    /// <remarks>
    /// This is what makes reordering the manifest free: nothing hand-numbers a key, so a channel
    /// inserted in the middle renumbers everything after it and no two members can collide. Pre-v1
    /// that renumbering costs nothing (see CLAUDE.md); after v1.0.0 it is exactly the change a
    /// schema-version bump would be for.
    /// </remarks>
    [Fact]
    public void The_messagepack_keys_are_contiguous_from_zero_and_follow_manifest_order()
    {
        var keys = SampleProperties()
            .Select(property => property.GetCustomAttribute<KeyAttribute>()!.IntKey!.Value)
            .OrderBy(key => key)
            .ToList();

        keys.ShouldBe(Enumerable.Range(0, RaceRoomChannels.All.Count));
    }

    /// <summary>
    /// Every channel name capitalises to a property that exists, which is the assumption every
    /// reflective consumer of the manifest makes.
    /// </summary>
    [Fact]
    public void Every_channel_name_matches_a_property_on_the_sample()
    {
        foreach (var channel in RaceRoomChannels.All)
        {
            var propertyName = char.ToUpperInvariant(channel.Name[0]) + channel.Name[1..];

            typeof(RaceRoomTelemetrySample).GetProperty(propertyName)
                .ShouldNotBeNull($"channel '{channel.Name}' should map to a property '{propertyName}'");
        }
    }

    /// <summary>
    /// <b>FL, FR, RL, RR, without exception.</b> Every per-wheel array on every wire is indexed
    /// positionally in this order, and a channel that expanded any other way would put one side of
    /// the car where the other belongs on every chart that reads it.
    /// </summary>
    [Fact]
    public void Every_per_wheel_channel_expands_in_corner_order()
    {
        var suffixes = new[] { "Fl", "Fr", "Rl", "Rr" };

        var groups = RaceRoomChannels.All
            .Where(channel => suffixes.Any(suffix => channel.Name.EndsWith(suffix, StringComparison.Ordinal)))
            .GroupBy(channel => channel.Name[..^2])
            .Where(group => group.Count() == 4);

        groups.ShouldNotBeEmpty();

        foreach (var group in groups)
        {
            group.Select(channel => channel.Name[^2..])
                .ShouldBe(suffixes, $"'{group.Key}' should expand FL, FR, RL, RR");
        }
    }

    /// <summary>
    /// The tread expands inboard-first, per corner.
    /// </summary>
    /// <remarks>
    /// Inner, middle, outer — the tyre's own edges resolved by which side of the car it is fitted
    /// to, not RaceRoom's raw left and right, which swap across the car. Getting that backwards was
    /// a real bug that read as a camber problem on two corners and its opposite on the other two
    /// (#107); the connector resolves it, and this pins the shape the resolution writes into.
    /// </remarks>
    [Fact]
    public void The_tread_expands_inboard_first_for_every_corner()
    {
        RaceRoomChannels.All
            .Where(channel => channel.Name.StartsWith("tyreTemp", StringComparison.Ordinal))
            .Select(channel => channel.Column)
            .ShouldBe([
                "tyre_temp_fl_inner", "tyre_temp_fl_middle", "tyre_temp_fl_outer",
                "tyre_temp_fr_inner", "tyre_temp_fr_middle", "tyre_temp_fr_outer",
                "tyre_temp_rl_inner", "tyre_temp_rl_middle", "tyre_temp_rl_outer",
                "tyre_temp_rr_inner", "tyre_temp_rr_middle", "tyre_temp_rr_outer",
            ]);
    }

    /// <summary>
    /// Only the channels that genuinely cannot be absent are NOT NULL.
    /// </summary>
    /// <remarks>
    /// "Absent is not zero" is the platform's oldest rule, and a column declared NOT NULL is a claim
    /// that the simulator always reports it. The key columns and the handful of scalars that are
    /// always present make that claim; everything else must be free to say nothing.
    /// </remarks>
    [Fact]
    public void Only_the_channels_that_cannot_be_absent_are_not_null()
    {
        RaceRoomChannels.All
            .Where(channel => !channel.IsNullable)
            .Select(channel => channel.Column)
            .ShouldBe([
                "session_id", "timestamp", "sequence_number", "simulation_time",
                "lap_number", "sector", "speed", "steering", "engine_rpm", "fuel_left",
            ], ignoreOrder: true);
    }

    [Fact]
    public void Every_channel_belongs_to_a_group_and_every_group_can_be_looked_up()
    {
        foreach (var channel in RaceRoomChannels.All)
        {
            channel.Group.ShouldNotBeNullOrWhiteSpace();
            RaceRoomChannels.ByGroup.ShouldContainKey(channel.Group);
            RaceRoomChannels.ByGroup[channel.Group].ShouldContain(channel.Name);
        }

        // A group name and a channel name must not collide, or `?channels=` could not tell a request
        // for one from a request for the other.
        RaceRoomChannels.ByGroup.Keys.Intersect(RaceRoomChannels.ByName.Keys).ShouldBeEmpty();
    }

    /// <summary>
    /// The store types are the ones the writer knows how to write.
    /// </summary>
    /// <remarks>
    /// The generator throws on an unmapped type, so this is not the guard — it is the list, written
    /// down where a reader can see what the manifest is allowed to say. A new type here means a new
    /// case in the generator's <c>NpgsqlDbType</c> map and a decision about how it is written.
    /// </remarks>
    [Fact]
    public void Every_channel_uses_a_store_type_the_writer_knows()
    {
        RaceRoomChannels.All.Select(channel => channel.StoreType).Distinct().ShouldBeSubsetOf([
            "uuid", "timestamp with time zone", "bigint", "double precision",
            "integer", "real", "smallint", "boolean",
        ]);
    }
}
