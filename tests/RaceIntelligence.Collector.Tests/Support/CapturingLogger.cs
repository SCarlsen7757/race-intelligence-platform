using Microsoft.Extensions.Logging;

namespace RaceIntelligence.Collector.Tests.Support;

/// <summary>
/// Minimal <see cref="ILogger{T}"/> that records what was logged, so a test can assert that a
/// knowingly-lossy path reported itself rather than failing silently.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
