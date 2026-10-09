using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Elsa.Workflows.Runtime.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Elsa.Secrets.Workflows.Tests.Support;

/// <summary>
/// The canary's runtime-captured logs surface (spec 188, T079): a logger provider for every category at every level,
/// which keeps each line's rendered message, its state's names and values and the full text of any exception it
/// carries, so a value that reaches a log line through any of them is found.
/// </summary>
public sealed class CanaryLogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    /// <summary>Every captured line, in the order they were written.</summary>
    public IReadOnlyCollection<string> Lines => _lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = new StringBuilder()
                .Append(logLevel).Append(' ').Append(category).Append(' ').Append(eventId.Id).Append(' ')
                .Append(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var (name, value) in values)
                    line.Append(" | ").Append(name).Append('=').Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
            }

            if (exception is not null)
                line.Append(" | exception=").Append(exception);
            lines.Enqueue(line.ToString());
        }
    }
}

/// <summary>One finished span of the runtime's activity source, as an exporter would see it.</summary>
public sealed record CanarySpan(
    string Name,
    IReadOnlyDictionary<string, string?> Tags,
    IReadOnlyList<string> Events,
    ActivityStatusCode Status,
    string? StatusDescription)
{
    public string? WorkflowExecutionId => Tags.GetValueOrDefault(WorkflowEngineTelemetry.WorkflowExecutionIdTag);

    /// <summary>Everything the span carries, as one text to scan.</summary>
    public string Render() =>
        string.Join('\n', [Name, Status.ToString(), StatusDescription ?? string.Empty, .. Tags.Select(tag => $"{tag.Key}={tag.Value}"), .. Events]);
}

/// <summary>
/// The canary's runtime telemetry export surface (spec 188, T079): an <see cref="ActivityListener"/> on the runtime's
/// engine activity source that records every finished span with its name, tags, events and status, as
/// <c>RuntimeEngineTracingTests</c> does. A listener is process-wide, so it records the spans of every runtime in the
/// process while it is attached; the canary scans them all and finds its own by their workflow execution id tag.
/// </summary>
public sealed class CanarySpanRecorder : IDisposable
{
    private readonly ConcurrentQueue<CanarySpan> _spans = new();
    private readonly ActivityListener _listener;

    public CanarySpanRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WorkflowEngineTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _spans.Enqueue(new CanarySpan(
                activity.OperationName,
                activity.TagObjects.ToDictionary(tag => tag.Key, tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture)),
                activity.Events.Select(@event => $"{@event.Name} {string.Join(' ', @event.Tags.Select(tag => $"{tag.Key}={tag.Value}"))}").ToArray(),
                activity.Status,
                activity.StatusDescription))
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Every span recorded so far.</summary>
    public IReadOnlyCollection<CanarySpan> Spans => _spans.ToArray();

    public void Dispose() => _listener.Dispose();
}
