using RaceIntelligence.RaceRoom.Telemetry;
using RaceIntelligence.Collector.Abstractions.Telemetry;

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
    void OnSample(RaceRoomTelemetrySample sample, CancellationToken cancellationToken);

    /// <summary>
    /// No further samples will arrive. Called once when the source stops, and again as shutdown
    /// begins, so it must be safe to call more than once.
    /// </summary>
    /// <remarks>
    /// This exists for the backpressure case above, and is the reason it is on this interface rather
    /// than on the plugin. An observer parked inside <see cref="OnSample"/> holds the collect loop's
    /// thread and therefore cannot observe a cancellation token — the only thing that can release it
    /// is being told no more samples are coming. Without this, shutting down a collector whose
    /// buffer is full would wait out the host's whole shutdown timeout.
    /// </remarks>
    void OnSampleStreamCompleted();
}
