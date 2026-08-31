namespace RaceIntelligence.Read.Api.Endpoints;

/// <summary>Shared RFC 7807 <see cref="ProblemDetails"/> results for the read endpoints.</summary>
internal static class ProblemResults
{
    /// <summary>404: no session exists with the given id.</summary>
    public static IResult SessionNotFound(Guid id) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Session not found",
        detail: $"No session with id '{id}'.");

    /// <summary>404: the session exists but recorded no telemetry for some requested lap.</summary>
    /// <remarks>
    /// Distinct from an empty 200 on purpose. "This lap has no samples" is nearly always a wrong
    /// lap number, and an empty array renders as a blank chart that looks like a bug in the chart.
    /// <para>
    /// <b>Every missing lap is named, not just the first.</b> An overlay asks for several at once,
    /// and fixing them one round trip at a time is the experience this avoids.
    /// </para>
    /// </remarks>
    public static IResult LapsNotFound(Guid sessionId, IReadOnlyList<int> lapNumbers) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Lap not found",
        detail: lapNumbers.Count == 1
            ? $"Session '{sessionId}' has no telemetry for lap {lapNumbers[0]}."
            : $"Session '{sessionId}' has no telemetry for laps {string.Join(", ", lapNumbers)}.");

    /// <summary>400: a query value was outside what the endpoint accepts.</summary>
    public static IResult InvalidQuery(string parameter, string reason) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid query parameter",
        detail: $"'{parameter}' {reason}");

    /// <summary>
    /// 400: the requested lap holds more samples than this endpoint will serve at once.
    /// </summary>
    /// <remarks>
    /// A refusal with the numbers in it, rather than a truncated body that looks like a complete
    /// one. A lap this large means either a very long lap or a sample rate nobody expected, and
    /// silently returning the first N would put a chart on screen that is missing its end with
    /// nothing to say so.
    /// </remarks>
    public static IResult LapTooLarge(int lapNumber, int count, int maximum) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Lap too large to read",
        detail: $"Lap {lapNumber} holds {count} samples; this endpoint serves at most {maximum} for one lap.");

    /// <summary>400: more laps were named than this endpoint will read at once.</summary>
    /// <remarks>
    /// The overlay this endpoint exists for is two to four laps; the ceiling is generous rather than
    /// tight. What it rules out is "give me the session, one lap at a time" — the request this
    /// endpoint's whole design refuses, arriving by a different door.
    /// </remarks>
    public static IResult TooManyLaps(int count, int maximum) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Too many laps requested",
        detail: $"That request names {count} laps; this endpoint reads at most {maximum} at a time.");

    /// <summary>
    /// 400: the named laps are each readable, but together they exceed what this endpoint serves.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="LapTooLarge"/> because it means something different: no single lap
    /// is wrong, the overlay is simply too big. The caller's fix is to drop a lap rather than to
    /// wonder which one is malformed.
    /// </remarks>
    public static IResult RequestTooLarge(int count, int maximum) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Too many samples requested",
        detail: $"Those laps hold {count} samples between them; this endpoint serves at most {maximum} at a time.");
}
