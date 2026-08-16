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
        null, null, null, null, [], [], [], null, null, null, -1, null, -1, null, LiveDataTier.Observed);

    private static FocusFrameMessage Focus(int lap) => new(
        "room", "id:1", DateTimeOffset.UnixEpoch, 0, lap, 1, null, 0, null, null, 0, null, 0, 0, [], [], []);

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
    /// A frame for the previous driver delivered after a switch reads as a glitch in the new
    /// driver's traces — one sample from a different car, at a different point on track.
    /// </summary>
    [Fact]
    public void Clearing_the_focus_discards_a_frame_not_yet_sent()
    {
        var queue = new ViewerQueue();

        queue.OfferFocus(Focus(lap: 1));
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
