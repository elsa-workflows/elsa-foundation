using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogSinkTests
{
    private sealed class FakeStore(long highWaterMark) : IStructuredLogStore
    {
        public List<StructuredLogEntry> Appended { get; } = [];
        public void Append(StructuredLogEntry entry) => Appended.Add(entry);
        public long GetHighWaterMark() => highWaterMark;
        public IReadOnlyList<StructuredLogEntry> GetRecent(StructuredLogFilter filter) => Appended;
        public IReadOnlyList<StructuredLogEntry> GetAfter(long afterSequence, StructuredLogFilter filter) => Appended;
    }

    private sealed class FakePublisher : IStructuredLogLivePublisher
    {
        public List<StructuredLogEntry> Published { get; } = [];
        public void Publish(StructuredLogEntry entry) => Published.Add(entry);
    }

    [Fact]
    public void EmitSeedsSequenceFromStoreHighWaterMarkAndIncrementsMonotonically()
    {
        var store = new FakeStore(highWaterMark: 10);
        var sink = new StructuredLogSink(store, new FakePublisher());

        sink.Emit(TestEntries.Create());
        sink.Emit(TestEntries.Create());

        Assert.Equal(new[] { 11L, 12L }, store.Appended.Select(e => e.Sequence));
    }

    [Fact]
    public void EmitAppendsToStoreAndPublishesTheSameStampedEntry()
    {
        var store = new FakeStore(highWaterMark: 0);
        var publisher = new FakePublisher();
        var sink = new StructuredLogSink(store, publisher);

        sink.Emit(TestEntries.Create(message: "x"));

        var appended = Assert.Single(store.Appended);
        var published = Assert.Single(publisher.Published);
        Assert.Equal(1L, appended.Sequence);
        Assert.Same(appended, published);
    }
}
