using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RaceIntelligence.Collector.Live;

/// <summary>
/// Consumer half of the live path: drains <see cref="LiveOutbox"/> onto a connection to the hub,
/// reconnecting for as long as the collector runs.
/// </summary>
/// <remarks>
/// <para>
/// The live counterpart of <see cref="Upload.TelemetryUploadService"/>, and independent of it by
/// design. A hub outage must not stall archiving, and a full archive buffer must not stall the live
/// view — they share only the collect loop that feeds them both, and that loop blocks on neither.
/// </para>
/// <para>
/// <b>A failed send is not a lost frame worth recovering.</b> Where the upload path retries,
/// buffers and drains before letting a session end, this one drops what it was holding and
/// reconnects. That is not a weaker version of the same policy — it is the correct policy for data
/// whose value expires in milliseconds. Re-sending a frame from before an outage would show a race
/// engineer a car where it was thirty seconds ago.
/// </para>
/// </remarks>
public sealed class LivePublishService(
    LiveOutbox outbox,
    ILiveConnectionFactory connectionFactory,
    IOptions<CollectorOptions> options,
    TimeProvider timeProvider,
    ILogger<LivePublishService> logger) : BackgroundService
{
    private readonly LiveOptions _live = options.Value.Live;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = _live.ReconnectDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await connectionFactory.ConnectAsync(stoppingToken).ConfigureAwait(false);

                // Only reset the backoff once a connection has been established, not on each
                // attempt: a hub that accepts a socket and immediately drops it would otherwise be
                // hammered at the minimum delay forever.
                backoff = _live.ReconnectDelay;

                await PumpAsync(connection, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The live publishing connection failed; reconnecting in {Backoff}. Archiving is unaffected.",
                    backoff);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(backoff, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Doubling, capped. Uncapped, a hub that was down for an hour would not be picked back
            // up until long after it returned — quite possibly after the race had finished.
            backoff = backoff < _live.MaxReconnectDelay
                ? TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _live.MaxReconnectDelay.Ticks))
                : _live.MaxReconnectDelay;
        }

        var (standings, self) = outbox.DroppedFrames;
        if (standings > 0 || self > 0)
        {
            logger.LogInformation(
                "Live publishing stopped. {Standings} standings and {Self} telemetry frames were superseded before they could be sent.",
                standings, self);
        }
    }

    /// <summary>
    /// Sends whatever the outbox has, for as long as the connection holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The session is re-announced first on every connection, not only when it starts. A hub learns
    /// which session a client is publishing from the announcement carried on that socket, so a
    /// reconnect mid-race would otherwise deliver standings for a session it has never heard of.
    /// </para>
    /// <para>
    /// A frame in flight when the socket fails is lost, and deliberately not recovered. For the two
    /// conflated streams that is the correct behaviour outright — something current replaces it
    /// within milliseconds. For the session announcement it is covered by the re-announce above. A
    /// lost goodbye is the one real gap, and it costs only that the hub expires the room on its
    /// timeout instead of immediately.
    /// </para>
    /// </remarks>
    private async Task PumpAsync(ILiveConnection connection, CancellationToken cancellationToken)
    {
        // Asks the outbox to queue the announcement rather than sending it here. Sending it
        // directly would race the outbox's own queue and could put it behind standings it is
        // supposed to precede — and would double-send it on the first connection, where the
        // session was announced before this loop existed.
        outbox.RequireSessionAnnouncement();

        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await outbox.ReadAsync(cancellationToken).ConfigureAwait(false);
            await connection.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }
}
