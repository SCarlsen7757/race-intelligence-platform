namespace RaceIntelligence.ML;

/// <summary>
/// Identifies a trained model artifact and the data it was trained on.
/// </summary>
/// <param name="ModelName">Stable name of the model (e.g. "LapTimeTrend").</param>
/// <param name="ModelVersion">Version of the trained artifact.</param>
/// <param name="TrainedAtUtc">When training completed.</param>
/// <param name="TrainingSessionCount">How many sessions contributed to training.</param>
public sealed record TrainedModelDescriptor(
    string ModelName,
    Version ModelVersion,
    DateTimeOffset TrainedAtUtc,
    int TrainingSessionCount);

/// <summary>
/// Trains a model from historical telemetry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not implemented.</b> Machine learning is an enhancement, not a requirement — the analysis
/// and strategy layers run on deterministic algorithms alone.
/// </para>
/// <para>
/// Training reads the immutable raw telemetry and writes only new model artifacts. Because raw
/// data is never modified, a future model can be retrained against history collected long before
/// that model was conceived.
/// </para>
/// </remarks>
public interface IModelTrainer
{
    /// <summary>Trains a model over the given sessions and returns a descriptor of the artifact.</summary>
    Task<TrainedModelDescriptor> TrainAsync(
        IReadOnlyList<Guid> sessionIds,
        CancellationToken cancellationToken = default);
}
