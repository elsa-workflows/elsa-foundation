using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests;

/// <summary>Thread-safe logger double: the drain loop and channel-shed callbacks log from other threads.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
            _entries.Add((logLevel, formatter(state, exception), exception));
    }

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Snapshot()
    {
        lock (_entries)
            return _entries.ToList();
    }
}
