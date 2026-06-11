using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowSchedulerCommandProcessor : IWorkflowExecutionCommandProcessor
{
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly IWorkflowSchedulerDrainer? _schedulerDrainer;
    private readonly IWorkflowSchedulerDrainPolicy _schedulerDrainPolicy;
    private readonly IReadOnlyCollection<IWorkflowSchedulerDrainObserver> _schedulerDrainObservers;
    private readonly TimeProvider _timeProvider;

    public WorkflowSchedulerCommandProcessor(IWorkflowSchedulerWorkQueue schedulerWorkQueue)
        : this(schedulerWorkQueue, schedulerDrainer: null, EnqueueOnlySchedulerDrainPolicy.Instance, [], TimeProvider.System)
    {
    }

    public WorkflowSchedulerCommandProcessor(IWorkflowSchedulerWorkQueue schedulerWorkQueue, TimeProvider timeProvider)
        : this(schedulerWorkQueue, schedulerDrainer: null, EnqueueOnlySchedulerDrainPolicy.Instance, [], timeProvider)
    {
    }

    public WorkflowSchedulerCommandProcessor(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IWorkflowSchedulerDrainer schedulerDrainer,
        IWorkflowSchedulerDrainPolicy schedulerDrainPolicy,
        IEnumerable<IWorkflowSchedulerDrainObserver> schedulerDrainObservers)
        : this(schedulerWorkQueue, schedulerDrainer, schedulerDrainPolicy, schedulerDrainObservers, TimeProvider.System)
    {
    }

    public WorkflowSchedulerCommandProcessor(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IWorkflowSchedulerDrainer? schedulerDrainer,
        IWorkflowSchedulerDrainPolicy schedulerDrainPolicy,
        IEnumerable<IWorkflowSchedulerDrainObserver> schedulerDrainObservers,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(schedulerDrainPolicy);
        ArgumentNullException.ThrowIfNull(schedulerDrainObservers);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _schedulerWorkQueue = schedulerWorkQueue;
        _schedulerDrainer = schedulerDrainer;
        _schedulerDrainPolicy = schedulerDrainPolicy;
        _schedulerDrainObservers = schedulerDrainObservers.ToArray();
        _timeProvider = timeProvider;
    }

    public async ValueTask ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: envelope.EnvelopeId,
            workflowExecutionId: envelope.WorkflowExecutionId,
            commandId: envelope.Command.CommandId,
            commandKind: envelope.Command.Kind,
            envelopeId: envelope.EnvelopeId,
            idempotencyKey: envelope.IdempotencyKey,
            enqueuedAt: envelope.EnqueuedAt,
            recordedAt: _timeProvider.GetUtcNow(),
            sequence: envelope.Sequence,
            payload: envelope.Command.Payload,
            commandMetadata: envelope.Command.Metadata,
            envelopeMetadata: envelope.Metadata);

        workItem = await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);

        if (_schedulerDrainer is null)
            return;

        var drainRequest = _schedulerDrainPolicy.CreateDrainRequest(envelope, workItem);
        if (drainRequest is null)
            return;

        var drainResult = await _schedulerDrainer.DrainAsync(drainRequest, cancellationToken);
        foreach (var observer in _schedulerDrainObservers)
            await observer.OnDrainedAsync(envelope, drainResult, cancellationToken);
    }

    private sealed class EnqueueOnlySchedulerDrainPolicy : IWorkflowSchedulerDrainPolicy
    {
        public static readonly EnqueueOnlySchedulerDrainPolicy Instance = new();

        private EnqueueOnlySchedulerDrainPolicy()
        {
        }

        public RuntimeSchedulerDrainRequest? CreateDrainRequest(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerWorkItem workItem) => null;
    }
}
