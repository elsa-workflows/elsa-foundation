using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogFilterTests
{
    [Fact]
    public void EmptyFilterMatchesEverything()
    {
        var filter = Core.Models.StructuredLogFilter.None;
        Assert.True(filter.Matches(TestEntries.Create(level: LogLevel.Trace)));
    }

    [Theory]
    [InlineData(LogLevel.Information, LogLevel.Warning, true)]
    [InlineData(LogLevel.Information, LogLevel.Information, true)]
    [InlineData(LogLevel.Warning, LogLevel.Information, false)]
    public void MinimumLevelBoundary(LogLevel minimum, LogLevel entryLevel, bool expected)
    {
        var filter = new Core.Models.StructuredLogFilter { MinimumLevel = minimum };
        Assert.Equal(expected, filter.Matches(TestEntries.Create(level: entryLevel)));
    }

    [Fact]
    public void CategoryMatchesExactlyOnly()
    {
        var filter = new Core.Models.StructuredLogFilter { Category = "A.B" };
        Assert.True(filter.Matches(TestEntries.Create(category: "A.B")));
        Assert.False(filter.Matches(TestEntries.Create(category: "A.B.C")));
    }

    [Fact]
    public void SourceMatchesExactlyOnly()
    {
        var filter = new Core.Models.StructuredLogFilter { SourceId = "s1" };
        Assert.True(filter.Matches(TestEntries.Create(sourceId: "s1")));
        Assert.False(filter.Matches(TestEntries.Create(sourceId: "s2")));
    }

    [Fact]
    public void AllCriteriaMustHold()
    {
        var filter = new Core.Models.StructuredLogFilter
        {
            MinimumLevel = LogLevel.Warning,
            Category = "A",
            SourceId = "s1"
        };

        Assert.True(filter.Matches(TestEntries.Create(level: LogLevel.Error, category: "A", sourceId: "s1")));
        Assert.False(filter.Matches(TestEntries.Create(level: LogLevel.Error, category: "A", sourceId: "s2")));
    }
}
