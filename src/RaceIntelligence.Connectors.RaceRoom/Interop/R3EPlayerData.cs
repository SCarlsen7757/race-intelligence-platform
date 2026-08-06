using System.Runtime.InteropServices;

namespace RaceIntelligence.Connectors.RaceRoom.Interop;

/// <summary>
/// High-precision data for the player's own vehicle only (<c>r3e_playerdata</c>). Not populated
/// for AI, remote, or replay-controlled cars.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct R3EPlayerData
{
    /// <summary>Player user id.</summary>
    public int UserId;

    /// <summary>Virtual physics time. Unit: ticks (1 tick = 1/400th of a second).</summary>
    public int GameSimulationTicks;

    /// <summary>Virtual physics time. Unit: seconds.</summary>
    public double GameSimulationTime;

    /// <summary>Car world-space position.</summary>
    public R3EVec3F64 Position;

    /// <summary>Car world-space velocity. Unit: m/s.</summary>
    public R3EVec3F64 Velocity;

    /// <summary>Car local-space velocity. Unit: m/s.</summary>
    public R3EVec3F64 LocalVelocity;

    /// <summary>Car world-space acceleration. Unit: m/s^2.</summary>
    public R3EVec3F64 Acceleration;

    /// <summary>Car local-space acceleration. Unit: m/s^2.</summary>
    public R3EVec3F64 LocalAcceleration;

    /// <summary>Car body orientation. Unit: Euler angles.</summary>
    public R3EVec3F64 Orientation;

    /// <summary>Car body rotation.</summary>
    public R3EVec3F64 Rotation;

    /// <summary>Car body angular acceleration (torque divided by inertia).</summary>
    public R3EVec3F64 AngularAcceleration;

    /// <summary>Car world-space angular velocity. Unit: rad/s.</summary>
    public R3EVec3F64 AngularVelocity;

    /// <summary>Car local-space angular velocity. Unit: rad/s.</summary>
    public R3EVec3F64 LocalAngularVelocity;

    /// <summary>Driver g-force local to car.</summary>
    public R3EVec3F64 LocalGforce;

    /// <summary>Total steering force coming through the steering bars.</summary>
    public double SteeringForce;

    /// <summary>Total steering force, as a percentage.</summary>
    public double SteeringForcePercentage;

    /// <summary>Current engine torque.</summary>
    public double EngineTorque;

    /// <summary>Current downforce. Unit: Newtons (N).</summary>
    public double CurrentDownforce;

    /// <summary>Reserved/currently unused by the game.</summary>
    public double Voltage;

    /// <summary>Reserved/currently unused by the game.</summary>
    public double ErsLevel;

    /// <summary>Reserved/currently unused by the game.</summary>
    public double PowerMguH;

    /// <summary>Reserved/currently unused by the game.</summary>
    public double PowerMguK;

    /// <summary>Reserved/currently unused by the game.</summary>
    public double TorqueMguK;

    /// <summary>Suspension deflection per tyre (FL, FR, RL, RR). Unit: meters.</summary>
    public Double4 SuspensionDeflection;

    /// <summary>Suspension velocity per tyre (FL, FR, RL, RR). Unit: meters per second.</summary>
    public Double4 SuspensionVelocity;

    /// <summary>Camber per tyre (FL, FR, RL, RR). Unit: radians.</summary>
    public Double4 Camber;

    /// <summary>Ride height per tyre (FL, FR, RL, RR). Unit: meters.</summary>
    public Double4 RideHeight;

    public double FrontWingHeight;
    public double FrontRollAngle;
    public double RearRollAngle;
    public double ThirdSpringSuspensionDeflectionFront;
    public double ThirdSpringSuspensionVelocityFront;
    public double ThirdSpringSuspensionDeflectionRear;
    public double ThirdSpringSuspensionVelocityRear;

    public double Unused1;

    public double Unused2;

    public double Unused3;
}
