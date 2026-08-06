using RaceIntelligence.Ingest.Contracts;

namespace RaceIntelligence.Ingest.Api.Endpoints;

/// <summary>Shared RFC 7807 <see cref="ProblemDetails"/> results reused across the session and telemetry endpoints.</summary>
internal static class ProblemResults
{
    /// <summary>400: the request body declared a <see cref="SchemaVersion"/> this server does not understand.</summary>
    /// <remarks>Never silently coerced or best-effort interpreted — see <see cref="SchemaVersion"/>.</remarks>
    public static IResult SchemaVersionUnsupported(int received) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Unsupported schema version",
        detail: $"Schema version {received} is not supported. This server accepts schema version {SchemaVersion.Current}.");

    /// <summary>400: a request field carrying raw JSON text did not contain valid JSON.</summary>
    public static IResult MalformedJson(string field, string reason) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Malformed JSON",
        detail: $"'{field}' is not valid JSON: {reason}");

    /// <summary>400: a raw sim code is outside the range its <c>smallint</c> column can represent.</summary>
    /// <remarks>
    /// Rejected rather than narrowed: a wrapped value would be indistinguishable from a real code,
    /// and <c>-1</c> in particular is RaceRoom's "not available" sentinel.
    /// </remarks>
    public static IResult ValueOutOfRange(string field, int received) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Value out of range",
        detail: $"'{field}' was {received}, which is outside the supported range {short.MinValue} to {short.MaxValue}.");

    /// <summary>404: no session exists with the given id.</summary>
    public static IResult SessionNotFound(Guid id) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Session not found",
        detail: $"No session with id '{id}'.");
}
