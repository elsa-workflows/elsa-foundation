using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Endpoints;
using Microsoft.Extensions.Primitives;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public class OpenTelemetryTraceFilterBinderTests
{
    private readonly OpenTelemetryTraceFilterBinder _binder = new();

    [Fact]
    public void Bind_MapsRecognisedQueryValues()
    {
        var filter = _binder.Bind(new Dictionary<string, StringValues>
        {
            ["traceId"] = "trace-1",
            ["serviceName"] = "api",
            ["status"] = "error",
            ["take"] = "25"
        });

        Assert.Equal("trace-1", filter.TraceId);
        Assert.Equal("api", filter.ServiceName);
        Assert.Equal(SpanStatus.Error, filter.Status);
        Assert.Equal(25, filter.Take);
    }

    [Fact]
    public void Bind_WhenStatusIsInvalid_Throws()
    {
        Assert.Throws<InvalidTelemetryQueryException>(() => _binder.Bind(new Dictionary<string, StringValues>
        {
            ["status"] = "not-a-status"
        }));
    }

    [Fact]
    public void Bind_WhenTakeIsInvalid_Throws()
    {
        Assert.Throws<InvalidTelemetryQueryException>(() => _binder.Bind(new Dictionary<string, StringValues>
        {
            ["take"] = "-1"
        }));
    }

    [Fact]
    public void Bind_WhenFromIsAfterTo_Throws()
    {
        Assert.Throws<InvalidTelemetryQueryException>(() => _binder.Bind(new Dictionary<string, StringValues>
        {
            ["from"] = "2026-05-26T11:00:00Z",
            ["to"] = "2026-05-26T10:00:00Z"
        }));
    }

    [Fact]
    public void Bind_WhenEmpty_ReturnsEmptyFilter()
    {
        var filter = _binder.Bind(new Dictionary<string, StringValues>());

        Assert.Null(filter.TraceId);
        Assert.Null(filter.Status);
        Assert.Null(filter.Take);
    }
}
