using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RaceIntelligence.Core.Buffering;
using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Collector.Buffering;

/// <summary>
/// <see cref="ITelemetryBuffer"/> implementation backed by a bounded
/// <see cref="System.Threading.Channels.Channel{T}"/>, kept entirely in process memory.
/// </summary>
/// <remarks>
/// <para>
/// <b>In-memory only.</b> A crash, a reboot, or an outage that outlasts
/// <see cref="CollectorOptions.BufferCapacity"/> loses every sample still queued here. That is the
/// accepted Phase 1 gap documented on <see cref="ITelemetryBuffer"/>; this type is the first,
/// non-durable implementation of it.
/// </para>
/// <para>
/// <b>Full-mode trade-off:</b> <see cref="BoundedChannelFullMode.Wait"/> (the default, see
/// <see cref="CollectorOptions.BufferFullMode"/>) makes <see cref="TryWrite"/> block the calling
/// thread until the reader (<see cref="Upload.TelemetryUploadService"/>) frees up space — this
/// yields backpressure that protects every sample, at the cost of potentially stalling whatever is
/// calling <see cref="TryWrite"/> (the poll loop driving <see cref="TelemetryCollectorService"/>)
/// for as long as the outage lasts. <see cref="BoundedChannelFullMode.DropWrite"/> is the opposite
/// trade: the poll loop never stalls, but samples are silently lost once the buffer is full — this
/// implementation always turns that into a logged warning and an incremented
/// <see cref="BufferMetrics.TotalDropped"/> rather than a truly silent drop, per the platform's
/// "raw data is permanent" principle: it must not be raw data lost silently where it can be
/// reported instead. <see cref="BoundedChannelFullMode.Wait"/> is the default because losing no
/// samples is worth more than a stalled poll loop for a single, low-throughput producer like a sim
/// telemetry poll — the poll loop resumes as soon as the outage clears and the reader catches up.
/// </para>
/// </remarks>
public sealed class ChannelTelemetryBuffer : ITelemetryBuffer
{
    private readonly Channel<TelemetrySample> _channel;
    private readonly ILogger<ChannelTelemetryBuffer> _logger;
    private readonly BoundedChannelFullMode _fullMode;
    private readonly int _capacity;

    // Cancels a producer parked inside a backpressure-blocking TryWrite. Without it, shutdown with
    // a full buffer deadlocks: the blocking write owns the producer's thread, so the producer can
    // never observe its own stopping token, and the host waits out its whole ShutdownTimeout.
    private readonly CancellationTokenSource _shutdown = new();

    private long _totalWritten;
    private long _totalRead;
    private long _totalDropped;

    /// <summary>Creates a buffer with the given capacity and full-buffer behaviour.</summary>
    /// <param name="capacity">Maximum number of samples held before <paramref name="fullMode"/> takes effect. Must be positive.</param>
    /// <param name="fullMode">
    /// How to behave once <paramref name="capacity"/> is reached. Only <see cref="BoundedChannelFullMode.Wait"/>
    /// and <see cref="BoundedChannelFullMode.DropWrite"/> are meaningful here — see the class remarks.
    /// </param>
    /// <param name="logger">Logger used to report dropped samples.</param>
    public ChannelTelemetryBuffer(int capacity, BoundedChannelFullMode fullMode, ILogger<ChannelTelemetryBuffer> logger)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        ArgumentNullException.ThrowIfNull(logger);

        _capacity = capacity;
        _fullMode = fullMode;
        _logger = logger;

        // SingleWriter: only TelemetryCollectorService ever writes. SingleReader: false —
        // TelemetryUploadService's batch-by-size-or-age loop can have two overlapping
        // WaitToReadAsync calls in flight across consecutive iterations (one raced against a
        // Task.Delay and abandoned when the delay wins), which the single-reader fast path does
        // not support.
        _channel = Channel.CreateBounded<TelemetrySample>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // the underlying channel always uses Wait; DropWrite semantics are implemented explicitly below so a drop can be logged (see TryWrite).
            SingleWriter = true,
            SingleReader = false,
        });
    }

    /// <inheritdoc />
    public bool TryWrite(TelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        if (_fullMode == BoundedChannelFullMode.Wait)
        {
            // Block the calling thread (deliberately — see class remarks) until space frees up, the
            // buffer completes, or shutdown is signalled. This is the one place this type's
            // behaviour diverges from ITelemetryBuffer.TryWrite's "without blocking" doc comment: a
            // synchronous TryWrite has no other way to apply real backpressure, and
            // CollectorOptions.BufferFullMode defaults to Wait specifically so this path is the
            // common case. Passing _shutdown.Token is what keeps that divergence bounded — Complete
            // or DisposeAsync unparks the producer instead of leaving it stuck until the host's
            // ShutdownTimeout expires.
            try
            {
                _channel.Writer.WriteAsync(sample, _shutdown.Token).AsTask().GetAwaiter().GetResult();
                Interlocked.Increment(ref _totalWritten);
                return true;
            }
            catch (Exception ex) when (ex is ChannelClosedException or OperationCanceledException or ObjectDisposedException)
            {
                Interlocked.Increment(ref _totalDropped);
                return false;
            }
        }

        // DropWrite: the channel itself was created with FullMode.Wait, so its own TryWrite
        // returns false (rather than silently discarding) exactly when full — check depth first
        // so a full buffer is reported as a drop instead of a transient false from something else.
        bool wasFull = _channel.Reader.Count >= _capacity;
        bool written = !wasFull && _channel.Writer.TryWrite(sample);
        if (written)
        {
            Interlocked.Increment(ref _totalWritten);
            return true;
        }

        Interlocked.Increment(ref _totalDropped);
        _logger.LogWarning(
            "Telemetry buffer full (capacity {Capacity}); dropped sample {SequenceNumber} for session {SessionId}. " +
            "BufferFullMode is DropWrite — switch to Wait to trade poll-loop stalls for zero sample loss.",
            _capacity, sample.SequenceNumber, sample.SessionId);
        return false;
    }

    /// <inheritdoc />
    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.WaitToReadAsync(cancellationToken);

    /// <inheritdoc />
    public bool TryRead(out TelemetrySample sample)
    {
        if (_channel.Reader.TryRead(out var read))
        {
            sample = read;
            Interlocked.Increment(ref _totalRead);
            return true;
        }

        sample = null!;
        return false;
    }

    /// <inheritdoc />
    /// <remarks>Also unparks a producer currently blocked in <see cref="TryWrite"/>; that write is counted as dropped.</remarks>
    public void Complete()
    {
        _channel.Writer.TryComplete();
        _shutdown.Cancel();
    }

    /// <inheritdoc />
    public BufferMetrics Metrics => new(
        Interlocked.Read(ref _totalWritten),
        Interlocked.Read(ref _totalRead),
        Interlocked.Read(ref _totalDropped),
        _channel.Reader.Count);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Complete();
        _shutdown.Dispose();
        return ValueTask.CompletedTask;
    }
}
