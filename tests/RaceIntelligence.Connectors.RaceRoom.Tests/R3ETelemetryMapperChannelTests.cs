using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.RaceRoom.Telemetry;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Covers the channels that used to travel inside a JSON <c>Extras</c> string and are now typed
/// fields on the sample.
/// </summary>
/// <remarks>
/// <para>
/// The old tests asserted the shape of a hand-written document: that its groups appeared exactly
/// once, that its per-wheel arrays had four entries in the right order, that a duplicated property
/// name had not crept in. None of those failures are expressible any more — the members are
/// generated from one manifest and the compiler checks the names.
/// </para>
/// <para>
/// What is still worth asserting is the part no generator can check: <b>which raw field each channel
/// reads, and what the connector does to it on the way</b>. Every value below is asymmetric across
/// the four corners, so a transposed index or a field wired to the wrong source cannot pass. And the
/// sentinel rule has moved here from the storage boundary, so the tests for it belong here too.
/// </para>
/// </remarks>
public class R3ETelemetryMapperChannelTests
{
    private static RaceRoomTelemetrySample MapSample(Action<R3ESharedRawBuilder>? configure = null)
    {
        var builder = new R3ESharedRawBuilder().InRaceSession("Channel Track", "Channel Layout");
        configure?.Invoke(builder);
        var raw = builder.Build();
        return R3ETelemetryMapper.ToSample(in raw, Guid.NewGuid(), sequenceNumber: 0, DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<OperatingWindow> MapWindows(Action<R3ESharedRawBuilder>? configure = null)
    {
        var builder = new R3ESharedRawBuilder().InRaceSession("Channel Track", "Channel Layout");
        configure?.Invoke(builder);
        var raw = builder.Build();
        return R3ETelemetryMapper.ToOperatingWindows(in raw);
    }

    [Fact]
    public void TheTyreChannelsADegradationModelNeedsAreReadInCornerOrder()
    {
        // None of these can be backfilled: they are only ever observed live, and raw telemetry is
        // never rewritten. Tyre grip especially — it is the one channel that measures grip loss
        // directly instead of inferring it from lap time.
        var sample = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.TireGrip[0] = 0.91f;
            raw.TireGrip[1] = 0.92f;
            raw.TireGrip[2] = 0.93f;
            raw.TireGrip[3] = 0.94f;

            raw.TireLoad[0] = 1100f;
            raw.TireLoad[1] = 1200f;
            raw.TireLoad[2] = 1300f;
            raw.TireLoad[3] = 1400f;

            raw.TireDirt[0] = 0.01f;
            raw.TireDirt[1] = 0.02f;
            raw.TireDirt[2] = 0.03f;
            raw.TireDirt[3] = 0.04f;

            raw.TireRps[0] = 51f;
            raw.TireRps[1] = 52f;
            raw.TireRps[2] = 53f;
            raw.TireRps[3] = 54f;

            raw.TireOnMtrl[0] = 1;
            raw.TireOnMtrl[1] = 2;
            raw.TireOnMtrl[2] = 3;
            raw.TireOnMtrl[3] = 4;
        }));

