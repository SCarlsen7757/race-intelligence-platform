using MessagePack;

namespace RaceIntelligence.RaceRoom.Telemetry;

/// <summary>Which corner an operating window belongs to. FL, FR, RL, RR, as everywhere else.</summary>
public enum Corner
{
    FrontLeft = 0,
    FrontRight = 1,
    RearLeft = 2,
    RearRight = 3,
}

/// <summary>
/// The temperature band one corner is expected to work in — the tyre's and the brake's.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are constants, and that is why they are not on the sample.</b> Across 122,562 samples
/// of one recorded session, tyre <c>Optimal</c>/<c>Cold</c>/<c>Hot</c> and brake
/// <c>optimal</c>/<c>cold</c>/<c>hot</c> each had exactly one distinct value, while the reading they
/// bound had 119,146. Twenty-four unchanging numbers were being written on every row of a table
/// where rows arrive at 58 Hz.
/// </para>
/// <para>
/// <b>Keyed by compound as well as corner</b>, because the one thing that does change them is
/// fitting a different tyre. A window keyed only by session would be silently wrong from the first
/// stop that switched compound — which is precisely the stint a degradation question is about.
/// </para>
/// </remarks>
/// <param name="Corner">The corner these bounds describe.</param>
/// <param name="Compound">
/// RaceRoom's tyre subtype for the axle this corner is on (<c>Primary</c>, <c>Alternate</c>,
/// <c>Soft</c>, <c>Medium</c>, <c>Hard</c>), or <see langword="null"/> when the simulator reports
/// none. Raw and untranslated, like every other simulator code the platform stores.
/// </param>
/// <remarks>
/// One type on both wires. The ingest side rides it on every telemetry batch and the live side on
/// the slow-channel frame; a separate DTO per wire would be two restatements of eight numbers whose
/// only job is to travel together.
/// </remarks>
[MessagePackObject]
public sealed record OperatingWindow(
    [property: Key(0)] Corner Corner,
    [property: Key(1)] int? Compound,
    [property: Key(2)] float? TyreOptimalCelsius,
    [property: Key(3)] float? TyreColdCelsius,
    [property: Key(4)] float? TyreHotCelsius,
    [property: Key(5)] float? BrakeOptimalCelsius,
    [property: Key(6)] float? BrakeColdCelsius,
    [property: Key(7)] float? BrakeHotCelsius);
