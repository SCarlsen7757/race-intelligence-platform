using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using RaceIntelligence.Live.Contracts.View;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// The hub's side of one browser's viewing connection: a command loop and a send pump, running
/// together for as long as the socket lives.
/// </summary>
/// <remarks>
/// <para>
/// Two loops rather than one, because the two directions are completely independent. A viewer that
/// sends nothing for an hour must still receive at 60 Hz, and a viewer clicking through drivers
/// must not have its commands read only between frames. They share nothing but the socket, which
/// permits one concurrent send and one concurrent receive.
/// </para>
/// <para>
/// <b>This endpoint is open — no key, no session, no identity.</b> Everything reachable from here
/// is read-only by construction: the commands select what to receive and touch nothing a publisher
/// owns. The two things an anonymous caller could otherwise cost the hub are bounded explicitly —
/// command size by <see cref="LiveViewJson.MaxCommandBytes"/>, and outbound memory by the
/// viewer's fixed-size conflating queue.
/// </para>
/// </remarks>
public sealed class ViewerSession(
    LiveRoomRegistry rooms,
    LiveViewerRegistry viewers,
    TimeProvider timeProvider,
    ILogger<ViewerSession> logger)
{
    /// <summary>Runs the connection until the browser disconnects or the host shuts down.</summary>
    public async Task RunAsync(ILiveSocket socket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var viewer = new LiveViewer();
        viewers.Add(viewer);

        // The room list is sent unprompted, before any command: it is the dashboard's landing view,
        // and a viewer that had to ask for it would render empty until its first request completed.
        viewer.Queue.OfferRoomList(rooms.BuildRoomList());

        // Linked so that whichever loop ends first — a browser tab closing, a send failing — takes
        // the other with it instead of leaving it parked on a dead socket.
        using var connectionEnded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var receiving = ReceiveCommandsAsync(socket, viewer, connectionEnded.Token);
            var sending = SendAsync(socket, viewer, connectionEnded.Token);

            await Task.WhenAny(receiving, sending).ConfigureAwait(false);
            await connectionEnded.CancelAsync().ConfigureAwait(false);

            // Both are awaited so neither is left running against a socket about to be disposed.
            await Task.WhenAll(receiving, sending).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Either loop ending, or the host shutting down. Both are ordinary.
        }
        catch (WebSocketException ex)
        {
            logger.LogDebug(ex, "A viewer connection dropped.");
        }
        finally
        {
            viewers.Remove(viewer);

            var (tower, focus) = viewer.Queue.DroppedFrames;
            if (tower > 0 || focus > 0)
            {
                logger.LogDebug(
                    "A viewer disconnected having missed {Tower} tower and {Focus} focus frames it could not keep up with.",
                    tower, focus);
            }
        }
    }

    private async Task ReceiveCommandsAsync(ILiveSocket socket, LiveViewer viewer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ReadOnlyMemory<byte>? payload;
            try
            {
                payload = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (LiveSocketMessageTooLargeException)
            {
                // Nothing a real dashboard sends comes close to the cap, so this is a broken or
                // hostile client. It is told and disconnected rather than the hub continuing to
                // read from it.
                await CloseQuietlyAsync(socket, WebSocketCloseStatus.MessageTooBig, "Command too large.")
                    .ConfigureAwait(false);
                return;
            }

            if (payload is null)
            {
                return;
            }

            LiveViewCommand? command;
            try
            {
                command = JsonSerializer.Deserialize<LiveViewCommand>(payload.Value.Span, LiveViewJson.Default);
            }
            catch (JsonException)
            {
                // Unlike a publisher's undecodable frame, this does not end the connection. A
                // browser's commands are discrete requests rather than a position in a stream, so
                // one bad command costs that command and nothing else.
                viewer.Queue.OfferError(new LiveErrorMessage(
                    LiveErrorCodes.MalformedCommand, "The command could not be understood."));
                continue;
            }

            if (command is null)
            {
                continue;
            }

            Handle(viewer, command);
        }
    }

    private void Handle(LiveViewer viewer, LiveViewCommand command)
    {
        switch (command)
        {
            case WatchRoomCommand { RoomId: null }:
                viewer.WatchRoom(null);
                break;

            case WatchRoomCommand watch:
                WatchRoom(viewer, watch.RoomId!);
                break;

            case FocusDriverCommand { DriverKey: null }:
                viewer.UnfocusAll();
                break;

            case FocusDriverCommand focus:
                FocusDriver(viewer, focus.DriverKey!);
                break;

            case UnfocusDriverCommand unfocus:
                viewer.Unfocus(unfocus.DriverKey);
                break;

            case SubscribeLapHistoryCommand { DriverKey: null }:
                viewer.UnsubscribeAllLapHistory();
                break;

            case SubscribeLapHistoryCommand subscribe:
                SubscribeLapHistory(viewer, subscribe.DriverKey!);
                break;

            case UnsubscribeLapHistoryCommand unsubscribe:
                viewer.UnsubscribeLapHistory(unsubscribe.DriverKey);
                break;

            default:
                viewer.Queue.OfferError(new LiveErrorMessage(
                    LiveErrorCodes.MalformedCommand, "Unsupported command."));
                break;
        }
    }

    private void WatchRoom(LiveViewer viewer, string roomId)
    {
        if (rooms.FindByRoomId(roomId) is not { } room)
        {
            viewer.Queue.OfferError(new LiveErrorMessage(
                LiveErrorCodes.UnknownRoom, "That session is no longer live."));
            return;
        }

        viewer.WatchRoom(roomId);

        // Before the tower, and unconditionally. The change-detecting broadcast only fires when
        // something moves, so a viewer joining a race whose pit window opened ten minutes ago would
        // otherwise never be told it is open — and every average speed in the tower would stay blank
        // until some publisher happened to re-announce the session.
        viewer.Queue.OfferSessionState(room.SessionState());

        // Sent immediately rather than waiting for the next publisher frame. At the tower's rate
        // that wait is only ~100 ms, but a viewer switching rooms would see an empty table for it,
        // and a room whose publisher has gone quiet would show nothing at all.
        if (room.Snapshot(timeProvider.GetUtcNow()) is { } snapshot)
        {
            viewer.Queue.OfferTower(snapshot);
        }
    }

    private void FocusDriver(LiveViewer viewer, string driverKey)
    {
        if (viewer.RoomId is not { } roomId)
        {
            viewer.Queue.OfferError(new LiveErrorMessage(
                LiveErrorCodes.NotWatchingRoom, "Pick a session before following a driver in it."));
            return;
        }

        if (rooms.FindByRoomId(roomId) is not { } room)
        {
            viewer.Queue.OfferError(new LiveErrorMessage(
                LiveErrorCodes.UnknownRoom, "That session is no longer live."));
            return;
        }

        // Answered explicitly rather than by silence. A driver with no client running looks
        // identical in the tower to one whose telemetry simply has not arrived yet, and a viewer
        // that clicked a row deserves to be told which it is instead of watching an empty panel.
        switch (room.GetFocusAvailability(driverKey))
        {
            case DriverFocusAvailability.Available:
                viewer.Focus(driverKey);

                // Answered from what the hub already holds rather than waiting out an extras
                // interval. At roughly 1 Hz that is a second of an empty damage panel, which a race
                // engineer reads as "no damage" rather than as "not known yet".
                if (room.LatestSlowFrameFor(driverKey) is { } extras)
                {
                    viewer.Queue.OfferSlowFrame(extras);
                }

                // Same argument, and it bites harder: tyre charts plot a fifteen-minute stint, so a
                // viewer waiting out an interval sees an empty chart for a car that has been running
                // on those tyres for half an hour.
                if (room.LatestStintFor(driverKey) is { } stint)
                {
                    viewer.Queue.OfferStint(stint);
                }

                break;

            case DriverFocusAvailability.ObservedOnly:
                viewer.Queue.OfferError(new LiveErrorMessage(
                    LiveErrorCodes.NoTelemetryForDriver,
                    "That driver is not running a collector, so only timing is available for them."));
                break;

            default:
                viewer.Queue.OfferError(new LiveErrorMessage(
                    LiveErrorCodes.UnknownDriver, "That driver is not in this session."));
                break;
        }
    }

    /// <summary>
    /// Subscribes a viewer to one driver's completed laps and answers with what the hub already has.
    /// </summary>
    /// <remarks>
    /// Deliberately not gated on <see cref="DriverFocusAvailability"/> the way focus is. Lap history
    /// is read out of the standings snapshot, which describes every car in the session, so an
    /// <see cref="LiveDataTier.Observed"/> driver — one whose machine is not publishing — has a full
    /// history like anyone else. The only refusal is a driver the room has never seen.
    /// </remarks>
    private void SubscribeLapHistory(LiveViewer viewer, string driverKey)
    {
        if (viewer.RoomId is not { } roomId)
        {
            viewer.Queue.OfferError(new LiveErrorMessage(
                LiveErrorCodes.NotWatchingRoom, "Pick a session before following a driver's laps in it."));
            return;
        }

        if (rooms.FindByRoomId(roomId) is not { } room)
        {
            viewer.Queue.OfferError(new LiveErrorMessage(
                LiveErrorCodes.UnknownRoom, "That session is no longer live."));
            return;
        }

        if (room.LapHistoryFor(driverKey) is not { } history)
        {
            viewer.Queue.OfferError(new LiveErrorMessage(
                LiveErrorCodes.UnknownDriver, "That driver is not in this session."));
            return;
        }

        viewer.SubscribeLapHistory(driverKey);

        // Sent straight away rather than waiting for the driver's next lap, which on a long circuit
        // is minutes of an empty panel — and would be forever for a driver who has finished.
        viewer.Queue.OfferLapHistory(history);
    }

    /// <summary>
    /// Drains the viewer's queue onto the socket, one message at a time.
    /// </summary>
    /// <remarks>
    /// The serialization buffer is reused across messages. At 60 Hz per focused viewer a fresh
    /// array per frame is real garbage for no reason, and a single pump per viewer means there is
    /// never a second writer to contend with it.
    /// </remarks>
    private static async Task SendAsync(ILiveSocket socket, LiveViewer viewer, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 8 * 1024);

        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await viewer.Queue.ReadAsync(cancellationToken).ConfigureAwait(false);

            buffer.ResetWrittenCount();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                JsonSerializer.Serialize(writer, message, LiveViewJson.Default);
            }

            await socket.SendAsync(buffer.WrittenMemory, binary: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CloseQuietlyAsync(ILiveSocket socket, WebSocketCloseStatus status, string description)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await socket.CloseAsync(status, description, timeout.Token).ConfigureAwait(false);
    }
}
