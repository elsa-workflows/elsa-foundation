using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Groundwork.DiagnosticRecords;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.Tests;

public sealed class GroundworkStructuredLogStoreTests : GroundworkStructuredLogStoreTestBase
{
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
