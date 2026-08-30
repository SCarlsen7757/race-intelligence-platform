using RaceIntelligence.RaceRoom.Channels;

namespace RaceIntelligence.RaceRoom.Telemetry;

/// <summary>
/// One instant of RaceRoom telemetry, every channel typed and named.
/// </summary>
/// <remarks>
/// <para>
/// The members are generated from <c>channels/raceroom-telemetry.channels</c>; this file exists to
/// say what the type is and what its conventions are, which no manifest can.
/// </para>
/// <para>
/// <b>Absent is not zero.</b> A nullable channel means the simulator did not report a value, and
/// that stays distinct from a real zero at every layer. RaceRoom signals it with <c>-1</c>, and the
/// connector is the only place that translation happens (ADR 0002 section 3) — by the time a sample
/// is one of these, every sentinel is already a <see langword="null"/>. Two channels break the rule
/// deliberately and say so where they are set: <see cref="DrsActivationsLeft"/>, where
/// <c>int.MaxValue</c> means endless rather than unavailable, and <see cref="Gear"/>, where
/// <c>-1</c> is reverse and only <c>-2</c> means unavailable.
/// </para>
/// <para>
/// Corner order is <b>FL, FR, RL, RR</b> throughout, and the tread's <c>Inner</c>/<c>Outer</c> are
/// the tyre's inboard and outboard edges resolved by which side of the car it is fitted to — not
/// RaceRoom's raw left and right, which are the tyre's own sides and therefore swap across the car
/// (#107).
/// </para>
/// </remarks>
[GeneratedFromChannels(ChannelArtifact.WireDto)]
public sealed partial record RaceRoomTelemetrySample;
