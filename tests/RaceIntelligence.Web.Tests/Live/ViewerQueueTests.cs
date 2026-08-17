using RaceIntelligence.Live.Contracts.View;
using RaceIntelligence.Web.Live;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Live;

/// <summary>
/// Covers the promise that makes an open dashboard safe to run at 60 Hz: a viewer that cannot keep
/// up loses frames, and never anything else.
/// </summary>
public sealed class ViewerQueueTests
{
    private static TowerSnapshotMessage Tower(int driverCount) =>
        new("room", DateTimeOffset.UnixEpoch, [.. Enumerable.Range(0, driverCount).Select(Row)]);

    private static TowerRow Row(int index) => new(
        $"id:{index}", $"Driver {index}", null, null, null, index + 1, null, null, null, null, null,
        null, null, null, null, [], [], [], null, null, null, -1, -1, null, -1, null, LiveDataTier.Observed);

    private static FocusFrameMessage Focus(int lap) => new(
        "room", "id:1", DateTimeOffset.UnixEpoch, 0, lap, 1, null, 0, null, null, null, 0, null, 0, 0, [], [], []);

    private static ExtrasFrameMessage Extras() => new(
        "room", "id:1", DateTimeOffset.UnixEpoch, """{"damage":{"engine":0.5}}""");

    private static LapHistoryMessage History(string driverKey, int laps) => new(
        "room",
        driverKey,
        [.. Enumerable.Range(1, laps).Select(number => new LapRecord(number, 90_000, [], true))],
        Truncated: false);

    /// <summary>
    /// The reason each stream keeps a single slot: a viewer that stalls for two seconds must be
    /// shown the current race when it recovers, not two seconds of where the cars used to be.
    /// </summary>
    [Fact]
    public void Only_the_newest_tower_snapshot_survives()
    {
        var queue = new ViewerQueue();

        queue.OfferTower(Tower(driverCount: 1));
        queue.OfferTower(Tower(driverCount: 2));
        queue.OfferTower(Tower(driverCount: 3));

        queue.TryRead().ShouldBeOfType<TowerSnapshotMessage>().Drivers.Count.ShouldBe(3);
        queue.TryRead().ShouldBeNull();
        queue.DroppedFrames.Tower.ShouldBe(2);
    }

    [Fact]
    public void Only_the_newest_focus_frame_survives()
    {
        var queue = new ViewerQueue();

        queue.OfferFocus(Focus(lap: 1));
        queue.OfferFocus(Focus(lap: 2));

        queue.TryRead().ShouldBeOfType<FocusFrameMessage>().LapNumber.ShouldBe(2);
        queue.DroppedFrames.Focus.ShouldBe(1);
    }

    /// <summary>
    /// Errors answer a viewer's own commands, and each one says something no later message repeats.
    /// Conflating them would leave a viewer that made two bad requests told about only one.
    /// </summary>
    [Fact]
    public void Errors_queue_rather_than_replacing_each_other()
    {
        var queue = new ViewerQueue();

        queue.OfferError(new LiveErrorMessage(LiveErrorCodes.UnknownRoom, "first"));
        queue.OfferError(new LiveErrorMessage(LiveErrorCodes.UnknownDriver, "second"));

        queue.TryRead().ShouldBeOfType<LiveErrorMessage>().Message.ShouldBe("first");
        queue.TryRead().ShouldBeOfType<LiveErrorMessage>().Message.ShouldBe("second");
    }

    [Fact]
    public void Errors_are_delivered_ahead_of_data()
    {
        var queue = new ViewerQueue();

        queue.OfferTower(Tower(driverCount: 1));
        queue.OfferFocus(Focus(lap: 1));
        queue.OfferError(new LiveErrorMessage(LiveErrorCodes.UnknownDriver, "no such driver"));

        queue.TryRead().ShouldBeOfType<LiveErrorMessage>();
    }

    /// <summary>
    /// The tower arrives at a tenth of the focus stream's rate, so preferring focus would let a slow
    /// viewer starve its timing tower completely — while a focus frame skipped now is replaced
    /// within milliseconds.
    /// </summary>
    [Fact]
    public void The_tower_is_delivered_ahead_of_the_focus_stream()
    {
        var queue = new ViewerQueue();

        queue.OfferFocus(Focus(lap: 1));
        queue.OfferTower(Tower(driverCount: 1));

        queue.TryRead().ShouldBeOfType<TowerSnapshotMessage>();
        queue.TryRead().ShouldBeOfType<FocusFrameMessage>();
    }

    /// <summary>
    /// Below the tower and above the focus stream. Lap history is the least replaceable of the three
    /// — the next one does not arrive until the driver finishes another lap — so putting it under
    /// the 60 Hz stream would let a slow viewer starve it for a whole lap at a time.
    /// </summary>
    [Fact]
    public void Lap_history_sits_between_the_tower_and_the_focus_stream()
    {
        var queue = new ViewerQueue();

        queue.OfferFocus(Focus(lap: 1));
        queue.OfferLapHistory(History("id:1", laps: 1));
        queue.OfferTower(Tower(driverCount: 1));

        queue.TryRead().ShouldBeOfType<TowerSnapshotMessage>();
        queue.TryRead().ShouldBeOfType<LapHistoryMessage>();
        queue.TryRead().ShouldBeOfType<FocusFrameMessage>();
    }

