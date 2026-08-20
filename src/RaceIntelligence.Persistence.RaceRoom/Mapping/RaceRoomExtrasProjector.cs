using System.Text.Json;
using System.Text.Json.Nodes;

namespace RaceIntelligence.Persistence.RaceRoom.Mapping;

/// <summary>
/// Projects the RaceRoom-specific values worth querying out of a sample's extras document and
/// leaves the unpromoted remainder ready for the <c>extras</c> jsonb column.
/// </summary>
/// <remarks>
/// This is deliberately owned by the RaceRoom store rather than Persistence.Core: the wire stays
/// canonical telemetry plus raw simulator extras, while each simulator decides which of its own
/// values deserve typed storage. Simulator negative sentinels become <see langword="null"/> only
/// at this storage boundary; a real zero remains zero.
/// </remarks>
public static class RaceRoomExtrasProjector
{
    private static readonly string[] PushToPassLeaves =
    [
        "available",
        "engaged",
        "amountLeft",
        "engagedTimeLeftSeconds",
        "waitTimeLeftSeconds",
    ];

    private static readonly string[] DamageLeaves =
    [
        "engine",
        "transmission",
        "aerodynamics",
        "suspension",
    ];

    /// <summary>Parses and projects one extras document.</summary>
    public static RaceRoomExtrasProjection Project(string extras)
    {
        ArgumentNullException.ThrowIfNull(extras);

        var root = JsonNode.Parse(extras);
        if (root is not JsonObject document)
        {
            // Valid JSON need not be an object. There are no known leaves to promote from such a
            // value, but preserving it keeps the open-ended extras contract intact.
            return RaceRoomExtrasProjection.Empty(extras);
        }

        var pushToPass = document["pushToPass"] as JsonObject;
        var damage = document["damage"] as JsonObject;

        var projection = new RaceRoomExtrasProjection(
            PushToPassAvailable: NonNegativeInt(pushToPass?["available"]),
            PushToPassEngaged: NonNegativeInt(pushToPass?["engaged"]),
            PushToPassAmountLeft: NonNegativeInt(pushToPass?["amountLeft"]),
            PushToPassEngagedTimeLeftSeconds: NonNegativeFloat(pushToPass?["engagedTimeLeftSeconds"]),
            PushToPassWaitTimeLeftSeconds: NonNegativeFloat(pushToPass?["waitTimeLeftSeconds"]),
            TyreSubtypeFront: NonNegativeInt(document["tireSubtypeFront"]),
            TyreSubtypeRear: NonNegativeInt(document["tireSubtypeRear"]),
            CutTrackWarnings: NonNegativeInt(document["cutTrackWarnings"]),
            DamageEngine: NonNegativeFloat(damage?["engine"]),
            DamageTransmission: NonNegativeFloat(damage?["transmission"]),
            DamageAerodynamics: NonNegativeFloat(damage?["aerodynamics"]),
            DamageSuspension: NonNegativeFloat(damage?["suspension"]),
            Extras: string.Empty);

        RemoveLeaves(document, "pushToPass", pushToPass, PushToPassLeaves);
        document.Remove("tireSubtypeFront");
        document.Remove("tireSubtypeRear");
        document.Remove("cutTrackWarnings");
        RemoveLeaves(document, "damage", damage, DamageLeaves);

        return projection with { Extras = document.ToJsonString() };
    }

    private static void RemoveLeaves(
        JsonObject document,
        string objectName,
        JsonObject? nested,
        IEnumerable<string> leaves)
    {
        if (nested is null)
        {
            // A malformed known container is still otherwise-valid unrecognized JSON. It cannot
            // yield a typed value, but it must not make a batch fail or be discarded.
            return;
        }

        foreach (var leaf in leaves)
        {
            nested.Remove(leaf);
        }

        if (nested.Count == 0)
        {
            document.Remove(objectName);
        }
    }

    private static int? NonNegativeInt(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) && number >= 0
            ? number
            : null;

    private static float? NonNegativeFloat(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<float>(out var number) && number >= 0
            ? number
            : null;
}

/// <summary>The typed RaceRoom values plus the unpromoted extras JSON from one sample.</summary>
public sealed record RaceRoomExtrasProjection(
    int? PushToPassAvailable,
    int? PushToPassEngaged,
    int? PushToPassAmountLeft,
    float? PushToPassEngagedTimeLeftSeconds,
    float? PushToPassWaitTimeLeftSeconds,
    int? TyreSubtypeFront,
    int? TyreSubtypeRear,
    int? CutTrackWarnings,
    float? DamageEngine,
    float? DamageTransmission,
    float? DamageAerodynamics,
    float? DamageSuspension,
    string Extras)
{
    internal static RaceRoomExtrasProjection Empty(string extras) =>
        new(null, null, null, null, null, null, null, null, null, null, null, null, extras);
}