        new[] { sample.TyreGripFl, sample.TyreGripFr, sample.TyreGripRl, sample.TyreGripRr }
            .ShouldBe([0.91f, 0.92f, 0.93f, 0.94f]);
        new[] { sample.TyreLoadNewtonsFl, sample.TyreLoadNewtonsFr, sample.TyreLoadNewtonsRl, sample.TyreLoadNewtonsRr }
            .ShouldBe([1100f, 1200f, 1300f, 1400f]);
        new[] { sample.TyreDirtFl, sample.TyreDirtFr, sample.TyreDirtRl, sample.TyreDirtRr }
            .ShouldBe([0.01f, 0.02f, 0.03f, 0.04f]);
        new[]
        {
            sample.TyreRotationRadiansPerSecondFl, sample.TyreRotationRadiansPerSecondFr,
            sample.TyreRotationRadiansPerSecondRl, sample.TyreRotationRadiansPerSecondRr,
        }.ShouldBe([51f, 52f, 53f, 54f]);
        new short?[]
        {
            sample.TyreSurfaceMaterialFl, sample.TyreSurfaceMaterialFr,
            sample.TyreSurfaceMaterialRl, sample.TyreSurfaceMaterialRr,
        }.ShouldBe([(short)1, (short)2, (short)3, (short)4]);
    }

    /// <summary>
    /// The sentinel rule, now applied here rather than three layers downstream. This is the whole
    /// argument for moving it: the connector is the only component that knows what RaceRoom means by
    /// a negative number, and the builder's default state is exactly the "nothing reported" case.
    /// </summary>
    [Fact]
    public void UnreportedTyreChannelsArriveAsNullRatherThanMinusOne()
    {
        var sample = MapSample();

        sample.TyreGripFl.ShouldBeNull();
        sample.TyreLoadNewtonsFl.ShouldBeNull();
        sample.TyreDirtFl.ShouldBeNull();

        // Surface material is deliberately not in this list. Its zero is tarmac — a real answer —
        // so the builder's default is a reported value rather than an unreported one, and only a
        // negative would be the sentinel.
        sample.TyreSurfaceMaterialFl.ShouldBe((short)0);
    }

    /// <summary>
    /// <c>TireFlatspot</c> is documented as an <see cref="int"/> tri-state — <c>-1</c> N/A, <c>0</c>
    /// false, <c>1</c> true — not a float. Stored as one it invited a "how flat-spotted is it"
    /// reading the simulator never offered.
    /// </summary>
    [Fact]
    public void FlatspotIsATriStateAndItsUnavailableValueIsNull()
    {
        var sample = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.TireFlatspot[0] = -1;
            raw.TireFlatspot[1] = 0;
            raw.TireFlatspot[2] = 1;
            raw.TireFlatspot[3] = 1;
        }));

        sample.TyreFlatspotFl.ShouldBeNull();
        sample.TyreFlatspotFr.ShouldBe((short)0);
        sample.TyreFlatspotRl.ShouldBe((short)1);
        sample.TyreFlatspotRr.ShouldBe((short)1);
    }

    /// <summary>
    /// Wheel speed arrives negative for a car driving forwards, measured: 120,918 samples of one
    /// recorded session were negative and none positive, with the magnitude tracking road speed.
    /// Left as it comes, wheel slip is uncomputable — subtracting road speed from a negative wheel
    /// speed gives about −113 m/s where the answer is +0.9.
    /// </summary>
    [Fact]
    public void WheelSpeedIsNormalisedSoPositiveIsForward()
    {
        var sample = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.CarSpeed = 56.1f;
            raw.TireSpeed[0] = -57.0f;
            raw.TireSpeed[1] = -57.1f;
            raw.TireSpeed[2] = -56.8f;
            raw.TireSpeed[3] = -56.9f;
        }));

        sample.WheelSpeedFl.ShouldBe(57.0f);
        sample.WheelSpeedFr.ShouldBe(57.1f);
        sample.WheelSpeedRl.ShouldBe(56.8f);
        sample.WheelSpeedRr.ShouldBe(56.9f);

        // The point of the exercise: slip is now a small number rather than a nonsensical one.
        (sample.WheelSpeedFl!.Value - sample.Speed).ShouldBeInRange(-2f, 2f);
    }

    [Fact]
    public void BrakeTemperatureReadsTheCurrentTempAndLeavesTheWindowToItsOwnTable()
    {
        var sample = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.BrakeTemp[0].CurrentTemp = 101f;
            raw.BrakeTemp[1].CurrentTemp = 202f;
            raw.BrakeTemp[2].CurrentTemp = 303f;
            raw.BrakeTemp[3].CurrentTemp = 404f;

            raw.BrakePressure[0] = 1f;
            raw.BrakePressure[1] = 2f;
            raw.BrakePressure[2] = 3f;
            raw.BrakePressure[3] = 4f;
        }));

        new[] { sample.BrakeTempFl, sample.BrakeTempFr, sample.BrakeTempRl, sample.BrakeTempRr }
            .ShouldBe([101f, 202f, 303f, 404f]);

        // Brake pressure is on every sample, not on the slow channel: it changes as fast as the
        // pedal does, and a braking event lasts about a second.
        new[] { sample.BrakePressureFl, sample.BrakePressureFr, sample.BrakePressureRl, sample.BrakePressureRr }
            .ShouldBe([1f, 2f, 3f, 4f]);
    }

    [Fact]
    public void BrakeOperatingWindowsCarryTheBandBesideTheReading()
    {
        // 380 °C is cold on one car and cooking on another, and the simulator is willing to say
        // which. The front left's hot bound is the sentinel: a pad the simulator named no ceiling
        // for must not arrive as one at -1 degrees.
        var windows = MapWindows(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.BrakeTemp[0].OptimalTemp = 410f;
            raw.BrakeTemp[1].OptimalTemp = 420f;
            raw.BrakeTemp[2].OptimalTemp = 430f;
            raw.BrakeTemp[3].OptimalTemp = 440f;
            raw.BrakeTemp[0].ColdTemp = 201f;
            raw.BrakeTemp[0].HotTemp = -1f;
        }));

        windows.Select(w => w.BrakeOptimalCelsius).ShouldBe([410f, 420f, 430f, 440f]);
        windows[0].BrakeColdCelsius.ShouldBe(201f);
        windows[0].BrakeHotCelsius.ShouldBeNull();
    }

    [Fact]
    public void OperatingWindowsTakeTheCompoundFromTheAxleTheCornerIsOn()
    {
        var windows = MapWindows(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.TireSubtypeFront = 2;
            raw.TireSubtypeRear = 4;
        }));

        windows[(int)Corner.FrontLeft].Compound.ShouldBe(2);
        windows[(int)Corner.FrontRight].Compound.ShouldBe(2);
        windows[(int)Corner.RearLeft].Compound.ShouldBe(4);
        windows[(int)Corner.RearRight].Compound.ShouldBe(4);
    }

    [Fact]
    public void OperatingWindowsComeBackOnePerCornerInCornerOrder()
    {
        MapWindows().Select(w => w.Corner)
            .ShouldBe([Corner.FrontLeft, Corner.FrontRight, Corner.RearLeft, Corner.RearRight]);
    }

    [Fact]
    public void IncidentPointsAndTheServerLimitAreCarriedSeparately()
    {
        var sample = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.IncidentPoints = 7;
            raw.MaxIncidentPoints = 20;
        }));

        sample.IncidentPoints.ShouldBe(7);
        sample.MaxIncidentPoints.ShouldBe(20);
    }

    /// <summary>
    /// A zero incident count is a real answer — a clean driver — where an unreported one is not. The
    /// old document kept both as the same <c>-1</c>-or-a-number and left the distinction to whoever
    /// read it; here they are different values of different kinds.
    /// </summary>
    [Fact]
    public void AZeroIncidentCountIsARealAnswerAndAnUnreportedOneIsNull()
    {
        MapSample(builder => builder.Configure((ref R3ESharedRaw raw) => raw.IncidentPoints = 0))
            .IncidentPoints.ShouldBe(0);

        // The builder's default is the sentinel: offline, or a server that sets no limit.
        MapSample().MaxIncidentPoints.ShouldBeNull();
    }

    [Fact]
    public void CutTrackWarningsAreCarriedAndTheirUnavailableValueIsNull()
    {
        MapSample(builder => builder.Configure((ref R3ESharedRaw raw) => raw.CutTrackWarnings = 3))
            .CutTrackWarnings.ShouldBe(3);

        MapSample().CutTrackWarnings.ShouldBeNull();
    }

    [Fact]
    public void TheFormerlyNestedGroupsAreFlatChannelsReadingTheRawFieldsTheyName()
    {
        var sample = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.PushToPass.Available = 1;
            raw.PushToPass.Engaged = 0;
            raw.PushToPass.AmountLeft = 4;
            raw.PushToPass.EngagedTimeLeft = 2.5f;
            raw.PushToPass.WaitTimeLeft = 30f;

            raw.CarDamage.Engine = 0.5f;
            raw.CarDamage.Transmission = 0.75f;
            raw.CarDamage.Aerodynamics = 0.25f;
            raw.CarDamage.Suspension = 1f;

            raw.Flags.Yellow = 1;
            raw.Flags.Blue = 0;

            raw.PitWindowStatus = 2;
            raw.PitWindowStart = 12;
            raw.PitWindowEnd = 20;
        }));

        sample.PushToPassAvailable.ShouldBe(1);
        sample.PushToPassEngaged.ShouldBe(0);
        sample.PushToPassAmountLeft.ShouldBe(4);
        sample.PushToPassEngagedTimeLeftSeconds.ShouldBe(2.5f);
        sample.PushToPassWaitTimeLeftSeconds.ShouldBe(30f);

        sample.DamageEngine.ShouldBe(0.5f);
        sample.DamageTransmission.ShouldBe(0.75f);
        sample.DamageAerodynamics.ShouldBe(0.25f);
        sample.DamageSuspension.ShouldBe(1f);

        sample.FlagYellow.ShouldBe((short)1);
        sample.FlagBlue.ShouldBe((short)0);

        sample.PitWindowStatus.ShouldBe((short)2);
        sample.PitWindowStart.ShouldBe(12);
        sample.PitWindowEnd.ShouldBe(20);
    }

    /// <summary>
    /// The one channel where <c>-1</c> is not the only special value. The official layout documents
    /// <c>int32::max</c> on <c>numActivationsLeft</c> as <i>unlimited</i>, so it must not go through
    /// the <c>-1</c> rule and arrive as a confident 2,147,483,647.
    /// </summary>
    [Fact]
    public void EndlessDrsActivationsAreNotACount()
    {
        var endless = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
            raw.Drs.NumActivationsLeft = int.MaxValue));

        endless.DrsActivationsLeft.ShouldBeNull();
        endless.DrsActivationsUnlimited.ShouldBe(true);
    }

    [Fact]
    public void ACountedDrsAllowanceIsCarriedAsACountAndAnUnreportedOneIsNull()
    {
        var counted = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
            raw.Drs.NumActivationsLeft = 3));

        counted.DrsActivationsLeft.ShouldBe(3);
        counted.DrsActivationsUnlimited.ShouldBe(false);

        var unavailable = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
            raw.Drs.NumActivationsLeft = -1));

        unavailable.DrsActivationsLeft.ShouldBeNull();
        unavailable.DrsActivationsUnlimited.ShouldBeNull();
    }

    /// <summary>
    /// <c>LocalAcceleration</c> is m/s² "from car center, +X=left, +Y=up, +Z=back". Longitudinal is
    /// therefore −Z and lateral is −X, and a traction circle drawn on the raw axes is wrong in both
    /// — wrong in a way that looks plausible, which is why it is corrected once here.
    /// </summary>
    [Fact]
    public void LocalAccelerationIsCorrectedFromRaceRoomsAxesToTheConventionalOnes()
    {
        var sample = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            // Braking hard (backwards acceleration, so +Z) while turning right (so the car
            // accelerates left, +X).
            raw.Player.LocalAcceleration.X = 9.0;
            raw.Player.LocalAcceleration.Y = 1.5;
            raw.Player.LocalAcceleration.Z = 12.0;
        }));

        sample.AccelerationLongitudinal.ShouldBe(-12.0f);
        sample.AccelerationLateral.ShouldBe(-9.0f);
        sample.AccelerationVertical.ShouldBe(1.5f);
    }

    /// <summary>
    /// The struct these come from has been declared in this project since the connector was written
    /// and nothing read it until #109 — an entire block of what RaceRoom exposes, described and left
    /// on the floor (#104).
    /// </summary>
    [Fact]
    public void TheVehicleDynamicsChannelsAreReadAtAll()
    {
        var sample = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.Player.Position.X = 1234.5;
            raw.Player.Position.Y = 67.25;
            raw.Player.Position.Z = -890.75;

            raw.Player.Camber[0] = -0.06;
            raw.Player.Camber[1] = -0.05;
            raw.Player.Camber[2] = -0.04;
            raw.Player.Camber[3] = -0.03;

            raw.Player.RideHeight[0] = 0.041;
            raw.Player.SuspensionVelocity[0] = 0.12;
            raw.Player.CurrentDownforce = 5400.0;
            raw.Player.EngineTorque = 480.0;
        }));

        sample.WorldPositionX.ShouldBe(1234.5);
        sample.WorldPositionY.ShouldBe(67.25);
        sample.WorldPositionZ.ShouldBe(-890.75);

        // Negative camber is the normal setup, so this channel must not be sentinel-filtered.
        new[] { sample.CamberFl, sample.CamberFr, sample.CamberRl, sample.CamberRr }
            .ShouldBe([-0.06f, -0.05f, -0.04f, -0.03f]);

        sample.RideHeightFl.ShouldBe(0.041f);
        sample.SuspensionVelocityFl.ShouldBe(0.12f);
        sample.DownforceNewtons.ShouldBe(5400f);
        sample.EngineTorqueNewtonMetres.ShouldBe(480f);
    }

    /// <summary>
    /// A downforce of zero and a downforce nobody reported are different facts, and only one of them
    /// means the wing fell off.
    /// </summary>
    [Fact]
    public void AnUnreportedDownforceIsNullRatherThanZero()
    {
        MapSample(builder => builder.Configure((ref R3ESharedRaw raw) => raw.Player.CurrentDownforce = -1.0))
            .DownforceNewtons.ShouldBeNull();

        MapSample(builder => builder.Configure((ref R3ESharedRaw raw) => raw.Player.CurrentDownforce = 0.0))
            .DownforceNewtons.ShouldBe(0f);
    }

    /// <summary>
    /// Six channels the connector has always populated, the live wire has always carried, and the
    /// archive silently dropped: the old wire DTO had no members for them, so they reached a race
    /// engineer's screen and never reached a database (#109).
    /// </summary>
    [Fact]
    public void TheAidChannelsTheArchiveUsedToLoseAreOnTheSample()
    {
        var sample = MapSample(builder => builder.Configure((ref R3ESharedRaw raw) =>
        {
            raw.AbsSetting = 3;
            raw.AidSettings.Abs = 5;
            raw.TractionControlSetting = 2;
            raw.AidSettings.Tc = 1;
            raw.TractionControlPercent = 12.5f;
            raw.BrakeBias = 0.56f;
        }));

        sample.AbsSetting.ShouldBe(3);
        // aid_settings uses 5 to mean "the aid just intervened", which is a different question from
        // what it is set to.
        sample.AbsActive.ShouldBe(true);
        sample.TractionControlSetting.ShouldBe(2);
        sample.TractionControlActive.ShouldBe(false);
        sample.TractionControlPercent.ShouldBe(12.5f);
        sample.BrakeBias.ShouldBe(0.56f);
    }
}
