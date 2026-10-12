using Microsoft.Extensions.Logging;

namespace Elsa.Testing;

/// <summary>
/// One captured log call. <see cref="Fields"/> holds the structured state pairs when the state is structured
/// (LoggerMessage, message templates) and is empty otherwise.
/// </summary>
public sealed record LogEntry(
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, object?> Fields,
    Exception? Exception);

/// <summary>
/// <see cref="ILogger"/> that records every call so tests can assert on levels, event ids, messages, structured
/// fields and exceptions. Safe to share across threads.
/// </summary>
public class RecordingLogger : ILogger
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_entries)
                return _entries.ToArray();
        }
    }

    public IEnumerable<LogEntry> Warnings => Entries.Where(entry => entry.Level == LogLevel.Warning);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var fields = state is IEnumerable<KeyValuePair<string, object?>> structured
            ? structured.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        var entry = new LogEntry(logLevel, eventId, formatter(state, exception), fields, exception);
        lock (_entries)
            _entries.Add(entry);
    }
}

/// <inheritdoc cref="RecordingLogger"/>
public sealed class RecordingLogger<T> : RecordingLogger, ILogger<T>;
