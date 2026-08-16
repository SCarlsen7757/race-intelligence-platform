using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaceIntelligence.Live.Contracts.View;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance the hub serializes view messages with
/// and deserializes viewer commands with.
/// </summary>
/// <remarks>
/// <para>
/// Shared rather than constructed per call site for correctness as much as cost: the browser
/// parses these by property name, so an endpoint that quietly used different naming or a different
/// null policy would produce fields the dashboard silently reads as <c>undefined</c>. (It is also
/// genuinely expensive — <see cref="JsonSerializerOptions"/> caches its resolved metadata, and a
/// fresh instance per message would re-resolve every contract on a hot path.)
/// </para>
/// <para>
/// <b>camelCase</b> because the consumer is JavaScript. <b>Nulls omitted</b> because most fields on
/// a timing tower row are absent most of the time — a car that has not set a lap has null times,
/// null gaps and null sectors — and dropping them is a large saving on a message sent several times
/// a second. JavaScript reads a missing property and an explicit <c>null</c> identically as
/// <c>undefined</c>-ish, so nothing downstream can tell the difference.
/// </para>
/// <para>
/// <b><see cref="JsonSerializerDefaults.Web"/></b> also makes reads case-insensitive and rejects
/// trailing commas, matching what ASP.NET Core does everywhere else in the platform.
/// </para>
/// </remarks>
public static class LiveViewJson
{
    /// <summary>The shared serializer options for <see cref="LiveViewMessage"/> and <see cref="LiveViewCommand"/>.</summary>
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The largest viewer command the hub will read off a socket, in bytes.
    /// </summary>
    /// <remarks>
    /// Commands are two small records; anything approaching this is either a bug or an attempt to
    /// make the hub allocate on an unauthenticated connection, since the viewing endpoint is open.
    /// </remarks>
    public const int MaxCommandBytes = 4 * 1024;
}
