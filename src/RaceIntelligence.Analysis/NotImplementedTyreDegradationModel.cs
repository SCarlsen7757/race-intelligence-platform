using RaceIntelligence.Core.Capabilities;

namespace RaceIntelligence.Analysis;

/// <summary>
/// Placeholder tyre degradation model. Declares the shape and requirements of a real model but
/// performs no analysis.
/// </summary>
/// <remarks>
/// <b>Phase 2 placeholder.</b> Present so the Analysis project has a concrete type proving the
/// capability-gating contract compiles end to end. It requires
/// <see cref="SimCapabilities.TyreWear"/>, so a session from a simulator without tyre wear
/// telemetry will never select it. Replace with a real deterministic model (Phase 2), then
/// potentially a trained model (Phase 4) — raw telemetry never changes to accommodate either.
/// </remarks>
public sealed class NotImplementedTyreDegradationModel : ITyreDegradationModel
{
    /// <inheritdoc />
    public string AlgorithmName => "Tyre Degradation Model";

    /// <inheritdoc />
    public Version AlgorithmVersion { get; } = new(0, 0, 0);

    /// <inheritdoc />
    public SimCapabilities RequiredCapabilities => SimCapabilities.TyreWear;

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always. Phase 2 has not been implemented yet.</exception>
    public TyreDegradationEstimate Analyze(StintAnalysisInput input)
        => throw new NotImplementedException("Tyre degradation analysis is a Phase 2 deliverable.");
}
