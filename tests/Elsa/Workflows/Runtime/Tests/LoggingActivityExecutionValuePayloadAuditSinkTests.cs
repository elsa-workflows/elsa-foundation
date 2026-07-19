using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class LoggingActivityExecutionValuePayloadAuditSinkTests
{
    [Fact]
    public async Task Records_attributable_resolution_metadata_without_a_payload()
    {
        var logger = new RecordingLogger<LoggingActivityExecutionValuePayloadAuditSink>();
        var sink = new LoggingActivityExecutionValuePayloadAuditSink(logger);
        var record = new ActivityExecutionValuePayloadAuditRecord(
            "wf-1",
            "activity-1",
            "evidence-1",
            "tenant:tenant-a",
            "profile-1",
            "subject-1",
            "request-1",
            "resolved",
            DateTimeOffset.UnixEpoch);

        await sink.RecordAsync(record);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.All(
            new[]
            {
                "resolved", "wf-1", "activity-1", "evidence-1", "tenant:tenant-a",
                "profile-1", "subject-1", "request-1"
            },
            value => Assert.Contains(value, entry.Message, StringComparison.Ordinal));
        Assert.DoesNotContain(entry.Fields, field => field.Key.Equals("Payload", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cancellation_is_propagated_before_logging()
    {
        var logger = new RecordingLogger<LoggingActivityExecutionValuePayloadAuditSink>();
        var sink = new LoggingActivityExecutionValuePayloadAuditSink(logger);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sink.RecordAsync(
                new(
                    "wf-1",
                    "activity-1",
                    "evidence-1",
                    "tenant:tenant-a",
                    "profile-1",
                    "subject-1",
                    "request-1",
                    "resolved",
                    DateTimeOffset.UnixEpoch),
                cancellation.Token).AsTask());

        Assert.Empty(logger.Entries);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new(
                logLevel,
                formatter(state, exception),
                state is IEnumerable<KeyValuePair<string, object?>> fields ? fields.ToArray() : []));

        public sealed record LogEntry(
            LogLevel Level,
            string Message,
            IReadOnlyList<KeyValuePair<string, object?>> Fields);
    }
}
