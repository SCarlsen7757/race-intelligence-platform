using System.Runtime.CompilerServices;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Games;
using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Collector.Tests.Support;

/// <summary>
/// Fake <see cref="ITelemetrySource"/> that yields a fixed, caller-supplied sequence of events and
/// then idles (honoring cancellation) — simulates a connected source with nothing further to report
/// until the test tears the hosted service down.
/// </summary>
internal sealed class ScriptedTelemetrySource(IReadOnlyList<TelemetryEvent> events) : ITelemetrySource
{
    public GameVersionIdentity? Version => null;

    public SimCapabilities Capabilities => SimCapabilities.None;

    public ConnectionState State => ConnectionState.InSession;

    public async IAsyncEnumerable<TelemetryEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var telemetryEvent in events)
        {
            // Yield before every event so this behaves like a real polling source: its
            // MoveNextAsync completes asynchronously, which is what lets a consuming
            // BackgroundService's ExecuteAsync return to StartAsync instead of running the whole
            // script inline on the caller's thread.
            await Task.Yield();
            yield return telemetryEvent;
        }

        // Nothing more to report; idle until the test cancels/stops the hosted service.
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
