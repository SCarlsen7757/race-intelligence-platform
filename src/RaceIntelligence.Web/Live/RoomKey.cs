using RaceIntelligence.Live.Contracts.Publish;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// What the hub uses to decide that two publishers are describing the same session.
/// </summary>
/// <remarks>
/// <para>
/// RaceRoom publishes no server identifier, so this is derived from what the simulator does report.
/// It is the same tuple the connector already uses to tell one session from the next, plus the game
/// key so two simulators can never collide.
/// </para>
/// <para>
/// <b>A key, not an identity.</b> Two people qualifying at the same track at the same time in
/// different servers produce exactly the same key. What separates them is who they can see, which
/// is what <c>LiveRosterFingerprint</c> captures — and confirming the key against the roster before
/// merging two publishers is step 6 of the build order. Until then a shared key is taken at face
/// value, which is correct for the single-publisher case that step 4 covers and is the reason the
/// merge is scheduled immediately after.
/// </para>
/// </remarks>
/// <param name="GameKey">Which simulator.</param>
/// <param name="TrackName">Track name as the simulator reports it.</param>
/// <param name="LayoutName">Layout name as the simulator reports it.</param>
/// <param name="SessionType">The canonical <see cref="RaceIntelligence.Core.Sessions.SessionType"/> as an integer.</param>
/// <param name="SessionIteration">Which session of this type it is.</param>
public readonly record struct RoomKey(
    string GameKey,
    string TrackName,
    string LayoutName,
    int SessionType,
    int SessionIteration)
{
    /// <summary>Derives the key a session announcement belongs to.</summary>
    /// <remarks>
    /// Names are trimmed and compared case-insensitively. They come from the same simulator build
    /// on both machines so they ought to match byte for byte, and this costs nothing — but a room
    /// that silently forked into two because one client reported a trailing space would present as
    /// "the merge does not work", which is a bad thing to have to debug from a race report.
    /// </remarks>
    public static RoomKey From(LiveSessionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return new RoomKey(
            frame.GameKey.Trim(),
            frame.TrackName.Trim(),
            frame.LayoutName.Trim(),
            frame.SessionType,
            frame.SessionIteration);
    }

    /// <inheritdoc />
    public bool Equals(RoomKey other) =>
        SessionType == other.SessionType
        && SessionIteration == other.SessionIteration
        && string.Equals(GameKey, other.GameKey, StringComparison.OrdinalIgnoreCase)
        && string.Equals(TrackName, other.TrackName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(LayoutName, other.LayoutName, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        SessionType,
        SessionIteration,
        StringComparer.OrdinalIgnoreCase.GetHashCode(GameKey),
        StringComparer.OrdinalIgnoreCase.GetHashCode(TrackName),
        StringComparer.OrdinalIgnoreCase.GetHashCode(LayoutName));
}
