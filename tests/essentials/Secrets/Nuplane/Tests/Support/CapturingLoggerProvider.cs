using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Elsa.Secrets.Nuplane.Tests.Support;

/// <summary>
/// Every log line a composition emitted, in one string, so a sentinel test can assert a secret reached no
/// sink at all rather than only that it stayed out of a return value.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> entries = new();

    public IReadOnlyCollection<string> Entries => entries;

    public string AllText => string.Join(Environment.NewLine, entries);

    public ILogger CreateLogger(string categoryName) => new Capturing(categoryName, entries);

    public void Dispose()
    {
    }

    private sealed class Capturing(string category, ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Enqueue($"{logLevel} {category} {formatter(state, exception)} {exception}");
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
