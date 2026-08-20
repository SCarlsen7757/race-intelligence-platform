namespace RaceIntelligence.Identity.Api.Endpoints;

/// <summary>Shared RFC 7807 <see cref="ProblemDetails"/> results for the registry's endpoints.</summary>
internal static class ProblemResults
{
    /// <summary>400: a required field was missing or blank.</summary>
    public static IResult Required(string field) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Missing required value",
        detail: $"'{field}' is required and cannot be empty.");

    /// <summary>404: no person exists with the given id.</summary>
    public static IResult PersonNotFound(Guid id) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Person not found",
        detail: $"No person with id '{id}'.");

    /// <summary>404: this person does not claim that simulator identity.</summary>
    public static IResult AliasNotFound(Guid personId, string simKey, string simDriverId) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Alias not found",
        detail: $"Person '{personId}' does not claim '{simDriverId}' in '{simKey}'.");

    /// <summary>
    /// 409: that simulator identity already belongs to someone.
    /// </summary>
    /// <remarks>
    /// A conflict rather than an overwrite, and deliberately so. One simulator identity belongs to
    /// at most one person, and silently reassigning it would rewrite a human's earlier assertion on
    /// the strength of a later one — with no record that it happened. Releasing the existing claim
    /// is an explicit act.
    /// </remarks>
    public static IResult AliasAlreadyClaimed(string simKey, string simDriverId) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Simulator identity already claimed",
        detail: $"'{simDriverId}' in '{simKey}' is already claimed by another person. Release that claim first.");
}
