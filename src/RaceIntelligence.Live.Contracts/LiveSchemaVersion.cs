namespace RaceIntelligence.Live.Contracts;

/// <summary>
/// Versions the shape of the live wire DTOs in this project, independently of
/// <c>RaceIntelligence.Ingest.Contracts.SchemaVersion</c>.
/// </summary>
/// <remarks>
/// <para>
/// A separate version line from the ingest schema, because the two evolve for unrelated reasons
/// and at very different rates. The ingest schema is conservative by necessity — it describes rows
/// that stay in the database forever, so a change there is close to permanent. Nothing on this
/// path is stored at all: a live frame is superseded within milliseconds and forgotten. Tying the
/// two together would either freeze the dashboard behind the archive's caution or drag the archive
/// along with the dashboard's churn.
/// </para>
/// <para>
/// The tolerance for a mismatch is correspondingly different too. An ingest schema the server does
/// not recognise is rejected outright, because misreading it would bake a wrong number into
/// permanent history. A live schema mismatch costs one disconnected publisher and no data at all,
/// so the hub refuses the connection with a clear reason and the operator updates the client.
/// </para>
/// </remarks>
public static class LiveSchemaVersion
{
    /// <summary>The current, and so far only, supported live schema version.</summary>
    public const int Current = 1;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="schemaVersion"/> is a version this hub
    /// understands.
    /// </summary>
    public static bool IsSupported(int schemaVersion) => schemaVersion == Current;
}
