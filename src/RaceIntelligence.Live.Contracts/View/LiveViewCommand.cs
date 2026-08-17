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
/// Subscribes this viewer to one driver's full-rate channels, replacing any previous focus.
/// </summary>
/// <remarks>
/// At most one focus per viewer. A head-to-head comparison of two drivers is a second socket
/// rather than a second subscription — that keeps the hub's per-viewer conflation logic to a single
/// stream, which is what guarantees a slow viewer drops frames instead of stalling the publisher.
/// </remarks>
/// <param name="DriverKey">
/// The driver to follow, matching <see cref="TowerRow.DriverKey"/>, or <see langword="null"/> to
/// stop following. A driver whose tier is <see cref="LiveDataTier.Observed"/> has no full-rate data
/// to send, and the hub answers with a <see cref="LiveErrorMessage"/> rather than silence.
/// </param>
public sealed record FocusDriverCommand(string? DriverKey) : LiveViewCommand;

/// <summary>
/// Adds a driver to the set whose completed laps this viewer receives, and answers immediately with
/// what the hub has so far.
/// </summary>
/// <remarks>
/// <para>
/// <b>Additive, unlike <see cref="FocusDriverCommand"/>.</b> A viewer may hold several of these at
/// once, because comparing two drivers' stints side by side is the whole point of expanding rows —
/// where comparing two drivers' 60 Hz pedal traces is a second socket. The cost scale is what makes
/// the two answers different: a lap history is sent when a lap finishes, roughly once a minute per
/// driver, so a dozen subscriptions is still less traffic than one focus stream.
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
