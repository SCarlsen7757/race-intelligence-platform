using System.Text.Json.Serialization;

namespace RaceIntelligence.Live.Contracts.View;

/// <summary>
/// Base type for the (few) messages a browser sends back to the hub.
/// </summary>
/// <remarks>
/// The viewing socket is overwhelmingly one-directional; these exist only so a viewer can say what
/// it wants to receive, rather than the hub broadcasting every room and every driver's full-rate
/// telemetry to everyone. Nothing here mutates any state a publisher owns — a viewer cannot affect
/// what is collected, only what it is sent.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(WatchRoomCommand), "watchRoom")]
[JsonDerivedType(typeof(FocusDriverCommand), "focusDriver")]
[JsonDerivedType(typeof(UnfocusDriverCommand), "unfocusDriver")]
[JsonDerivedType(typeof(SubscribeLapHistoryCommand), "subscribeLapHistory")]
[JsonDerivedType(typeof(UnsubscribeLapHistoryCommand), "unsubscribeLapHistory")]
public abstract record LiveViewCommand;

/// <summary>Subscribes this viewer to a room's timing tower, replacing any previous subscription.</summary>
/// <param name="RoomId">
/// The room to watch, or <see langword="null"/> to leave the current one and receive only the room
/// list.
/// </param>
public sealed record WatchRoomCommand(string? RoomId) : LiveViewCommand;

/// <summary>
/// Adds a driver to the set whose full-rate channels this viewer receives.
/// </summary>
/// <remarks>
/// <b>Additive, and uncapped.</b> A race engineer answering "why is he quicker than me through the
/// infield" needs both cars on screen at once, so this follows the
/// <see cref="SubscribeLapHistoryCommand"/> precedent rather than replacing the previous focus, and
/// the same way that command is uncapped, so is this one — a viewer may follow as many drivers at
/// full rate as the room has.
/// </remarks>
/// <param name="DriverKey">
/// The driver to follow, matching <see cref="TowerRow.DriverKey"/>, or <see langword="null"/> to
/// drop <b>every</b> focus at once — the same meaning null carries on
/// <see cref="SubscribeLapHistoryCommand"/>, and for the same reason: with a set rather than a
/// single slot, dropping one has to name it, which is what <see cref="UnfocusDriverCommand"/> is
/// for. A driver whose tier is <see cref="LiveDataTier.Observed"/> has no full-rate data to send,
/// and the hub answers with a <see cref="LiveErrorMessage"/> rather than silence.
/// </param>
public sealed record FocusDriverCommand(string? DriverKey) : LiveViewCommand;

/// <summary>
/// Removes one driver from the focus set, leaving every other focus untouched.
/// </summary>
/// <remarks>
/// The mirror of <see cref="UnsubscribeLapHistoryCommand"/>, and it exists for the same reason:
/// emulating a single unfocus by dropping all and re-stating the rest leaves a window in which the
/// other driver is unsubscribed, and at 60 Hz that window is visible as a hole in their traces.
/// Unfocusing a driver that was never focused is a no-op rather than an error.
/// </remarks>
/// <param name="DriverKey">The driver to stop following.</param>
public sealed record UnfocusDriverCommand(string DriverKey) : LiveViewCommand;

/// <summary>
/// Adds a driver to the set whose completed laps this viewer receives, and answers immediately with
/// what the hub has so far.
/// </summary>
/// <remarks>
/// <para>
/// <b>Additive and uncapped</b>, the same as <see cref="FocusDriverCommand"/>. A viewer may hold as
/// many of these as it has rows expanded, because comparing stints side by side is the whole point
/// of expanding rows. A lap history is sent when a lap finishes, roughly once a minute per driver, so
/// a dozen subscriptions is still far less traffic than one focus stream.
/// </para>
/// <para>
/// Works for a driver of any <see cref="LiveDataTier"/>. Lap history comes from the standings
/// snapshot, which sees every car, so a driver who is not running a collector still has one.
/// </para>
/// <para>
/// The hub keeps no viewer memory across connections, so a reconnecting dashboard resends these
/// alongside its <see cref="WatchRoomCommand"/> and <see cref="FocusDriverCommand"/>.
/// </para>
/// </remarks>
/// <param name="DriverKey">
/// The driver to follow, matching <see cref="TowerRow.DriverKey"/>, or <see langword="null"/> to
/// drop every lap-history subscription at once. A key the hub cannot find in the room is answered
/// with a <see cref="LiveErrorMessage"/> carrying <c>unknownDriver</c>, and the connection stays
/// open.
/// </param>
public sealed record SubscribeLapHistoryCommand(string? DriverKey) : LiveViewCommand;

/// <summary>
/// Removes one driver from the lap-history set — a collapsed row.
/// </summary>
/// <remarks>
/// A command of its own rather than a null <see cref="SubscribeLapHistoryCommand.DriverKey"/>,
/// because the subscription is a set: null there already has to mean "drop all", and a dashboard
/// collapsing one of five expanded rows needs to say which one. Unsubscribing from a driver that was
/// never subscribed is a no-op rather than an error — the browser and the hub can disagree about
/// what is open after a reconnect, and that disagreement is not worth an error message.
/// </remarks>
/// <param name="DriverKey">The driver to stop following.</param>
public sealed record UnsubscribeLapHistoryCommand(string DriverKey) : LiveViewCommand;
