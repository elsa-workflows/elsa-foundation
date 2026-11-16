using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

internal static class TestEntries
{
    public static StructuredLogEntry Create(
        long sequence = 0,
        LogLevel level = LogLevel.Information,
        string category = "Test.Category",
        string sourceId = "test",
        string message = "message") =>
        new()
        {
            Sequence = sequence,
            Timestamp = DateTimeOffset.UnixEpoch,
            Level = level,
            Category = category,
            Message = message,
            SourceId = sourceId
        };
}
