using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Endpoints;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogReplayCursorPublicSurfaceTests
{
    [Fact]
    public void Cursor_rejects_nul_and_parse_rejects_default_or_nul_wire_values()
    {
        Assert.Throws<ArgumentException>(() => new StructuredLogReplayCursor("slrc1.test.bad\0cursor"));
        Assert.False(default(StructuredLogReplayCursor).IsValid);
        Assert.False(StructuredLogReplayCursor.TryParse("slrc1.test.bad\0cursor", out _));
    }

    [Fact]
    public void Formatter_rejects_default_cursor_and_valid_cursor_round_trips_through_sse_id()
    {
        var formatter = new StructuredLogSseFormatter(new StructuredLogEntrySerializer());
        var invalid = TestEntries.Create() with { ReplayCursor = default(StructuredLogReplayCursor) };
        Assert.Throws<ArgumentException>(() => formatter.FormatEntry(invalid));

        var cursor = new StructuredLogReplayCursor("slrc1.test.valid-cursor");
        var frame = formatter.FormatEntry(TestEntries.Create() with { ReplayCursor = cursor });
        var wireValue = frame.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]["id: ".Length..];

        Assert.True(StructuredLogReplayCursor.TryParse(wireValue, out var parsed));
        Assert.Equal(cursor, parsed);
    }

    [Fact]
    public void CoreExportsOnlyTheOpaqueReplayCursorValue()
    {
        var exportedTypes = typeof(StructuredLogReplayCursor).Assembly.GetExportedTypes();

        Assert.DoesNotContain(exportedTypes, type => type.Name.Contains("CursorCodec", StringComparison.Ordinal));
        Assert.DoesNotContain(exportedTypes, type => type.Name.Contains("CursorParts", StringComparison.Ordinal));
        Assert.DoesNotContain(
            exportedTypes.SelectMany(type => type.GetMembers()),
            member => member.Name is "ProviderPosition" or "RecordToken");
    }
}
