namespace RaceIntelligence.Ingest.Contracts;

/// <summary>
/// Versions the shape of the wire DTOs in this project, independent of the <c>/api/v1/</c> route
/// prefix.
/// </summary>
/// <remarks>
/// The platform versions on two axes: the <c>/api/v1/</c> route prefix versions the HTTP contract
/// (URLs, verbs, status codes), while <see cref="Current"/> versions the body shape carried inside
/// every request. A route can stay <c>v1</c> for years while the DTO shape it accepts evolves in
/// controlled, explicit steps — every request body declares the schema version it was written
/// against, and the server rejects anything it does not understand rather than silently
/// misinterpreting fields. This matters specifically because raw telemetry is permanent: a session
/// or sample accepted today must remain unambiguous to read for as long as the database exists.
/// </remarks>
public static class SchemaVersion
{
    /// <summary>The current, and so far only, supported schema version.</summary>
    /// <remarks>
    /// <b>Pinned at 1 until the project is released.</b> The argument above is about permanence,
    /// and nothing is permanent yet: there is no release tag, no deployment, and no archive whose
    /// rows have to stay readable across a change. So the DTO shape changes in place and this does
    /// not move. From <c>v1.0.0</c> it starts stepping with the changelog, exactly as described
    /// above — see <c>CLAUDE.md</c> for the project-wide rule.
    /// </remarks>
    public const int Current = 1;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="schemaVersion"/> is a version this
    /// server understands. Callers must reject unsupported versions outright (HTTP 400) rather
    /// than attempt to coerce or best-effort interpret them.
    /// </summary>
    /// <param name="schemaVersion">The <c>SchemaVersion</c> value carried on an inbound request body.</param>
    public static bool IsSupported(int schemaVersion) => schemaVersion == Current;
}
