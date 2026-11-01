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
