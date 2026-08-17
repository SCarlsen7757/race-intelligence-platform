namespace RaceIntelligence.Collector.Plugins.Ingest.Upload;

/// <summary>
/// How many samples <see cref="TelemetryUploadService"/> currently holds in its open (not yet
/// uploaded) batch.
/// </summary>
/// <remarks>
/// Samples leave <see cref="Core.Buffering.ITelemetryBuffer"/> the moment the uploader reads them,
/// but they are not uploaded until the batch flushes on size or age — so up to
/// <see cref="IngestOptions.MaxBatchSize"/> of them exist in neither place as far as the buffer's
/// own metrics are concerned. Anything that needs to know "is everything uploaded yet" (currently
/// <see cref="TelemetryCollectorService"/>'s end-of-session flush) must add this to the buffer's
/// depth, or it will conclude the pipeline is drained while a full batch is still in flight.
/// </remarks>
public sealed class OpenBatchTracker
{
    private int _count;

    /// <summary>Samples currently sitting in the uploader's open batch.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Records the open batch's current size. Called by the uploader only.</summary>
    internal void Set(int count) => Volatile.Write(ref _count, count);
}
