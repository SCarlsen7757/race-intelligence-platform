using System.Text.Json.Nodes;
using RaceIntelligence.Persistence.RaceRoom.Mapping;
using Shouldly;

namespace RaceIntelligence.Persistence.RaceRoom.Tests;

public sealed class RaceRoomExtrasProjectorTests
{
    [Fact]
    public void Projects_every_promoted_value_and_removes_only_known_leaves()
    {
        const string extras =
            """
            {
              "pushToPass": {
                "available": 1,
                "engaged": 0,
                "amountLeft": 7,
                "engagedTimeLeftSeconds": 2.5,
                "waitTimeLeftSeconds": 9.25,
                "mode": "attack"
              },
              "tireSubtypeFront": 2,
              "tireSubtypeRear": 3,
              "cutTrackWarnings": 4,
              "damage": {
                "engine": 0.75,
                "transmission": 0,
                "aerodynamics": 0.5,
                "suspension": 1,
                "source": "shared-memory"
              },
              "unknown": { "nested": true }
            }
            """;

        var projected = RaceRoomExtrasProjector.Project(extras);

        projected.PushToPassAvailable.ShouldBe(1);
        projected.PushToPassEngaged.ShouldBe(0);
        projected.PushToPassAmountLeft.ShouldBe(7);
        projected.PushToPassEngagedTimeLeftSeconds.ShouldBe(2.5f);
        projected.PushToPassWaitTimeLeftSeconds.ShouldBe(9.25f);
        projected.TyreSubtypeFront.ShouldBe(2);
        projected.TyreSubtypeRear.ShouldBe(3);
        projected.CutTrackWarnings.ShouldBe(4);
        projected.DamageEngine.ShouldBe(0.75f);
        projected.DamageTransmission.ShouldBe(0f);
        projected.DamageAerodynamics.ShouldBe(0.5f);
        projected.DamageSuspension.ShouldBe(1f);

        JsonNode.DeepEquals(
            JsonNode.Parse(projected.Extras),
            JsonNode.Parse(
                """
                {
                  "pushToPass": { "mode": "attack" },
                  "damage": { "source": "shared-memory" },
                  "unknown": { "nested": true }
                }
                """)).ShouldBeTrue();
    }

    [Fact]
    public void Negative_sentinels_become_null_while_real_zero_is_preserved()
    {
        var projected = RaceRoomExtrasProjector.Project(
            """
            {
              "pushToPass": {
                "available": -1,
                "engaged": 0,
                "amountLeft": -1,
                "engagedTimeLeftSeconds": -1.0,
                "waitTimeLeftSeconds": 0.0
              },
              "tireSubtypeFront": -1,
              "tireSubtypeRear": 0,
              "cutTrackWarnings": -1,
              "damage": {
                "engine": -1.0,
                "transmission": 0.0,
                "aerodynamics": -0.25,
                "suspension": 0.0
              }
            }
            """);

        projected.PushToPassAvailable.ShouldBeNull();
        projected.PushToPassEngaged.ShouldBe(0);
        projected.PushToPassAmountLeft.ShouldBeNull();
        projected.PushToPassEngagedTimeLeftSeconds.ShouldBeNull();
        projected.PushToPassWaitTimeLeftSeconds.ShouldBe(0f);
        projected.TyreSubtypeFront.ShouldBeNull();
        projected.TyreSubtypeRear.ShouldBe(0);
        projected.CutTrackWarnings.ShouldBeNull();
        projected.DamageEngine.ShouldBeNull();
        projected.DamageTransmission.ShouldBe(0f);
        projected.DamageAerodynamics.ShouldBeNull();
        projected.DamageSuspension.ShouldBe(0f);
        projected.Extras.ShouldBe("{}");
    }

    [Fact]
    public void Missing_and_incorrectly_typed_values_are_null_without_losing_other_json()
    {
        var projected = RaceRoomExtrasProjector.Project(
            """
            {
              "pushToPass": "not-an-object",
              "tireSubtypeFront": true,
              "damage": [0.2],
              "unknown": [1, null, "three"]
            }
            """);

        projected.PushToPassAvailable.ShouldBeNull();
        projected.TyreSubtypeFront.ShouldBeNull();
        projected.TyreSubtypeRear.ShouldBeNull();
        projected.CutTrackWarnings.ShouldBeNull();
        projected.DamageEngine.ShouldBeNull();

        JsonNode.DeepEquals(
            JsonNode.Parse(projected.Extras),
            JsonNode.Parse(
                """
                {
                  "pushToPass": "not-an-object",
                  "damage": [0.2],
                  "unknown": [1, null, "three"]
                }
                """)).ShouldBeTrue();
    }

    [Fact]
    public void Incorrectly_typed_known_leaves_are_removed_while_unknown_siblings_survive()
    {
        var projected = RaceRoomExtrasProjector.Project(
            """
            {
              "pushToPass": { "available": "yes", "future": 12 },
              "damage": { "engine": true, "future": { "value": 0.4 } },
              "cutTrackWarnings": 1.5
            }
            """);

        projected.PushToPassAvailable.ShouldBeNull();
        projected.DamageEngine.ShouldBeNull();
        projected.CutTrackWarnings.ShouldBeNull();
        JsonNode.DeepEquals(
            JsonNode.Parse(projected.Extras),
            JsonNode.Parse(
                """
                {
                  "pushToPass": { "future": 12 },
                  "damage": { "future": { "value": 0.4 } }
                }
                """)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"future simulator shape\"")]
    [InlineData("42")]
    public void Valid_non_object_json_is_preserved(string extras)
    {
        var projected = RaceRoomExtrasProjector.Project(extras);

        projected.Extras.ShouldBe(extras);
        projected.PushToPassAvailable.ShouldBeNull();
        projected.PushToPassEngaged.ShouldBeNull();
        projected.PushToPassAmountLeft.ShouldBeNull();
        projected.PushToPassEngagedTimeLeftSeconds.ShouldBeNull();
        projected.PushToPassWaitTimeLeftSeconds.ShouldBeNull();
        projected.TyreSubtypeFront.ShouldBeNull();
        projected.TyreSubtypeRear.ShouldBeNull();
        projected.CutTrackWarnings.ShouldBeNull();
        projected.DamageEngine.ShouldBeNull();
        projected.DamageTransmission.ShouldBeNull();
        projected.DamageAerodynamics.ShouldBeNull();
        projected.DamageSuspension.ShouldBeNull();
    }
}
