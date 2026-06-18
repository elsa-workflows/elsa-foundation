using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Endpoints;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogSseFormatterTests
{
    private readonly StructuredLogSseFormatter _formatter = new(new StructuredLogEntrySerializer());

    [Fact]
    public void EntryFrameCarriesIdEventAndData()
    {
        var frame = _formatter.Format(StructuredLogStreamItem.ForEntry(TestEntries.Create(sequence: 7)));

        Assert.StartsWith("id: 7\n", frame);
        Assert.Contains("event: entry\n", frame);
        Assert.Contains("data: {", frame);
        Assert.EndsWith("\n\n", frame);
    }

    [Fact]
    public void DroppedFrameHasNoIdAndUsesDroppedEvent()
    {
        var frame = _formatter.Format(StructuredLogStreamItem.ForDropped(new DroppedEntriesSignal(3, DateTimeOffset.UnixEpoch)));

        Assert.DoesNotContain("id:", frame);
        Assert.Contains("event: dropped\n", frame);
        Assert.Contains("\"droppedCount\":3", frame);
        Assert.EndsWith("\n\n", frame);
    }

    [Fact]
    public void HeartbeatIsAnSseComment()
    {
        Assert.Equal(": keep-alive\n\n", _formatter.Heartbeat());
    }
}
