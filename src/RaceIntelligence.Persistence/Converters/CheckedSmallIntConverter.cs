using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Persistence.Converters;

/// <summary>
/// EF Core value conversions narrowing a Core <see cref="int"/> to the <c>smallint</c> columns that
/// store raw, sim-supplied codes.
/// </summary>
/// <remarks>
/// The casts are <see langword="checked"/>. EF's implicit provider-type resolution — and an
/// unqualified <c>HasConversion&lt;short&gt;()</c> — narrow unchecked, which wraps silently: an
/// <see cref="int.MaxValue"/> rate code stored that way reads back as <c>-1</c>, which is exactly
/// RaceRoom's "not available" sentinel. A value that cannot survive the narrowing means the caller
/// sent something outside the range this column can represent, so it must fail loudly rather than
/// be recorded as a plausible-looking lie. Requests are range-checked at the ingest boundary and
/// rejected with 400 before they get here; these converters are the backstop for anything that
/// reaches the database by another path.
/// </remarks>
public static class CheckedSmallIntConverter
{
    /// <summary>The inclusive range of <see cref="int"/> values a <c>smallint</c> column can hold.</summary>
    public static bool IsRepresentable(int value) => value is >= short.MinValue and <= short.MaxValue;

    /// <inheritdoc cref="IsRepresentable(int)"/>
    public static bool IsRepresentable(int? value) => value is null || IsRepresentable(value.Value);

    /// <summary>Converts an <see cref="int"/> to/from its <c>smallint</c> storage representation.</summary>
    public static readonly ValueConverter<int, short> Converter = new(
        value => checked((short)value),
        value => value);

    /// <summary>Converts a <see cref="SessionType"/> to/from its <c>smallint</c> storage representation.</summary>
    /// <remarks>
    /// <see cref="SessionType"/> carries the simulator's own raw value reinterpreted through the
    /// enum's underlying <see cref="int"/>, so it is subject to the same narrowing hazard as the
    /// rate codes and is not constrained to the named members.
    /// </remarks>
    public static readonly ValueConverter<SessionType, short> SessionTypeConverter = new(
        value => checked((short)value),
        value => (SessionType)value);
}
