using System.Collections.Concurrent;
using RaceIntelligence.Collector.Abstractions;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Collector.Tests.Support;

/// <summary>
/// A plugin observer that records what it was told, and can be made to fail or to block on demand.
/// </summary>
/// <remarks>
/// Stands in for a whole plugin from the collect loop's point of view. The loop's contract is that
/// plugins are isolated from one another, so most tests here need two of these: one behaving badly
/// and one to prove it was unaffected.
/// </remarks>
internal sealed class RecordingObserver(
    string name,
    TimeSpan? standingsInterval = null,
    TimeSpan? extrasInterval = null)
    : ISessionObserver, ISampleObserver, IStandingsObserver, IExtrasObserver
{
    private readonly ConcurrentQueue<string> _calls = new();
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name => name;

    /// <summary>Every call this observer received, in order, as "operation" or "operation:detail".</summary>
    public IReadOnlyList<string> Calls => [.. _calls];

    /// <summary>Thrown from whichever operation is named, to prove one plugin failing does not affect another.</summary>
    public string? ThrowOn { get; init; }

    /// <summary>When set, <see cref="OnSample"/> blocks until the sample stream is reported complete.</summary>
    public bool BlockInOnSample { get; init; }

    /// <summary>Set once <see cref="OnSampleStreamCompleted"/> has been called.</summary>
    public bool StreamCompleted { get; private set; }

    public TimeSpan StandingsInterval { get; } = standingsInterval ?? TimeSpan.FromMilliseconds(100);

    public TimeSpan ExtrasInterval { get; } = extrasInterval ?? TimeSpan.FromSeconds(1);

    public ValueTask OnSessionStartedAsync(SessionInfo session, CancellationToken cancellationToken)
    {
        Record("sessionStarted", session.SessionId.ToString());
        return ValueTask.CompletedTask;
    }

    public ValueTask OnLapCompletedAsync(LapInfo lap, CancellationToken cancellationToken)
    {
        Record("lapCompleted", lap.LapNumber.ToString());
        return ValueTask.CompletedTask;
    }

    public ValueTask OnSessionEndedAsync(Guid sessionId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        Record("sessionEnded", sessionId.ToString());
        return ValueTask.CompletedTask;
    }

    public void OnSample(TelemetrySample sample, CancellationToken cancellationToken)
    {
        Record("sample", sample.SequenceNumber.ToString());

        if (BlockInOnSample)
        {
            // Deliberately ignores the token: this models a buffer applying backpressure, which
            // parks the calling thread and therefore cannot observe cancellation. Only being told
            // the stream is over can release it.
            _released.Task.GetAwaiter().GetResult();
        }
    }

    public void OnSampleStreamCompleted()
    {
        StreamCompleted = true;
        _released.TrySetResult();
        _calls.Enqueue("streamCompleted");
    }

    public void OnStandings(SessionStandings standings)
    {
        Record("standings", standings.Drivers.Count.ToString());
    }

    public void OnExtras(Guid sessionId, string extrasJson)
    {
        Record("extras", extrasJson);
    }

    private void Record(string operation, string? detail = null)
    {
        _calls.Enqueue(detail is null ? operation : $"{operation}:{detail}");

        if (string.Equals(ThrowOn, operation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name} was told to fail on {operation}.");
        }
    }
}
