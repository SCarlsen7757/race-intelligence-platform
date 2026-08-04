namespace RaceIntelligence.Core.Telemetry;

/// <summary>
/// Identifies one of the four wheel/tyre positions on a car.
/// </summary>
/// <remarks>
/// The numeric values (0=FrontLeft, 1=FrontRight, 2=RearLeft, 3=RearRight) happen to match the
/// array ordering RaceRoom's shared memory uses for its per-tyre arrays. That is a coincidence
/// the RaceRoom connector is free to rely on for a cheap array-to-struct mapping — it is not a
/// contract of this enum. Other connectors must map their own source ordering explicitly and
/// must not assume any particular simulator's array layout.
/// </remarks>
public enum WheelPosition
{
    /// <summary>Front-left wheel.</summary>
    FrontLeft = 0,

    /// <summary>Front-right wheel.</summary>
    FrontRight = 1,

    /// <summary>Rear-left wheel.</summary>
    RearLeft = 2,

    /// <summary>Rear-right wheel.</summary>
    RearRight = 3,
}

/// <summary>
/// A value of type <typeparamref name="T"/> for each of the four wheel positions.
/// </summary>
/// <remarks>
/// Used throughout the canonical telemetry model wherever a simulator reports a per-wheel
/// quantity (speed, suspension travel, tyre temperature, pressure, wear, ...). Keeping this as a
/// single generic struct avoids four parallel fields being repeated on every such measurement.
/// </remarks>
/// <typeparam name="T">The per-wheel value type.</typeparam>
/// <param name="FrontLeft">Value at the front-left wheel.</param>
/// <param name="FrontRight">Value at the front-right wheel.</param>
/// <param name="RearLeft">Value at the rear-left wheel.</param>
/// <param name="RearRight">Value at the rear-right wheel.</param>
public readonly record struct WheelData<T>(T FrontLeft, T FrontRight, T RearLeft, T RearRight)
{
    /// <summary>Gets the value for the given wheel position.</summary>
    /// <param name="pos">The wheel position to read.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pos"/> is not a defined <see cref="WheelPosition"/>.</exception>
    public T this[WheelPosition pos] => pos switch
    {
        WheelPosition.FrontLeft => FrontLeft,
        WheelPosition.FrontRight => FrontRight,
        WheelPosition.RearLeft => RearLeft,
        WheelPosition.RearRight => RearRight,
        _ => throw new ArgumentOutOfRangeException(nameof(pos), pos, "Unknown wheel position."),
    };
}

/// <summary>
/// Factory helpers for constructing <see cref="WheelData{T}"/> instances.
/// </summary>
public static class WheelData
{
    /// <summary>
    /// Builds a <see cref="WheelData{T}"/> from a 4-element span, mapping elements in
    /// front-left, front-right, rear-left, rear-right order.
    /// </summary>
    /// <param name="source">A span containing exactly 4 elements, ordered FL, FR, RL, RR.</param>
    /// <exception cref="ArgumentException"><paramref name="source"/> does not have exactly 4 elements.</exception>
    public static WheelData<T> From<T>(ReadOnlySpan<T> source)
    {
        if (source.Length != 4)
        {
            throw new ArgumentException($"Expected exactly 4 elements (FL, FR, RL, RR), got {source.Length}.", nameof(source));
        }

        return new WheelData<T>(source[0], source[1], source[2], source[3]);
    }
}
