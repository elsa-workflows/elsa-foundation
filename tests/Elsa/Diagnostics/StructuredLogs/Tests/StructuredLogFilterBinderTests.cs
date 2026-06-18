using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Endpoints;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogFilterBinderTests
{
    private readonly StructuredLogFilterBinder _binder = new();

    [Fact]
    public void AppliesEveryProvidedParameter()
    {
        var filter = _binder.Bind(minLevel: "warning", category: "A.B", source: "svc", take: "10");

        Assert.Equal(LogLevel.Warning, filter.MinimumLevel);
        Assert.Equal("A.B", filter.Category);
        Assert.Equal("svc", filter.SourceId);
        Assert.Equal(10, filter.MaxCount);
    }

    [Fact]
    public void BlankParametersBecomeNoConstraints()
    {
        var filter = _binder.Bind(minLevel: null, category: "  ", source: "", take: null);

        Assert.Null(filter.MinimumLevel);
        Assert.Null(filter.Category);
        Assert.Null(filter.SourceId);
        Assert.Null(filter.MaxCount);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("None")]
    public void InvalidMinLevelThrowsDomainException(string value)
    {
        Assert.Throws<InvalidLogQueryException>(() => _binder.Bind(value, null, null, null));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    public void InvalidTakeThrowsDomainException(string value)
    {
        Assert.Throws<InvalidLogQueryException>(() => _binder.Bind(null, null, null, value));
    }
}
