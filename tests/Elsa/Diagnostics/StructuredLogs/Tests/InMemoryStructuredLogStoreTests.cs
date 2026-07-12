using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class InMemoryStructuredLogStoreTests
{
    private static StructuredLogEntry Seq(long sequence, string message = "m", LogLevel level = LogLevel.Information) =>
        TestEntries.Create(sequence: sequence, level: level, message: message);

    [Fact]
    public async Task Replay_uses_committed_cursor_when_display_sequences_are_equal()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        var first = await store.AppendAsync(Seq(7, "first"));
        var second = await store.AppendAsync(Seq(7, "second"));

        var replay = await store.ReadAfterAsync(first.ReplayCursor, StructuredLogFilter.None, 100);
        var entries = replay.Entries;

        Assert.Equal(["second"], entries.Select(x => x.Message));
        Assert.NotEqual(first.ReplayCursor, second.ReplayCursor);
    }

    [Fact]
    public async Task Trim_to_zero_preserves_lifetime_logical_high_water_and_expires_the_cursor()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        var committed = await store.AppendAsync(Seq(9, "only"));

        await store.TrimAsync(0);

        Assert.Empty(await store.GetRecentAsync(StructuredLogFilter.None));
        Assert.Equal(9, await store.GetHighWaterMarkAsync());
        var exception = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            store.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100));
        Assert.Equal("The structured log replay cursor is unavailable.", exception.Message);
    }

    [Fact]
    public async Task Wrong_tenant_scope_and_stream_cursors_fail_with_the_same_non_disclosing_error()
    {
        var options = TestOptions.Create();
        var source = new InMemoryStructuredLogStore(options, new("tenant-a", "shell-a", "logs"));
        var wrongTenant = new InMemoryStructuredLogStore(options, new("tenant-b", "shell-a", "logs"));
        var wrongScope = new InMemoryStructuredLogStore(options, new("tenant-a", "shell-b", "logs"));
        var wrongStream = new InMemoryStructuredLogStore(options, new("tenant-a", "shell-a", "audit"));
        var committed = await source.AppendAsync(Seq(1));

        var tenantError = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            wrongTenant.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100));
        var scopeError = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            wrongScope.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100));
        var streamError = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            wrongStream.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100));

        Assert.Equal(tenantError.Message, scopeError.Message);
        Assert.Equal(scopeError.Message, streamError.Message);
        Assert.DoesNotContain("tenant", scopeError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stream", streamError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_after_rejects_a_default_cursor_at_the_store_boundary()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        await store.AppendAsync(Seq(1));

        var exception = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            store.ReadAfterAsync(default(StructuredLogReplayCursor), StructuredLogFilter.None, 100));

        Assert.Equal("The structured log replay cursor is unavailable.", exception.Message);
    }

    [Fact]
    public async Task AppendRetainsEntriesAndTracksHighWaterMark()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());

        await store.AppendAsync(Seq(1, "a"));
        await store.AppendAsync(Seq(2, "b"));

        var recent = await store.GetRecentAsync(StructuredLogFilter.None);
        Assert.Equal(new[] { 1L, 2L }, recent.Select(e => e.Sequence));
        Assert.Equal(2L, await store.GetHighWaterMarkAsync());
    }

    [Fact]
    public async Task HighWaterMarkIsZeroWhenEmpty()
    {
        Assert.Equal(0L, await new InMemoryStructuredLogStore(TestOptions.Create()).GetHighWaterMarkAsync());
    }

    [Fact]
    public async Task EvictsOldestBeyondCapacity()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create(o => o.BufferCapacity = 2));

        await store.AppendAsync(Seq(1, "a"));
        await store.AppendAsync(Seq(2, "b"));
        await store.AppendAsync(Seq(3, "c"));

        var recent = await store.GetRecentAsync(StructuredLogFilter.None);
        Assert.Equal(new[] { "b", "c" }, recent.Select(e => e.Message));
    }

    [Fact]
    public async Task GetRecentIsNewestAlignedAndClampedToTake()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        for (var i = 0; i < 5; i++)
            await store.AppendAsync(Seq(i + 1, i.ToString()));

        var recent = await store.GetRecentAsync(new StructuredLogFilter { MaxCount = 2 });

        Assert.Equal(new[] { "3", "4" }, recent.Select(e => e.Message));
    }

    [Fact]
    public async Task GetRecentClampsTakeToMaxRecentQuerySize()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create(o => o.MaxRecentQuerySize = 2));
        for (var i = 0; i < 5; i++)
            await store.AppendAsync(Seq(i + 1, i.ToString()));

        Assert.Equal(2, (await store.GetRecentAsync(new StructuredLogFilter { MaxCount = 100 })).Count);
    }

    [Fact]
    public async Task ReadAfterClampsScannedPositionsAndAdvancesAcrossFilteredEntries()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create(o => o.MaxRecentQuerySize = 2));
        await store.AppendAsync(TestEntries.Create(sequence: 1, sourceId: "skip"));
        await store.AppendAsync(TestEntries.Create(sequence: 2, sourceId: "match"));
        await store.AppendAsync(TestEntries.Create(sequence: 3, sourceId: "match"));

        var page = await store.ReadAfterAsync(
            null,
            new StructuredLogFilter { SourceId = "match" },
            100);

        Assert.Single(page.Entries);
        Assert.Equal(2, page.Entries[0].Sequence);
        Assert.Equal(page.Entries[0].ReplayCursor, page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task GetRecentAppliesFilter()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        await store.AppendAsync(Seq(1, level: LogLevel.Information));
        await store.AppendAsync(Seq(2, level: LogLevel.Error));

        var recent = await store.GetRecentAsync(new StructuredLogFilter { MinimumLevel = LogLevel.Warning });

        Assert.Single(recent);
        Assert.Equal(LogLevel.Error, recent[0].Level);
    }

    [Fact]
    public async Task GetRecentWithZeroTakeReturnsEmpty()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        await store.AppendAsync(Seq(1));

        Assert.Empty(await store.GetRecentAsync(new StructuredLogFilter { MaxCount = 0 }));
    }

    [Fact]
    public async Task ReplayReturnsOnlyEntriesAfterTheCommittedCursor()
    {
        var store = new InMemoryStructuredLogStore(TestOptions.Create());
        StructuredLogEntry? second = null;
        for (var i = 0; i < 4; i++)
        {
            var committed = await store.AppendAsync(Seq(i + 1, i.ToString()));
            if (i == 1)
                second = committed;
        }

        var replay = await store.ReadAfterAsync(second!.ReplayCursor, StructuredLogFilter.None, 100);
        var after = replay.Entries;

        Assert.Equal(new[] { 3L, 4L }, after.Select(e => e.Sequence));
    }

}
