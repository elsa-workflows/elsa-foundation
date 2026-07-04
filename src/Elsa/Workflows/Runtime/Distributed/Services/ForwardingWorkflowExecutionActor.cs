using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;

namespace Elsa.Workflows.Runtime.Distributed.Services;

/// <summary>
/// A routing stub returned by <see cref="DistributedWorkflowExecutionActorProvider"/> for an execution owned by another
/// node. It is not a mailbox: <see cref="EnqueueAsync(WorkflowExecutionCommandEnvelope, CancellationToken)"/> forwards
/// the command to the durable transport inbox and returns a <see cref="WorkflowExecutionCommandDispatchStatus.Deferred"/>
/// result, meaning the command was accepted for routing but will run on the owning node.
/// </summary>
/// <remarks>
/// Delivery is at-least-once. The command is durably enqueued before the deferred result is returned, so it is not lost
/// if this node dies immediately after. The owning node's placement pump leases and dispatches it; if that node dies
/// after leasing but before acking, the transport lease expires and the item is re-driven on the survivor's failover
/// claim. The fencing token checked at checkpoint commit — not this transport — is what prevents a re-driven command
/// from producing a second durable execution.
/// </remarks>
public sealed class ForwardingWorkflowExecutionActor : IWorkflowExecutionActor
{
    private readonly string _workflowExecutionId;
    private readonly string _localNodeId;
    private readonly string _owningNodeId;
    private readonly IExecutionCommandTransport _transport;
    private readonly TimeProvider _timeProvider;

    public ForwardingWorkflowExecutionActor(
        string workflowExecutionId,
        string localNodeId,
        string owningNodeId,
        IExecutionCommandTransport transport,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(localNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(owningNodeId);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutionId = workflowExecutionId;
        _localNodeId = localNodeId;
        _owningNodeId = owningNodeId;
        _transport = transport;
        _timeProvider = timeProvider;
    }

    public WorkflowExecutionActorDescriptor Descriptor => new(
        workflowExecutionId: _workflowExecutionId,
        agentId: $"distributed-forward:{_workflowExecutionId}:{_localNodeId}",
        providerName: DistributedWorkflowExecutionActorProvider.ProviderName,
        status: WorkflowExecutionActorStatus.Unavailable,
        capabilities: WorkflowExecutionActorCapabilities.DistributedPlacement,
        activatedAt: _timeProvider.GetUtcNow(),
        metadata: new Dictionary<string, string> { ["runtime.distributed.owningNode"] = _owningNodeId });

    public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.Equals(envelope.WorkflowExecutionId, _workflowExecutionId, StringComparison.Ordinal))
        {
            return new WorkflowExecutionCommandDispatchResult(
                envelopeId: envelope.EnvelopeId,
                workflowExecutionId: envelope.WorkflowExecutionId,
                status: WorkflowExecutionCommandDispatchStatus.Rejected,
                recordedAt: _timeProvider.GetUtcNow(),
                reason: "Envelope workflow execution ID does not match this forwarding actor.");
        }

        var now = _timeProvider.GetUtcNow();
        var item = await _transport.SendAsync(_workflowExecutionId, envelope, now, cancellationToken);

        return new WorkflowExecutionCommandDispatchResult(
            envelopeId: envelope.EnvelopeId,
            workflowExecutionId: envelope.WorkflowExecutionId,
            status: WorkflowExecutionCommandDispatchStatus.Deferred,
            recordedAt: now,
            reason: $"Forwarded to owning node '{_owningNodeId}' via durable transport.",
            metadata: new Dictionary<string, string>
            {
                ["runtime.distributed.owningNode"] = _owningNodeId,
                ["runtime.distributed.transportItemId"] = item.TransportItemId
            });
    }

    public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
        WorkflowExecutionCommandEnvelope envelope,
        WorkflowExecutionCommandDispatchOptions options,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(envelope, cancellationToken);
}
