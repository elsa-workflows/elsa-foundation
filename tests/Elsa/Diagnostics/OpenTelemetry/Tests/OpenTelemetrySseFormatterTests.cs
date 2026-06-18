using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Endpoints;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public class OpenTelemetrySseFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);
    private readonly OpenTelemetrySseFormatter _formatter = new(new OpenTelemetryStreamItemSerializer());

    [Fact]
    public void Format_WhenItemCarriesTrace_EmitsTraceEventFrame()
    {
        var trace = new TelemetryTrace("trace-1", "root", "name", Now, Now.AddMilliseconds(5), TimeSpan.FromMilliseconds(5), SpanStatus.Ok, ["resource-1"], [], 1);
        var item = new OpenTelemetryStreamItem { Trace = trace };

        var frame = _formatter.Format(item);

        Assert.StartsWith("event: trace\n", frame);
        Assert.Contains("\"traceId\":\"trace-1\"", frame);
        Assert.EndsWith("\n\n", frame);
    }

    [Fact]
    public void Format_WhenItemCarriesDropSignal_EmitsDroppedEventFrame()
    {
        var item = new OpenTelemetryStreamItem { DroppedItems = new(OpenTelemetrySignalType.Trace, 3, "SubscriberQueueFull") };

        var frame = _formatter.Format(item);

        Assert.StartsWith("event: dropped\n", frame);
        Assert.Contains("\"count\":3", frame);
    }

    [Fact]
    public void Format_WhenItemCarriesNoPayload_Throws()
    {
        Assert.Throws<ArgumentException>(() => _formatter.Format(new OpenTelemetryStreamItem()));
    }

    [Fact]
    public void Heartbeat_IsAnSseComment()
    {
        Assert.Equal(": keep-alive\n\n", _formatter.Heartbeat());
    }
}
