using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Groundwork.DiagnosticRecords;
using Groundwork.Sqlite.DiagnosticRecords;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.Tests;

public sealed class GroundworkStructuredLogReplayTests : GroundworkStructuredLogStoreTestBase
{
    [Fact]
    public async Task Tied_entries_replay_in_committed_cursor_order()
    {
        await using var first = await CreateStoreAsync(Binding);
        await using var second = await CreateStoreAsync(Binding);
        var timestamp = DateTimeOffset.Parse("2026-07-12T12:00:00Z");

        var commits = await Task.WhenAll(
            first.AppendAsync(Entry(5, "first", timestamp)).AsTask(),
            second.AppendAsync(Entry(5, "second", timestamp)).AsTask());
        var ordered = (await first.GetRecentAsync(StructuredLogFilter.None)).ToArray();
        var replayed = (await first.ReadAfterAsync(ordered[0].ReplayCursor, StructuredLogFilter.None, 100)).Entries;

        Assert.Single(replayed);
        Assert.Equal(ordered[1].Message, replayed[0].Message);
        Assert.NotEqual(commits[0].ReplayCursor, commits[1].ReplayCursor);
    }

    [Fact]
    public async Task Filtered_empty_page_advances_cursor_to_the_last_scanned_record()
    {
        await using var store = await CreateStoreAsync(Binding);
        var timestamp = DateTimeOffset.Parse("2026-07-12T12:00:00Z");
        await store.AppendAsync(Entry(1, "filtered-out", timestamp) with { SourceId = "other" });
        var expected = await store.AppendAsync(Entry(2, "included", timestamp) with { SourceId = "selected" });

        var emptyPage = await store.ReadAfterAsync(
            null,
            new StructuredLogFilter { SourceId = "selected" },
            1);
        var matchingPage = await store.ReadAfterAsync(
            emptyPage.NextCursor,
            new StructuredLogFilter { SourceId = "selected" },
            1);

        Assert.Empty(emptyPage.Entries);
        Assert.NotNull(emptyPage.NextCursor);
        Assert.True(emptyPage.HasMore);
        Assert.Equal([expected.ReplayCursor], matchingPage.Entries.Select(x => x.ReplayCursor));
    }

    [Fact]
    public async Task Restart_preserves_cursor_replay_and_lifetime_logical_high_water()
    {
        StructuredLogEntry first;
        await using (var beforeRestart = await CreateStoreAsync(Binding))
            first = await beforeRestart.AppendAsync(Entry(11, "before", DateTimeOffset.UnixEpoch));

        await using var restarted = await CreateStoreAsync(Binding);
        var second = await restarted.AppendAsync(Entry(12, "after", DateTimeOffset.UnixEpoch));
        var replay = await restarted.ReadAfterAsync(first.ReplayCursor, StructuredLogFilter.None, 100);

        Assert.Equal(12, await restarted.GetHighWaterMarkAsync());
        Assert.Equal([second.ReplayCursor], replay.Entries.Select(x => x.ReplayCursor));
    }

    [Fact]
    public async Task Malformed_or_foreign_binding_fails_with_one_non_disclosing_outcome()
    {
        await using var source = await CreateStoreAsync(Binding);
        await using var wrongTenant = await CreateStoreAsync(new("tenant-b", "shell-a", "structured-logs"));
        await using var wrongScope = await CreateStoreAsync(new("tenant-a", "shell-b", "structured-logs"));
        await using var wrongStream = await CreateStoreAsync(new("tenant-a", "shell-a", "audit-logs"));
        var committed = await source.AppendAsync(Entry(1, "source", DateTimeOffset.UnixEpoch));

        var errors = new[]
        {
            await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
                source.ReadAfterAsync(default(StructuredLogReplayCursor), StructuredLogFilter.None, 100)),
            await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
                wrongTenant.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100)),
            await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
                wrongScope.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100)),
            await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
                wrongStream.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100))
        };

        Assert.All(errors, error => Assert.Equal("The structured log replay cursor is unavailable.", error.Message));
    }

    [Fact]
    public async Task Trimmed_anchor_expires_while_lifetime_high_water_survives_restart()
    {
        StructuredLogEntry committed;
        await using (var store = await CreateStoreAsync(Binding))
        {
            committed = await store.AppendAsync(Entry(19, "trimmed", DateTimeOffset.UnixEpoch));
            await store.TrimAsync(0);
        }

        await using var restarted = await CreateStoreAsync(Binding);

        Assert.Equal(19, await restarted.GetHighWaterMarkAsync());
        Assert.Empty(await restarted.GetRecentAsync(StructuredLogFilter.None));
        await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            restarted.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100));
    }

    [Fact]
    public async Task Operational_query_failure_is_not_reported_as_cursor_unavailable()
    {
        var provider = await CreateProviderAsync(Binding);
        var failure = new StructuredLogsException("The diagnostics database is unavailable.");
        var failing = new QueryFailingStore(provider, failure);
        await using var store = new GroundworkStructuredLogStore(
            failing,
            Options.Create(new StructuredLogsOptions()),
            Binding);
        var committed = await store.AppendAsync(Entry(1, "source", DateTimeOffset.UnixEpoch));

        failing.FailQueries = true;

        var actual = await Assert.ThrowsAsync<StructuredLogsException>(() =>
            store.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100));
        Assert.Same(failure, actual);
    }

    private sealed class QueryFailingStore(IDiagnosticRecordStore inner, Exception failure) : IDiagnosticRecordStore
    {
        public bool FailQueries { get; set; }
        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;

        public ValueTask<DiagnosticRecordPage> QueryAsync(
            DiagnosticRecordQuery query,
            CancellationToken cancellationToken = default) =>
            FailQueries
                ? ValueTask.FromException<DiagnosticRecordPage>(failure)
                : inner.QueryAsync(query, cancellationToken);
    }
}

public abstract class GroundworkStructuredLogStoreTestBase : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"elsa-structured-logs-{Guid.NewGuid():N}.db");

    protected static StructuredLogStoreBinding Binding { get; } =
        new("tenant-a", "shell-a", "structured-logs");

    protected async Task<GroundworkStructuredLogStore> CreateStoreAsync(StructuredLogStoreBinding binding)
    {
        var provider = await CreateProviderAsync(binding);
        return new(provider, Options.Create(new StructuredLogsOptions()), binding);
    }

    protected Task<SqliteDiagnosticRecordStore> CreateProviderAsync(StructuredLogStoreBinding binding) =>
        SqliteDiagnosticRecordStoreFactory.CreateAsync(
            $"Data Source={_databasePath}",
            GroundworkStructuredLogStore.CreateStreamDefinition(binding.StreamId));

    protected static StructuredLogEntry Entry(long sequence, string message, DateTimeOffset timestamp) => new()
    {
        Sequence = sequence,
        Timestamp = timestamp,
        Level = LogLevel.Information,
        Category = "Test.Category",
        Message = message,
        SourceId = "writer"
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return Task.CompletedTask;
    }
}
