using System.ComponentModel.DataAnnotations;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// Configuration for the live hub, bound from the <c>Live</c> configuration section.
/// </summary>
public sealed class LiveHubOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Live";

    /// <summary>
    /// The shared secret a collector must present as <c>X-Api-Key</c> to publish.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Publishing is gated and viewing is not, because the two carry opposite risks. A viewer can
    /// only read what someone chose to publish; a publisher injects the data every race engineer
    /// watching is making decisions from, and there is no way to tell a fabricated timing tower
    /// from a real one by looking at it.
    /// </para>
    /// <para>
    /// Same Phase-1 compromise as the ingest API's key: one static secret, no per-client identity,
    /// no rotation. Unlike that one, this endpoint is meant to be reachable from the internet
    /// through a tunnel, so the comparison here is constant-time — see
    /// <see cref="LiveApiKeyGate"/>.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Live:ApiKey must be set; the publishing endpoint cannot be left unauthenticated.")]
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// How long a room survives with no frames from any publisher before the hub forgets it.
    /// </summary>
    /// <remarks>
    /// Long enough to cover a reconnect, so a collector whose socket drops mid-race rejoins the
    /// room it was already in — viewers watching it never notice. Short enough that a finished
    /// session leaves the dashboard on its own when a client is killed without a goodbye.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:05", "00:10:00")]
    public TimeSpan RoomExpiry { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How often expired rooms are swept.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan RoomSweepInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The largest message the hub will read from a publishing socket, in bytes.
    /// </summary>
    /// <remarks>
    /// Sized for the worst realistic standings frame: a full 128-car grid, every field populated,
    /// with room to spare. A publisher that exceeds this is malfunctioning or malicious, and
    /// either way the connection is closed rather than the hub buffering whatever it is sent.
    /// </remarks>
    [Range(4 * 1024, 4 * 1024 * 1024)]
    public int MaxPublisherMessageBytes { get; init; } = 512 * 1024;
}
