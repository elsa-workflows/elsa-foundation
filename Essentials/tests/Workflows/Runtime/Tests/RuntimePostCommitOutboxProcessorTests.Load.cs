using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimePostCommitOutboxProcessorTests
{
    [Theory]
    [InlineData(false, false, "success")]
    [InlineData(true, false, "success")]
    [InlineData(false, true, "success")]
    [InlineData(true, true, "success")]
    [InlineData(false, false, "fault")]
    [InlineData(true, false, "fault")]
    [InlineData(false, false, "cancel")]
    [InlineData(true, false, "cancel")]
    public async Task OutboxDelivery_ChargesActualWork_WithoutDoubleChargingAmbientCaller_AndAlwaysDisposes(bool claimed, bool ambient, string outcome)
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var signal = new LoadObservations();
        using var caller = ambient ? signal.OpenCharge() : null;
        var dispatcher = new GatedLoadDispatcher(signal, outcome);
        var processor = ChargedProcessor(claimed ? store : new UnclaimedLoadStore(store), dispatcher, signal);
        var empty = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));
        Assert.False(empty.Items.Any());
        Assert.Equal(ambient ? 1 : 0, signal.OpenCount);
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-load", "intent-load", "execution"));
        using var shutdown = new CancellationTokenSource();
        var run = processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10), shutdown.Token).AsTask();
        try
        {
            await dispatcher.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, signal.InFlightDispatches);
            Assert.Equal(1, signal.OpenCount);
            Assert.True(dispatcher.HadAmbientCharge);
        }
        finally
        {
            if (outcome == "cancel") await shutdown.CancelAsync();
            dispatcher.Release.TrySetResult();
        }
        if (outcome == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        else
        {
            var result = await run;
            Assert.Equal(1, result.AttemptedCount);
            Assert.Equal(outcome == "success" ? 1 : 0, result.DeliveredCount);
        }
        Assert.Equal(ambient ? 2 : 0, signal.InFlightDispatches);
        caller?.Dispose();
        Assert.Equal(0, signal.InFlightDispatches);
        Assert.False(signal.HasAmbientCharge);
    }

    private RuntimePostCommitOutboxProcessor ChargedProcessor(IRuntimePostCommitOutboxStore store,
        IRuntimePostCommitIntentDispatcher dispatcher, IRuntimeAdmissionLoadSignal signal) =>
        new(store, dispatcher, new FakeTimeProvider(_now), DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            null, [], null, null, null, null, [], signal);

    private sealed class GatedLoadDispatcher(IRuntimeAdmissionLoadSignal signal, string outcome) : IRuntimePostCommitIntentDispatcher
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HadAmbientCharge;
        public async ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            HadAmbientCharge = signal.HasAmbientCharge;
            signal.RecordDispatch();
            Ready.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (outcome == "fault") throw new InvalidOperationException("delivery failed");
        }
    }

    private sealed class LoadObservations : IRuntimeAdmissionLoadSignal
    {
        private readonly DispatchRuntimeAdmissionLoadSignal _inner = new();
        public int OpenCount;
        public long InFlightDispatches => _inner.InFlightDispatches;
        public bool HasAmbientCharge => _inner.HasAmbientCharge;
        public IRuntimeAdmissionCharge OpenCharge() { OpenCount++; return _inner.OpenCharge(); }
        public IRuntimeAdmissionCharge? TryOpenCharge(double limit, out long observedLoad) => _inner.TryOpenCharge(limit, out observedLoad);
        public void RecordDispatch() => _inner.RecordDispatch();
    }

    private sealed class UnclaimedLoadStore(InMemoryRuntimeCheckpointCommitStore inner) : IRuntimePostCommitOutboxStore
    {
        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default) => inner.GetDeliverableAsync(query, cancellationToken);
        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) => inner.RecordDeliveryResultAsync(result, cancellationToken);
    }
}
