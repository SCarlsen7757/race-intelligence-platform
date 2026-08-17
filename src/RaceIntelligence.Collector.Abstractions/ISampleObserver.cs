using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Collector.Abstractions;

/// <summary>Consumes the local car's telemetry at the collector's full poll rate.</summary>
/// <remarks>
/// <b>This runs on the collect loop and must return immediately.</b> At 60 Hz there are roughly
/// sixteen milliseconds between frames, and the loop is what reads the simulator — time spent here
/// is time the next frame is not being read, for every other plugin as well as this one. Enqueue
/// into whatever structure the plugin owns and return; do not perform I/O, and do not block on a
/// lock another thread can hold for long.
/// <para>
/// The one sanctioned exception is a bounded buffer configured to apply backpressure rather than
/// drop, which parks the loop on purpose when the archive cannot keep up. That is a deliberate
/// trade — slower collection over lost data — and it honours the cancellation token so shutdown can
/// still unpark it.
/// </para>
/// </remarks>
public interface ISampleObserver
{
    /// <summary>
    /// A telemetry sample has been read from the simulator.
    /// </summary>
    /// <param name="sample">The sample. The same instance is given to every observer; treat it as read-only.</param>
    /// <param name="cancellationToken">
    /// Cancelled at shutdown. Only meaningful to an implementation that can park the loop; a plugin
    /// that merely enqueues has nothing to observe it with.
    /// </param>
    void OnSample(TelemetrySample sample, CancellationToken cancellationToken);
}
