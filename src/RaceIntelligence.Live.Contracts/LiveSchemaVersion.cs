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
    /// <summary>
    /// The current supported live schema version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pinned at 1, and it stays there until the project is released.</b> Nothing is tagged and
    /// nothing is deployed, so there is no older collector anywhere for a newer hub to be
    /// compatible with — every part of this system is built and started from the same commit. A
    /// version ladder in that situation records history nobody can observe, and each rung is a
    /// branch to keep working and reason about for a compatibility case that cannot arise.
    /// </para>
    /// <para>
    /// So the wire shape changes in place: fields are added, moved and renumbered freely, and the
    /// version is not bumped for it. The handshake below stays because it costs one comparison and
    /// turns a stale process left running from an earlier build — the one mismatch that really does
    /// happen during development — into a refusal that names both versions, rather than a decode
    /// error mid-race.
    /// </para>
    /// <para>
    /// The first release tag is what ends this. From <c>v1.0.0</c> onwards there are collectors in
    /// the wild that a hub has to speak to, and this constant starts moving with the changelog to
    /// match. See <c>CLAUDE.md</c> for the project-wide statement of the same rule.
    /// </para>
    /// </remarks>
    public const int Current = 1;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="schemaVersion"/> is a version this hub
    /// understands.
    /// </summary>
    public static bool IsSupported(int schemaVersion) => schemaVersion == Current;
}
