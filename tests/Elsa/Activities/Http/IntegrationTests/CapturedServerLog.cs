using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Records warning-and-worse server-side log entries so a failed request can say why it failed.
/// <para>
/// The fixture serves requests through a TestServer, which turns an unhandled exception into a bare 500 with
/// an empty body. Without a provider the exception reaches nothing at all, and the test that follows fails on
/// a downstream symptom — "the collection was empty" — with the cause discarded. Issue #1297 cost a full
/// bisect for exactly that reason.
/// </para>
/// </summary>
public sealed class CapturedServerLog : ILoggerProvider
{
    private readonly ConcurrentQueue<string> entries = new();

    /// <summary>Captured entries, most recent last.</summary>
    public IReadOnlyList<string> Entries => entries.ToArray();

    /// <summary>The captured entries as a block, or empty when nothing was recorded.</summary>
    public string Report() => entries.IsEmpty
        ? string.Empty
        : $"{Environment.NewLine}Server log:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", entries)}";

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var detail = exception is null
                ? message
                // Innermost first: the outer frames are usually a pipeline wrapper, the inner one is the cause.
                : $"{message} [{string.Join(" <- ", Unwrap(exception).Select(x => $"{x.GetType().Name}: {x.Message}"))}]";
            entries.Enqueue($"{logLevel}: {category}: {detail}");
        }

        private static IEnumerable<Exception> Unwrap(Exception exception)
        {
            for (var current = exception; current is not null; current = current.InnerException)
                yield return current;
        }
    }
}
