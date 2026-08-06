namespace RaceIntelligence.Core.Telemetry;

/// <summary>
/// Identifies one of the four wheel/tyre positions on a car.
/// </summary>
/// <remarks>
/// The numeric values happen to match RaceRoom's per-tyre array ordering, which that connector may
/// exploit for a cheap mapping. It is a coincidence, not a contract: every other connector must map
/// its own source ordering explicitly.
/// </remarks>
public enum WheelPosition
{
    FrontLeft = 0,
    FrontRight = 1,
    RearLeft = 2,
    RearRight = 3,
}

/// <summary>
/// A value of type <typeparamref name="T"/> for each of the four wheel positions.
/// </summary>
/// <remarks>
/// One generic struct rather than four parallel fields repeated on every per-wheel measurement
/// (speed, suspension travel, tyre temperature, pressure, wear, ...).
/// </remarks>
/// <typeparam name="T">The per-wheel value type.</typeparam>
public readonly record struct WheelData<T>(T FrontLeft, T FrontRight, T RearLeft, T RearRight)
{
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
