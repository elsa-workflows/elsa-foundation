using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowSchedulerCommandProcessor : IWorkflowExecutionCommandProcessor
{
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;

    public WorkflowSchedulerCommandProcessor(IWorkflowSchedulerWorkQueue schedulerWorkQueue)
    {
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);

        _schedulerWorkQueue = schedulerWorkQueue;
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
            recordedAt: DateTimeOffset.UtcNow,
            sequence: envelope.Sequence,
            payload: envelope.Command.Payload,
            commandMetadata: envelope.Command.Metadata,
            envelopeMetadata: envelope.Metadata);

        await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }
}
