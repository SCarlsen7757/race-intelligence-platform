using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using RaceIntelligence.Collector.Buffering;
using RaceIntelligence.Collector.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Collector.Tests.Buffering;

public class ChannelTelemetryBufferTests
{
    private static ChannelTelemetryBuffer CreateBuffer(int capacity, BoundedChannelFullMode fullMode) =>
        new(capacity, fullMode, NullLogger<ChannelTelemetryBuffer>.Instance);

    [Fact]
    public async Task TryWrite_then_TryRead_round_trips_the_same_sample()
    {
        await using var buffer = CreateBuffer(capacity: 10, BoundedChannelFullMode.Wait);
        var sample = TelemetrySampleFactory.Create(Guid.NewGuid(), sequenceNumber: 42);

        bool written = buffer.TryWrite(sample);
        bool read = buffer.TryRead(out var readBack);

        written.ShouldBeTrue();
        read.ShouldBeTrue();
        readBack.ShouldBe(sample);
    }

    [Fact]
    public async Task Metrics_track_writes_reads_and_current_depth_accurately()
    {
        await using var buffer = CreateBuffer(capacity: 10, BoundedChannelFullMode.Wait);
        var sessionId = Guid.NewGuid();

        buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 0));
        buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 1));
        buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 2));
        buffer.TryRead(out _);

        var metrics = buffer.Metrics;
        metrics.TotalWritten.ShouldBe(3);
        metrics.TotalRead.ShouldBe(1);
        metrics.TotalDropped.ShouldBe(0);
        metrics.CurrentDepth.ShouldBe(2);
    }

    [Fact]
    public async Task DropWrite_mode_drops_and_counts_samples_once_capacity_is_reached()
    {
        await using var buffer = CreateBuffer(capacity: 2, BoundedChannelFullMode.DropWrite);
        var sessionId = Guid.NewGuid();

        bool first = buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 0));
        bool second = buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 1));
        bool third = buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 2));

        first.ShouldBeTrue();
        second.ShouldBeTrue();
        third.ShouldBeFalse();

        var metrics = buffer.Metrics;
        metrics.TotalWritten.ShouldBe(2);
        metrics.TotalDropped.ShouldBe(1);
        metrics.CurrentDepth.ShouldBe(2);
    }

    [Fact]
    public async Task Wait_mode_blocks_the_writer_until_the_reader_drains_space()
    {
        await using var buffer = CreateBuffer(capacity: 1, BoundedChannelFullMode.Wait);
        var sessionId = Guid.NewGuid();

        buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 0)).ShouldBeTrue();

        // The buffer is now full; a second TryWrite must block rather than drop.
        var writerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWrite = Task.Run(() =>
        {
            writerEntered.SetResult();
            return buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 1));
        });

        // Wait on the writer actually having started rather than on a fixed sleep. The short grace
        // that follows only backs the negative assertion ("it has not completed"), which cannot be
        // expressed as a signal -- it is not racing the logic to reach a positive result.
        await writerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var completedEarly = await Task.WhenAny(secondWrite, Task.Delay(TimeSpan.FromMilliseconds(100)));
        completedEarly.ShouldNotBe(secondWrite, "a full buffer in Wait mode must block the writer, not drop the sample.");
        buffer.Metrics.TotalWritten.ShouldBe(1);

        // Freeing one slot is the only thing that may release it.
        buffer.TryRead(out _).ShouldBeTrue();

        (await secondWrite.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).ShouldBeTrue();

        var metrics = buffer.Metrics;
        metrics.TotalWritten.ShouldBe(2);
        metrics.TotalDropped.ShouldBe(0);
    }

    [Fact]
    public async Task Complete_releases_a_writer_already_blocked_on_a_full_buffer()
    {
        // Shutdown path: the producer is parked inside a blocking TryWrite that owns its thread, so
        // it can never observe a cancellation token itself. Complete must unpark it, or the host
        // waits out its entire ShutdownTimeout.
        await using var buffer = CreateBuffer(capacity: 1, BoundedChannelFullMode.Wait);
        var sessionId = Guid.NewGuid();

        buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 0)).ShouldBeTrue();

        var writerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockedWrite = Task.Run(() =>
        {
            writerEntered.SetResult();
            return buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 1));
        });

        await writerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        buffer.Complete();

        (await blockedWrite.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
            .ShouldBeFalse("the unparked write did not store the sample, so it must report a drop.");
        buffer.Metrics.TotalDropped.ShouldBe(1);
    }

    [Fact]
    public async Task Complete_lets_readers_drain_remaining_samples_then_stop()
    {
        await using var buffer = CreateBuffer(capacity: 10, BoundedChannelFullMode.Wait);
        var sessionId = Guid.NewGuid();
        buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 0));
        buffer.TryWrite(TelemetrySampleFactory.Create(sessionId, 1));

        buffer.Complete();

        (await buffer.WaitToReadAsync()).ShouldBeTrue();
        buffer.TryRead(out _).ShouldBeTrue();
        (await buffer.WaitToReadAsync()).ShouldBeTrue();
        buffer.TryRead(out _).ShouldBeTrue();

        (await buffer.WaitToReadAsync()).ShouldBeFalse();
        buffer.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task TryWrite_after_Complete_is_dropped_rather_than_blocking_forever()
    {
        await using var buffer = CreateBuffer(capacity: 10, BoundedChannelFullMode.Wait);
        buffer.Complete();

        var task = Task.Run(() => buffer.TryWrite(TelemetrySampleFactory.Create(Guid.NewGuid(), 0)));
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

        completed.ShouldBe(task);
        (await task).ShouldBeFalse();
        buffer.Metrics.TotalDropped.ShouldBe(1);
    }
}
