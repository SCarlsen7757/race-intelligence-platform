using Microsoft.Extensions.Options;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// Sweeps out rooms nothing is publishing into any more.
/// </summary>
/// <remarks>
/// A timer rather than expiry-on-read, because the dashboard's landing view has to be correct
/// when nobody is looking at it. A room only checked when someone asks for it would sit in the
/// list of a viewer who happened to be connected throughout, and vanish for one who reloaded —
/// two people watching the same page seeing different sessions.
/// </remarks>
public sealed class LiveRoomJanitor(
    LiveRoomRegistry rooms,
    IOptions<LiveHubOptions> options,
    TimeProvider timeProvider,
    ILogger<LiveRoomJanitor> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.RoomSweepInterval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    rooms.RemoveExpiredRooms();
                }
                catch (Exception ex)
                {
                    // A failed sweep must not end the loop. The cost of one skipped sweep is a room
                    // lingering a few seconds; the cost of the loop stopping is that no room is ever
                    // cleaned up again for the life of the process.
                    logger.LogError(ex, "A room sweep failed. Expired rooms will be retried on the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
