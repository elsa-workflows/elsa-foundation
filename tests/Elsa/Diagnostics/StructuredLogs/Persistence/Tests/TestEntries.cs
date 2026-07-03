using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests;

internal static class TestEntries
{
    public static StructuredLogEntry Create(
        long sequence = 0,
        LogLevel level = LogLevel.Information,
        string category = "Test.Category",
        string sourceId = "test",
        string message = "message",
        IReadOnlyList<LogProperty>? properties = null,
        IReadOnlyList<LogScope>? scopes = null,
        LogExceptionDetails? exception = null) =>
        new()
        {
            Sequence = sequence,
            Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            Level = level,
            Category = category,
            Message = message,
            SourceId = sourceId,
            Properties = properties ?? [],
            Scopes = scopes ?? [],
            Exception = exception,
        };
}
