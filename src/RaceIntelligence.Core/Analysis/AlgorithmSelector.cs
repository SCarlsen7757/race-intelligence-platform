using RaceIntelligence.Core.Capabilities;

namespace RaceIntelligence.Core.Analysis;

/// <summary>
/// Filters algorithms by whether a session's capabilities satisfy what each algorithm requires.
/// </summary>
/// <remarks>
/// This is the concrete implementation of the platform's capability-check principle: rather than
/// analysis or strategy code asking "is this RaceRoom?", it asks the algorithm what it needs and
/// asks the session what it has, then only runs the algorithms that fit.
/// </remarks>
public static class AlgorithmSelector
{
    /// <summary>
    /// Returns the subset of <paramref name="algorithms"/> whose <see cref="IAlgorithmMetadata.RequiredCapabilities"/>
    /// are fully satisfied by <paramref name="available"/>.
    /// </summary>
    /// <typeparam name="T">The algorithm metadata type.</typeparam>
    /// <param name="algorithms">Candidate algorithms.</param>
    /// <param name="available">The capabilities available for the session being analyzed.</param>
    public static IEnumerable<T> Supported<T>(IEnumerable<T> algorithms, SimCapabilities available)
        where T : IAlgorithmMetadata
        => algorithms.Where(a => available.Has(a.RequiredCapabilities));
}
