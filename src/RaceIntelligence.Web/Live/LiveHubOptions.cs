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
    /// One static secret, no per-client identity, no rotation — now the only key in the platform
    /// still shaped that way, since the ingest API moved to a labelled key per collector. There is
    /// one publisher per room and the hub stores nothing, so a second key would name nothing here;
    /// the comparison is constant-time regardless — see <see cref="LiveApiKeyGate"/>.
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

    /// <summary>
    /// The browser origins allowed to read the room list and open a viewing socket.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Origins, not URLs: scheme, host and port with no trailing slash, exactly as a browser sends
    /// them in <c>Origin</c> — <c>http://localhost:3000</c>.
    /// </para>
    /// <para>
    /// <b>Required, and required to be non-empty, because the previous behaviour was to accept
    /// everything.</b> <see cref="Microsoft.AspNetCore.Builder.WebSocketOptions.AllowedOrigins"/>
    /// treats an empty list as "no origin check at all", so a hub that simply forgot to configure
    /// this would look configured and be open to every page on the internet. Failing to boot is
    /// the honest outcome, and it is the same call already made for <see cref="ApiKey"/>.
    /// </para>
    /// <para>
    /// This constrains browsers only. A collector connects with a raw <c>ClientWebSocket</c> and
    /// sends no <c>Origin</c> header, so it is unaffected — which is correct, because publishing is
    /// guarded by the API key rather than by where the request claims to come from. An origin
    /// check is a defence against a page in someone's browser, not against a program.
    /// </para>
    /// </remarks>
    [Required(ErrorMessage = "Live:AllowedOrigins must list the dashboard's origin; an empty list would accept every origin.")]
    [MinLength(1, ErrorMessage = "Live:AllowedOrigins must list the dashboard's origin; an empty list would accept every origin.")]
    public string[] AllowedOrigins { get; init; } = [];
}