    /// <summary>
    /// Conflating within a driver is safe precisely because each message is a full history: the one
    /// that survives says everything the ones it replaced did.
    /// </summary>
    [Fact]
    public void Only_the_newest_lap_history_per_driver_survives()
    {
        var queue = new ViewerQueue();

        queue.OfferLapHistory(History("id:1", laps: 1));
        queue.OfferLapHistory(History("id:1", laps: 2));
        queue.OfferLapHistory(History("id:1", laps: 3));

        queue.TryRead().ShouldBeOfType<LapHistoryMessage>().Laps.Count.ShouldBe(3);
        queue.TryRead().ShouldBeNull();
    }

    /// <summary>
    /// One slot per driver, not one in total: several rows can be expanded at once, and a single slot
    /// would let a busy driver's history evict another driver's indefinitely.
    /// </summary>
    [Fact]
    public void Lap_histories_for_different_drivers_do_not_evict_each_other()
    {
        var queue = new ViewerQueue();

        queue.OfferLapHistory(History("id:1", laps: 1));
        queue.OfferLapHistory(History("id:2", laps: 1));

        var delivered = new[]
        {
            queue.TryRead().ShouldBeOfType<LapHistoryMessage>().DriverKey,
            queue.TryRead().ShouldBeOfType<LapHistoryMessage>().DriverKey,
        };

        delivered.ShouldBe(["id:1", "id:2"], ignoreOrder: true);
        queue.TryRead().ShouldBeNull();
    }

    [Fact]
    public void Clearing_one_driver_leaves_the_others_alone()
    {
        var queue = new ViewerQueue();

        queue.OfferLapHistory(History("id:1", laps: 1));
        queue.OfferLapHistory(History("id:2", laps: 1));
        queue.ClearLapHistory("id:1");

        queue.TryRead().ShouldBeOfType<LapHistoryMessage>().DriverKey.ShouldBe("id:2");
        queue.TryRead().ShouldBeNull();
    }

    /// <summary>
    /// Extras go at the very bottom, below even the 60 Hz stream. A once-a-second document
    /// interrupting the trace a race engineer is reading, to deliver a number that will look the
    /// same next second, is the one trade this ladder is not worth making.
    /// </summary>
    [Fact]
    public void Extras_are_delivered_last_so_the_focus_stream_keeps_its_rate()
    {
        var queue = new ViewerQueue();

        queue.OfferExtras(Extras());
        queue.OfferFocus(Focus(lap: 1));
        queue.OfferTower(Tower(driverCount: 1));

        queue.TryRead().ShouldBeOfType<TowerSnapshotMessage>();
        queue.TryRead().ShouldBeOfType<FocusFrameMessage>();
        queue.TryRead().ShouldBeOfType<ExtrasFrameMessage>();
    }

    /// <summary>
    /// A frame for the previous driver delivered after a switch reads as a glitch in the new
    /// driver's traces — one sample from a different car, at a different point on track.
    /// </summary>
    [Fact]
    public void Clearing_the_focus_discards_a_frame_not_yet_sent()
    {
        var queue = new ViewerQueue();

        queue.OfferFocus(Focus(lap: 1));

        // The extras slot follows the focus, for the same reason: a damage panel still showing the
        // previous driver's car after a switch is worse than one showing nothing, because it looks
        // current.
        queue.OfferExtras(Extras());
        queue.ClearFocus();

        queue.TryRead().ShouldBeNull();
    }

    [Fact]
    public async Task ReadAsync_waits_until_something_is_offered()
    {
        var queue = new ViewerQueue();
        var pending = queue.ReadAsync(TestContext.Current.CancellationToken);

        pending.IsCompleted.ShouldBeFalse("nothing has been offered yet.");

        queue.OfferTower(Tower(driverCount: 1));

        (await pending).ShouldBeOfType<TowerSnapshotMessage>();
    }

    /// <summary>
    /// The one absolute requirement. The caller is a publisher's receive loop fanning a frame out to
    /// every viewer in a room: a single stalled viewer able to block it would stop the hub reading
    /// from the collector's socket at all.
    /// </summary>
    [Fact]
    public void Offering_never_blocks_even_when_nothing_is_reading()
    {
        var queue = new ViewerQueue();

        for (int i = 0; i < 10_000; i++)
        {
            queue.OfferTower(Tower(driverCount: 20));
            queue.OfferFocus(Focus(lap: i));
            queue.OfferError(new LiveErrorMessage(LiveErrorCodes.MalformedCommand, "noise"));
        }

        // Returning at all is the assertion. Memory is bounded too: two slots and a capped error
        // channel, whatever the offered rate.
        queue.TryRead().ShouldNotBeNull();
        queue.DroppedFrames.Tower.ShouldBe(9_999);
    }
}
