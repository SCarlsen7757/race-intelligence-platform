using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RaceIntelligence.Core.Capabilities;

namespace RaceIntelligence.Persistence.Converters;

/// <summary>
/// EF Core value conversion for <see cref="SimCapabilities"/>, a <c>[Flags] ulong</c>, to/from a
/// <c>bigint</c> (signed 64-bit) column.
/// </summary>
/// <remarks>
/// Postgres has no native unsigned 64-bit integer type, so the flag set's bit pattern is
/// reinterpreted into a signed <see cref="long"/> via <see langword="unchecked"/> casts rather than
/// converted by value. This preserves every one of the 64 possible flag bits — including bit 63,
/// which would overflow a checked/value-preserving conversion to <see cref="long"/> — at the cost
/// of the stored integer looking negative once that top bit is ever used. Round-tripping
/// (<see cref="long"/> back to <see cref="ulong"/>) is exact either way, so this is purely a
/// storage/display detail, never a correctness concern.
/// </remarks>
public static class SimCapabilitiesConverter
{
    /// <summary>Converts <see cref="SimCapabilities"/> to/from its <c>bigint</c> storage representation.</summary>
    public static readonly ValueConverter<SimCapabilities, long> Converter = new(
        value => unchecked((long)value),
        value => unchecked((SimCapabilities)value));
}
