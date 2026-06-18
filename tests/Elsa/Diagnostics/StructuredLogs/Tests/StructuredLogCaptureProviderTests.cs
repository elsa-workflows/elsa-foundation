using Elsa.Diagnostics.StructuredLogs.Capture;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogCaptureProviderTests
{
    private sealed class CollectingSink : IStructuredLogSink
    {
        public List<StructuredLogEntry> Entries { get; } = [];
        public void Emit(StructuredLogEntry entry) => Entries.Add(entry);
    }

    private sealed class ThrowingSink : IStructuredLogSink
    {
        public void Emit(StructuredLogEntry entry) => throw new InvalidOperationException("sink failed");
    }

    private sealed class FixedSourceProvider : IStructuredLogSourceProvider
    {
        private readonly LogSource _source = new() { Id = "test-source", DisplayName = "Test" };
        public LogSource GetLocalSource() => _source;
        public IReadOnlyList<LogSource> GetKnownSources() => [_source];
    }

    private static StructuredLogCaptureProvider CreateProvider(IStructuredLogSink sink, Action<Core.Options.StructuredLogsOptions>? configure = null) =>
        new(sink, new FixedSourceProvider(), TestOptions.Create(configure));

    [Fact]
    public void MapsCoreFieldsAndStampsSource()
    {
        var sink = new CollectingSink();
        var logger = CreateProvider(sink).CreateLogger("My.Category");

        logger.LogWarning(new EventId(7, "Evt"), "Retried {Attempt}", 2);

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("My.Category", entry.Category);
        Assert.Equal(7, entry.EventId);
        Assert.Equal("Evt", entry.EventName);
        Assert.Equal("Retried 2", entry.Message);
        Assert.Equal("Retried {Attempt}", entry.MessageTemplate);
        Assert.Contains(entry.Properties, p => p is { Name: "Attempt", Value: "2" });
        Assert.Equal("test-source", entry.SourceId);
    }

    [Fact]
    public void CapsPropertyCountAndValueLength()
    {
        var sink = new CollectingSink();
        var logger = CreateProvider(sink, o =>
        {
            o.MaxCapturedProperties = 1;
            o.MaxPropertyValueLength = 3;
        }).CreateLogger("C");

        logger.LogInformation("{First} {Second}", "abcdef", "xyz");

        var entry = Assert.Single(sink.Entries);
        Assert.Single(entry.Properties);
        Assert.Equal("abc", entry.Properties[0].Value);
    }

    [Fact]
    public void MapsExceptionChainDepthBounded()
    {
        var sink = new CollectingSink();
        var logger = CreateProvider(sink, o => o.MaxCapturedScopeDepth = 1).CreateLogger("C");

        var exception = new InvalidOperationException("outer", new Exception("inner"));
        logger.LogError(exception, "boom");

        var entry = Assert.Single(sink.Entries);
        Assert.NotNull(entry.Exception);
        Assert.Equal("System.InvalidOperationException", entry.Exception!.Type);
        Assert.Null(entry.Exception.Inner); // depth bound of 1 stops before the inner exception
    }

    [Fact]
    public void CapturesScopesUpToDepthBound()
    {
        var sink = new CollectingSink();
        var provider = CreateProvider(sink, o => o.MaxCapturedScopeDepth = 1);
        provider.SetScopeProvider(new LoggerExternalScopeProvider());
        var logger = provider.CreateLogger("C");

        using (logger.BeginScope(new Dictionary<string, object?> { ["Outer"] = "1" }))
        using (logger.BeginScope(new Dictionary<string, object?> { ["Inner"] = "2" }))
            logger.LogInformation("msg");

        var entry = Assert.Single(sink.Entries);
        Assert.Single(entry.Scopes);
    }

    [Fact]
    public void IgnoresOwnCategoryToPreventFeedbackLoops()
    {
        var sink = new CollectingSink();
        var logger = CreateProvider(sink).CreateLogger("Elsa.Diagnostics.StructuredLogs.Storage.InMemoryStructuredLogStore");

        logger.LogError("should be ignored");

        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void SwallowsSinkFailuresWithoutThrowing()
    {
        var logger = CreateProvider(new ThrowingSink()).CreateLogger("C");

        var exception = Record.Exception(() => logger.LogInformation("hello"));

        Assert.Null(exception);
    }

    [Fact]
    public void RespectsMinimumLevel()
    {
        var sink = new CollectingSink();
        var logger = CreateProvider(sink, o => o.MinimumLevel = LogLevel.Warning).CreateLogger("C");

        logger.LogInformation("below");
        logger.LogError("at-or-above");

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
    }
}
