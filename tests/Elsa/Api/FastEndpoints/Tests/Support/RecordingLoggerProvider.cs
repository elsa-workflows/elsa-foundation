using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Elsa.Api.FastEndpoints.Tests.Support;

/// <summary>
/// Minimal in-memory logger provider that records every log entry so tests can assert that the
/// per-shell "security disabled" warning is emitted exactly once and names the shell.
/// </summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<RecordedLog> Logs { get; } = new();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Logs);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<RecordedLog> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (exception is not null)
                message += " || EX: " + exception;
            logs.Enqueue(new RecordedLog(category, logLevel, message));
        }
    }
}

public sealed record RecordedLog(string Category, LogLevel Level, string Message);
