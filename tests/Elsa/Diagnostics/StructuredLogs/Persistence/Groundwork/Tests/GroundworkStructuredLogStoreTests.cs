using System.Text;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Groundwork.DiagnosticRecords;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.Tests;

public sealed class GroundworkStructuredLogStoreTests : GroundworkStructuredLogStoreTestBase
{
    [Fact]
    public async Task Append_before_explicit_lifecycle_start_is_rejected()
    {
        var provider = await CreateProviderAsync(Binding);
        await using var store = new GroundworkStructuredLogStore(
            provider,
            Options.Create(new StructuredLogsOptions()),
            Binding);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AppendAsync(Entry(1, "not-started", DateTimeOffset.UnixEpoch), timeout.Token).AsTask());
        Assert.Empty(await store.GetRecentAsync(StructuredLogFilter.None));
    }

    [Fact]
    public async Task Lifecycle_stop_before_start_is_terminal_without_retention_io()
    {
        var provider = await CreateProviderAsync(Binding);
        var observing = new RetentionAcknowledgementLosingStore(provider);
        var store = new GroundworkStructuredLogStore(
            observing,
            Options.Create(new StructuredLogsOptions()),
            Binding);

        await ((IDiagnosticsPersistenceDrain)store).StopAsync();

        Assert.Equal(0, observing.TrimCalls);
        Assert.Throws<InvalidOperationException>(store.Start);
        await store.DisposeAsync();
    }

    [Fact]
    public async Task Disposal_before_start_is_terminal_without_retention_io()
    {
        var provider = await CreateProviderAsync(Binding);
        var observing = new RetentionAcknowledgementLosingStore(provider);
        var store = new GroundworkStructuredLogStore(
            observing,
            Options.Create(new StructuredLogsOptions()),
            Binding);

        await store.DisposeAsync();

        Assert.Equal(0, observing.TrimCalls);
        Assert.Throws<InvalidOperationException>(store.Start);
    }

    [Fact]
    public async Task Acknowledgement_loss_retries_the_same_operation_and_publishes_one_committed_cursor()
    {
        var provider = await CreateProviderAsync(Binding);
        var lossy = new AcknowledgementLosingStore(provider);
        var counters = new DiagnosticsPersistenceCounters();
        await using var store = Start(new GroundworkStructuredLogStore(
            lossy,
            Options.Create(new StructuredLogsOptions()),
            Binding,
            counters));
        var publisher = new RecordingPublisher();
        var sink = new StructuredLogSink(store, publisher);

        sink.Emit(Entry(0, "once", DateTimeOffset.UnixEpoch));
        await WaitUntilAsync(() => publisher.Entries.Count == 1);

        Assert.Equal(2, lossy.AppendCalls);
        Assert.Single(lossy.OperationIds.Distinct());
        Assert.Single(await store.GetRecentAsync(StructuredLogFilter.None));
        Assert.NotNull(publisher.Entries.Single().ReplayCursor);
        Assert.Equal(1, counters.Snapshot().CommitRetries);
    }

    [Fact]
    public async Task Automatic_retention_acknowledgement_loss_retries_the_same_operation_and_keeps_exact_newest_records()
    {
        var provider = await CreateProviderAsync(Binding);
        var lossy = new RetentionAcknowledgementLosingStore(provider);
        await using var store = Start(new GroundworkStructuredLogStore(
            lossy,
            Options.Create(new StructuredLogsOptions()),
            Binding,
            maxRetainedEntries: 2,
            retentionInterval: 3));
        var timestamp = DateTimeOffset.UnixEpoch;

        await store.AppendAsync(Entry(1, "first", timestamp));
        await store.AppendAsync(Entry(2, "second", timestamp));
        await store.AppendAsync(Entry(3, "third", timestamp));
        await WaitUntilAsync(() => lossy.TrimCalls == 2);

        Assert.Single(lossy.OperationIds.Distinct());
        Assert.All(lossy.OperationIds, operation => Assert.InRange(Encoding.UTF8.GetByteCount(operation.Nonce), 1, 64));
        Assert.All(lossy.TrimResults, result => Assert.Equal(1, result.DeletedCount.Value));
        Assert.Equal(["second", "third"], (await store.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
    }

    [Theory]
    [InlineData("stréam")]
    [InlineData(" stream")]
    [InlineData("stream ")]
    public void Stream_definition_rejects_identifiers_outside_the_portable_provider_domain(string streamId)
    {
        Assert.Throws<ArgumentException>(() => GroundworkStructuredLogStore.CreateStreamDefinition(streamId));
    }

    [Fact]
    public void Stream_definition_rejects_identifiers_over_sixty_four_bytes()
    {
        Assert.Throws<ArgumentException>(() =>
            GroundworkStructuredLogStore.CreateStreamDefinition(new string('s', 65)));
    }

    [Fact]
    public async Task Hard_stop_settles_every_accepted_append_when_provider_ignores_cancellation()
    {
        var provider = await CreateProviderAsync(Binding);
        var hanging = new HangingAppendStore(provider);
        var options = Options.Create(new StructuredLogsOptions { ShutdownDrainTimeout = TimeSpan.FromMilliseconds(20) });
        var counters = new DiagnosticsPersistenceCounters();
        var store = new GroundworkStructuredLogStore(hanging, options, Binding, counters);
        store.Start();
        var append = store.AppendAsync(Entry(1, "pending", DateTimeOffset.UnixEpoch)).AsTask();
        await hanging.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        await store.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        // The hard stop is the only thing that can settle the append inside this window, because the
        // provider stays blocked until Release below.
        await Assert.ThrowsAsync<StructuredLogsException>(() => append.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.ShutdownTimeout]);
        hanging.Release();
        await hanging.Exited.WaitAsync(TimeSpan.FromSeconds(10));
    }

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

    private sealed class RetentionAcknowledgementLosingStore(IDiagnosticRecordStore inner) : IDiagnosticRecordStore
    {
        private readonly object _gate = new();
        private int _loseAcknowledgement = 1;
        private readonly List<DiagnosticOperationId> _operationIds = [];
        private readonly List<DiagnosticTrimResult> _trimResults = [];

        public int TrimCalls
        {
            get
            {
                lock (_gate)
                    return _trimResults.Count;
            }
        }

        public IReadOnlyList<DiagnosticOperationId> OperationIds
        {
            get
            {
                lock (_gate)
                    return _operationIds.ToArray();
            }
        }

        public IReadOnlyList<DiagnosticTrimResult> TrimResults
        {
            get
            {
                lock (_gate)
                    return _trimResults.ToArray();
            }
        }

        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;

        public async ValueTask<DiagnosticTrimResult> TrimAsync(
            DiagnosticTrimRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.TrimAsync(request, cancellationToken);
            lock (_gate)
            {
                _operationIds.Add(request.OperationId);
                _trimResults.Add(result);
            }

            if (Interlocked.Exchange(ref _loseAcknowledgement, 0) == 1)
                throw new DiagnosticAcknowledgementLostException(
                    DiagnosticOperationKind.Trim,
                    request.Stream,
                    request.OperationId);
            return result;
        }
    }
}
