using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeInProcessAgentProviderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Only_a_live_valid_cancel_waiter_requests_its_own_mailbox_handoff(bool cancelWaiter, bool wrongPartition)
    {
        var controls = new DrainControlContext();
        var processor = new ObservingControlProcessor(controls);
        using var provider = new InProcessWorkflowExecutionActorProvider(processor) { DrainControls = controls };
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = agent.EnqueueAsync(NewEnvelope(1), timeout.Token).AsTask();
        await processor.Entered.Task.WaitAsync(timeout.Token);
        using var waiterToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var now = DateTimeOffset.UtcNow;
        var cancel = new WorkflowExecutionCommandEnvelope("cancel", "wfexec-1",
            new("cancel", "wfexec-1", WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>()),
            "cancel", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now,
            partition: wrongPartition ? new WorkflowExecutionPartition("other") : null);
        var waiting = agent.EnqueueAsync(cancel, waiterToken.Token).AsTask();
        if (cancelWaiter)
        {
            await waiterToken.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        processor.Inspect.TrySetResult();
        await running.WaitAsync(timeout.Token);
        if (!cancelWaiter)
            Assert.Equal(wrongPartition ? WorkflowExecutionCommandDispatchStatus.Rejected : WorkflowExecutionCommandDispatchStatus.Accepted,
                (await waiting.WaitAsync(timeout.Token)).Status);
        Assert.Equal(!cancelWaiter && !wrongPartition, processor.OwnYield);
        Assert.False(processor.OtherYield);
        Assert.False(processor.CancelYield);
        Assert.False(controls.ShouldYield("wfexec-1"));
    }

    [Fact]
    public async Task A_drain_that_yielded_to_a_cancelled_waiter_is_redriven_at_once()
    {
        using var drain = await StartYieldingDrainAsync();
        var (controls, processor, agent, timeout) = (drain.Controls, drain.Processor, drain.Agent, drain.Timeout);
        using var waiterToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var waiting = agent.EnqueueAsync(NewCancel("cancel"), waiterToken.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);

        await waiterToken.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        processor.Release.TrySetResult();

        var redrive = await processor.Redriven.Task.WaitAsync(timeout.Token);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, redrive.Command.Kind);
        Assert.Equal("wfexec-1", redrive.WorkflowExecutionId);
        Assert.Equal(0, processor.CancelsProcessed);
        Assert.False(controls.ShouldYield("wfexec-1"));
    }

    [Fact]
    public async Task A_drain_that_yielded_to_a_duplicate_cancel_is_redriven_at_once()
    {
        using var drain = await StartYieldingDrainAsync(precedingCancelKey: "cancel");
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        var duplicate = agent.EnqueueAsync(NewCancel("cancel"), timeout.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        processor.Release.TrySetResult();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, (await duplicate.WaitAsync(timeout.Token)).Status);
        var redrive = await processor.Redriven.Task.WaitAsync(timeout.Token);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, redrive.Command.Kind);
        Assert.Equal(1, processor.CancelsProcessed);
    }

    [Fact]
    public async Task A_drain_that_yielded_to_a_cancel_that_ran_is_not_redriven()
    {
        using var drain = await StartYieldingDrainAsync();
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        processor.CancelGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancel = agent.EnqueueAsync(NewCancel("cancel"), timeout.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        processor.Release.TrySetResult();

        // Let the yielded turn finish its post-release check while the Cancel is still inside its own turn: the
        // Cancel's registration must still be held there, or that check would see no waiter and re-drive.
        await drain.Running.WaitAsync(timeout.Token);
        await processor.CancelEntered.Task.WaitAsync(timeout.Token);
        processor.CancelGate.TrySetResult();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await cancel.WaitAsync(timeout.Token)).Status);
        Assert.Equal(1, processor.CancelsProcessed);
        await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
        Assert.False(processor.Redriven.Task.IsCompleted);
    }

    [Fact]
    public async Task A_turn_that_drained_nothing_keeps_the_yield_for_a_later_withdrawn_cancel()
    {
        using var drain = await StartYieldingDrainAsync();
        var (processor, agent, timeout) = (drain.Processor, drain.Agent, drain.Timeout);
        // Mailbox waiters are served in order: two commands that drain nothing, then the Cancel the drain yields to.
        processor.GatedKey = "wfexec-1:command-3";
        var first = agent.EnqueueAsync(NewEnvelope(2), timeout.Token).AsTask();
        var second = agent.EnqueueAsync(NewEnvelope(3), timeout.Token).AsTask();
        using var waiterToken = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var waiting = agent.EnqueueAsync(NewCancel("cancel"), waiterToken.Token).AsTask();
        await processor.YieldObserved.Task.WaitAsync(timeout.Token);
        processor.Release.TrySetResult();

        // The first command has completed without draining; the Cancel withdraws while the second still holds the mailbox.
        await first.WaitAsync(timeout.Token);
        await processor.GatedEntered.Task.WaitAsync(timeout.Token);
        await waiterToken.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        processor.Gate.TrySetResult();
        await second.WaitAsync(timeout.Token);

        var redrive = await processor.Redriven.Task.WaitAsync(timeout.Token);
        Assert.Equal(WorkflowExecutionCommandKind.RunSchedulerWork, redrive.Command.Kind);
        Assert.Equal(0, processor.CancelsProcessed);
    }

    private async Task<YieldingDrain> StartYieldingDrainAsync(string? precedingCancelKey = null)
    {
        var controls = new DrainControlContext();
        var processor = new YieldingControlProcessor(controls);
        var provider = new InProcessWorkflowExecutionActorProvider(processor) { DrainControls = controls };
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        if (precedingCancelKey is not null)
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted,
                (await agent.EnqueueAsync(NewCancel(precedingCancelKey), timeout.Token)).Status);
        var running = agent.EnqueueAsync(NewEnvelope(1), timeout.Token).AsTask();
        await processor.Entered.Task.WaitAsync(timeout.Token);
        return new(controls, processor, provider, agent, timeout, running);
    }

    private sealed record YieldingDrain(DrainControlContext Controls, YieldingControlProcessor Processor,
        InProcessWorkflowExecutionActorProvider Provider, IWorkflowExecutionActor Agent, CancellationTokenSource Timeout,
        Task<WorkflowExecutionCommandDispatchResult> Running) : IDisposable
    {
        public void Dispose()
        {
            Provider.Dispose();
            Timeout.Dispose();
        }
    }

    private static WorkflowExecutionCommandEnvelope NewCancel(string idempotencyKey)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        return new(id, "wfexec-1",
            new(id, "wfexec-1", WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>()),
            idempotencyKey, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now);
    }

    // The first scheduler turn yields once a Cancel waits, holding the mailbox until the test releases it. A turn whose
    // source is the yield re-drive is reported; any other later scheduler turn drains nothing.
    private sealed class YieldingControlProcessor(DrainControlContext controls) : IWorkflowExecutionCommandExecutor
    {
        private int schedulerTurns;
        private int cancelsProcessed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource YieldObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<WorkflowExecutionCommandEnvelope> Redriven { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancelEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? CancelGate { get; set; }
        public string? GatedKey { get; set; }
        public TaskCompletionSource GatedEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CancelsProcessed => Volatile.Read(ref cancelsProcessed);

        public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Command.Kind == WorkflowExecutionCommandKind.Cancel)
            {
                Interlocked.Increment(ref cancelsProcessed);
                CancelEntered.TrySetResult();
                if (CancelGate is { } gate)
                    await gate.Task.WaitAsync(cancellationToken);
                return WorkflowExecutionCommandProcessResult.NoDrain;
            }
            if (envelope.Command.Metadata.TryGetValue("source", out var source) &&
                source == InProcessWorkflowExecutionActorProvider.YieldRedriveSource)
            {
                Redriven.TrySetResult(envelope);
                return WorkflowExecutionCommandProcessResult.NoDrain;
            }
            if (Interlocked.Increment(ref schedulerTurns) > 1)
            {
                if (envelope.IdempotencyKey == GatedKey)
                {
                    GatedEntered.TrySetResult();
                    await Gate.Task.WaitAsync(cancellationToken);
                }
                return WorkflowExecutionCommandProcessResult.NoDrain;
            }
            Entered.TrySetResult();
            while (!controls.ShouldYield(envelope.WorkflowExecutionId))
                await Task.Delay(5, cancellationToken);
            YieldObserved.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(true, RuntimeSchedulerDrainStopReason.Yielded, false, false, null);
        }
    }

    private sealed class ObservingControlProcessor(DrainControlContext controls) : IWorkflowExecutionCommandExecutor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Inspect { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool OwnYield;
        public bool OtherYield;
        public bool CancelYield;
        public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.Command.Kind == WorkflowExecutionCommandKind.Cancel)
            {
                CancelYield = controls.ShouldYield(envelope.WorkflowExecutionId);
                return WorkflowExecutionCommandProcessResult.NoDrain;
            }
            Entered.TrySetResult();
            await Inspect.Task.WaitAsync(cancellationToken);
            OwnYield = controls.ShouldYield(envelope.WorkflowExecutionId);
            OtherYield = controls.ShouldYield("wfexec-2");
            return WorkflowExecutionCommandProcessResult.NoDrain;
        }
    }
}
