namespace RaceIntelligence.Web.Live;

/// <summary>
/// The stable codes carried on <c>LiveErrorMessage</c>, so the dashboard branches on a constant
/// rather than on prose.
/// </summary>
/// <remarks>
/// The message text alongside each of these is for a human and may be reworded freely; these
/// values may not, because a browser build older than the hub still has to recognise them.
/// </remarks>
public static class LiveErrorCodes
{
    /// <summary>The viewer asked to watch a room the hub does not have.</summary>
    public const string UnknownRoom = "unknownRoom";

    /// <summary>The room a viewer was watching has expired or ended.</summary>
    public const string RoomClosed = "roomClosed";

    /// <summary>The viewer asked to focus a driver the hub cannot find in the room it is watching.</summary>
    public const string UnknownDriver = "unknownDriver";

    /// <summary>
    /// The viewer asked to focus a driver whose own machine is not publishing, so there is no
    /// full-rate telemetry to send.
    /// </summary>
    public const string NoTelemetryForDriver = "noTelemetryForDriver";

    /// <summary>
    /// The viewer asked to focus one driver more than <see cref="LiveViewer.MaxFocusDrivers"/>
    /// allows.
    /// </summary>
    /// <remarks>
    /// Refused rather than answered by evicting whoever was focused first: a viewer that asked for a
    /// third car and silently lost one of the two it was reading would have no way to tell that from
    /// the stream simply stopping.
    /// </remarks>
    public const string TooManyFocusDrivers = "tooManyFocusDrivers";

    /// <summary>The viewer must be watching a room before it can focus a driver in one.</summary>
    public const string NotWatchingRoom = "notWatchingRoom";

    /// <summary>The hub could not understand what the viewer sent.</summary>
    public const string MalformedCommand = "malformedCommand";
}
