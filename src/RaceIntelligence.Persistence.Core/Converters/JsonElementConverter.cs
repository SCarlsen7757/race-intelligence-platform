using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RaceIntelligence.Persistence.Core.Converters;

/// <summary>
/// Shared EF Core value conversion for <see cref="JsonElement"/>-shaped columns (jsonb).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a converter alone is not enough.</b> <see cref="JsonElement"/> is a struct that wraps a
/// reference to its parent <see cref="JsonDocument"/>; it has no value-based <see cref="object.Equals(object?)"/>
/// override, so it falls back to reference/bitwise struct equality. Every time EF Core materializes
/// an entity it round-trips the JSON column through <see cref="Deserialize"/>, producing a brand
/// new <see cref="JsonElement"/> instance. Without an explicit structural
/// <see cref="ValueComparer{T}"/>, the change tracker's default comparer sees "old instance" versus
/// "new instance" and considers the property changed even when nothing was touched — every entity
/// with a jsonb column would look permanently modified, and every <c>SaveChanges</c> would emit a
/// spurious <c>UPDATE</c>. Supplying a comparer that compares by <see cref="JsonElement.GetRawText"/>
/// fixes this. This is exercised directly by the regression test that loads an entity, changes
/// nothing, and asserts <c>ChangeTracker.HasChanges()</c> is <see langword="false"/>.
/// </para>
/// <para>
/// Both a non-nullable and a nullable variant are provided so all four jsonb columns in the schema
/// (<c>sessions.weather</c>, <c>sessions.setup</c>, <c>sessions.extras</c>,
/// <c>telemetry_samples.tyre_temperature</c>/<c>extras</c>) can reuse the same serialization and
/// comparison logic instead of four independent, potentially-divergent implementations.
/// </para>
/// <para>
/// Equality and hashing compare <see cref="JsonElement.GetRawText"/> rather than performing a
/// semantic deep-compare (e.g. treating <c>{"a":1,"b":2}</c> and <c>{"b":2,"a":1}</c> as equal).
/// This is intentional and cheap: values only ever reach the database through
/// <see cref="Serialize"/>, so the raw text a value round-trips to is always the same raw text it
/// was written with, and comparing raw text is enough to correctly detect "nothing changed".
/// </para>
/// </remarks>
public static class JsonElementConverter
{
    /// <summary>Converts a non-nullable <see cref="JsonElement"/> column (e.g. <c>extras</c>) to/from its jsonb <see cref="string"/> representation.</summary>
    public static readonly ValueConverter<JsonElement, string> Converter = new(
        value => Serialize(value),
        text => Deserialize(text));

    /// <summary>Structural comparer for non-nullable <see cref="JsonElement"/> columns. See remarks on why this is required.</summary>
    public static readonly ValueComparer<JsonElement> Comparer = new(
        (left, right) => RawTextEquals(left, right),
        value => value.GetRawText().GetHashCode(StringComparison.Ordinal),
        value => Snapshot(value));

    /// <summary>Converts a nullable <see cref="JsonElement"/> column (e.g. <c>weather</c>, <c>setup</c>) to/from its jsonb <see cref="string"/> representation.</summary>
    public static readonly ValueConverter<JsonElement?, string?> NullableConverter = new(
        value => value.HasValue ? Serialize(value.Value) : null,
        text => text == null ? null : Deserialize(text));

    /// <summary>Structural comparer for nullable <see cref="JsonElement"/> columns. See remarks on why this is required.</summary>
    public static readonly ValueComparer<JsonElement?> NullableComparer = new(
        (left, right) => NullableRawTextEquals(left, right),
        value => value.HasValue ? value.Value.GetRawText().GetHashCode(StringComparison.Ordinal) : 0,
        value => value.HasValue ? Snapshot(value.Value) : null);

    private static bool RawTextEquals(JsonElement left, JsonElement right) => left.GetRawText() == right.GetRawText();

    private static bool NullableRawTextEquals(JsonElement? left, JsonElement? right) => (left.HasValue, right.HasValue) switch
    {
        (false, false) => true,
        (true, true) => RawTextEquals(left.GetValueOrDefault(), right.GetValueOrDefault()),
        _ => false,
    };

    /// <summary>
    /// Serializes a <see cref="JsonElement"/> to the raw jsonb text Postgres should store. A
    /// default/<see langword="undefined"/> element (e.g. an uninitialized <c>required</c> property
    /// on a Core record that was never explicitly set) is written as JSON <c>null</c> rather than
    /// throwing, since jsonb columns cannot store CLR's "no value at all" concept. Exposed
    /// (rather than kept <see langword="private"/>) so <c>Bulk/NpgsqlTelemetryWriter</c> can reuse
    /// this exact rule when writing jsonb columns through the binary <c>COPY</c> path, which
    /// bypasses this converter entirely.
    /// </summary>
    internal static string Serialize(JsonElement value) => value.ValueKind == JsonValueKind.Undefined ? "null" : value.GetRawText();

    /// <summary>
    /// Parses a jsonb column back into a <see cref="JsonElement"/>. The element is cloned off the
    /// parsed document so that document can be disposed here and its pooled buffers returned —
    /// returning <c>RootElement</c> directly would keep every materialized document alive until the
    /// GC got to it, because a <see cref="JsonElement"/> holds a reference to its parent.
    /// </summary>
    private static JsonElement Deserialize(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Produces the change tracker's original-values snapshot. <see cref="JsonElement.Clone"/>
    /// detaches the value from a document that may later be disposed; where the parent is already
    /// non-disposable it returns the element unchanged, which is safe because a
    /// <see cref="JsonDocument"/>'s contents cannot be mutated after parsing.
    /// </summary>
    private static JsonElement Snapshot(JsonElement value) => value.Clone();
}
