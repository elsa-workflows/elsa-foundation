using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Groundwork.DiagnosticRecords;
using Groundwork.Sqlite.DiagnosticRecords;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.Tests;

public sealed class GroundworkStructuredLogStoreTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"elsa-structured-logs-{Guid.NewGuid():N}.db");
    private static readonly StructuredLogStoreBinding Binding = new("tenant-a", "shell-a", "structured-logs");

    [Fact]
    public async Task Two_writers_with_equal_timestamp_and_sequence_replay_in_committed_cursor_order()
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
    public async Task Trim_to_zero_and_restart_preserve_high_water_but_expire_replay_cursor()
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
    public async Task Wrong_tenant_scope_and_stream_fail_without_disclosing_which_binding_mismatched()
    {
        await using var source = await CreateStoreAsync(Binding);
        await using var wrongTenant = await CreateStoreAsync(new("tenant-b", "shell-a", "structured-logs"));
        await using var wrongScope = await CreateStoreAsync(new("tenant-a", "shell-b", "structured-logs"));
        await using var wrongStream = await CreateStoreAsync(new("tenant-a", "shell-a", "audit-logs"));
        var committed = await source.AppendAsync(Entry(1, "source", DateTimeOffset.UnixEpoch));

        var tenantError = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            wrongTenant.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100));
        var scopeError = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            wrongScope.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100));
        var streamError = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            wrongStream.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 100));

        Assert.Equal(tenantError.Message, scopeError.Message);
        Assert.Equal(scopeError.Message, streamError.Message);
        Assert.Equal("The structured log replay cursor is unavailable.", scopeError.Message);
    }

    [Fact]
    public async Task Default_cursor_is_rejected_at_the_groundwork_store_boundary()
    {
        await using var store = await CreateStoreAsync(Binding);

        var exception = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            store.ReadAfterAsync(default(StructuredLogReplayCursor), StructuredLogFilter.None, 100));

        Assert.Equal("The structured log replay cursor is unavailable.", exception.Message);
    }

    [Fact]
    public async Task Acknowledgement_loss_retries_the_same_operation_and_publishes_one_committed_cursor()
    {
        var provider = await CreateProviderAsync(Binding);
        var lossy = new AcknowledgementLosingStore(provider);
        await using var store = new GroundworkStructuredLogStore(lossy, Options.Create(new StructuredLogsOptions()), Binding);
        var publisher = new RecordingPublisher();
        var sink = new StructuredLogSink(store, publisher);

        sink.Emit(Entry(0, "once", DateTimeOffset.UnixEpoch));
        await WaitUntilAsync(() => publisher.Entries.Count == 1);

        Assert.Equal(2, lossy.AppendCalls);
        Assert.Single(lossy.OperationIds.Distinct());
        Assert.Single(await store.GetRecentAsync(StructuredLogFilter.None));
        Assert.NotNull(publisher.Entries.Single().ReplayCursor);
    }

    [Fact]
    public async Task Hard_stop_settles_every_accepted_append_when_provider_ignores_cancellation()
    {
        var provider = await CreateProviderAsync(Binding);
        var hanging = new HangingAppendStore(provider);
        var options = Options.Create(new StructuredLogsOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(20) });
        var store = new GroundworkStructuredLogStore(hanging, options, Binding);
        var append = store.AppendAsync(Entry(1, "pending", DateTimeOffset.UnixEpoch)).AsTask();
        await hanging.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        await store.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(append.IsCompleted);
        await Assert.ThrowsAsync<StructuredLogsException>(() => append);
        hanging.Release();
        await hanging.Exited.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    private async Task<GroundworkStructuredLogStore> CreateStoreAsync(StructuredLogStoreBinding binding)
    {
        var provider = await CreateProviderAsync(binding);
        return new(provider, Options.Create(new StructuredLogsOptions()), binding);
    }

    private Task<SqliteDiagnosticRecordStore> CreateProviderAsync(StructuredLogStoreBinding binding) =>
        SqliteDiagnosticRecordStoreFactory.CreateAsync(
            $"Data Source={_databasePath}",
            GroundworkStructuredLogStore.CreateStreamDefinition(binding.StreamId));

    private static StructuredLogEntry Entry(long sequence, string message, DateTimeOffset timestamp) => new()
    {
        Sequence = sequence,
        Timestamp = timestamp,
        Level = LogLevel.Information,
        Category = "Test.Category",
        Message = message,
        SourceId = "writer"
    };

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate())
            await Task.Delay(10, cts.Token);
    }

    private sealed class RecordingPublisher : IStructuredLogLivePublisher
    {
        private readonly object _gate = new();
        public IReadOnlyList<StructuredLogEntry> Entries
        {
            get
            {
                lock (_gate)
                    return _entries.ToArray();
            }
        }

        private readonly List<StructuredLogEntry> _entries = [];

        public void Publish(StructuredLogEntry entry)
        {
            lock (_gate)
                _entries.Add(entry);
        }
    }

    private sealed class AcknowledgementLosingStore(IDiagnosticRecordStore inner) : IDiagnosticRecordStore
    {
        private int _loseAcknowledgement = 1;
        public int AppendCalls { get; private set; }
        public List<DiagnosticOperationId> OperationIds { get; } = [];
        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;

        public async ValueTask<DiagnosticAppendResult> AppendAsync(
            DiagnosticRecordBatch batch,
            CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            OperationIds.Add(batch.OperationId);
            var result = await inner.AppendAsync(batch, cancellationToken);
            if (Interlocked.Exchange(ref _loseAcknowledgement, 0) == 1)
                throw new DiagnosticAcknowledgementLostException(
                    DiagnosticOperationKind.Append,
                    batch.Stream,
                    batch.OperationId);
            return result;
        }
    }

    private sealed class HangingAppendStore(IDiagnosticRecordStore inner) : IDiagnosticRecordStore
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;
        public Task Exited => _exited.Task;
        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;

        public async ValueTask<DiagnosticAppendResult> AppendAsync(
            DiagnosticRecordBatch batch,
            CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task; // Intentionally ignores provider cancellation.
            try
            {
                return await inner.AppendAsync(batch, CancellationToken.None);
            }
            finally
            {
                _exited.TrySetResult();
            }
        }

        public void Release() => _release.TrySetResult();
    }
}
