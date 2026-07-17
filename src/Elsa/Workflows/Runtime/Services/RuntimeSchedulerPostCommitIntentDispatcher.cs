using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeSchedulerPostCommitIntentDispatcher(
    IWorkflowSchedulerWorkQueue schedulerWorkQueue)
    : IRuntimePostCommitIntentDispatcher, IRuntimePostCommitIntentHandler
{
    public async ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();

        if (!StringComparer.Ordinal.Equals(intent.Kind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork))
            throw new InvalidOperationException($"Unsupported runtime post-commit intent kind '{intent.Kind}'.");

        if (intent.Payload is not { } payload)
            throw new InvalidOperationException($"Scheduler work post-commit intent '{intent.IntentId}' requires a scheduler work payload.");

        RuntimeSchedulerWorkItem workItem;
        try
        {
            workItem = payload.Deserialize<RuntimeSchedulerWorkItem>()
                       ?? throw new InvalidOperationException("Scheduler work post-commit intent payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsSchedulerWorkItemValidationException(argumentException))
        {
            throw new InvalidOperationException($"Scheduler work post-commit intent '{intent.IntentId}' payload is not valid scheduler work.", exception);
        }

        if (!StringComparer.Ordinal.Equals(intent.WorkflowExecutionId, workItem.WorkflowExecutionId))
            throw new InvalidOperationException($"Scheduler work post-commit intent '{intent.IntentId}' targets workflow execution '{intent.WorkflowExecutionId}', but scheduler work item '{workItem.WorkItemId}' targets workflow execution '{workItem.WorkflowExecutionId}'.");

        await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    ValueTask IRuntimePostCommitIntentHandler.HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken) =>
        DispatchAsync(intent, cancellationToken);

    private static bool IsSchedulerWorkItemValidationException(ArgumentException exception) =>
        exception.ParamName is
            "workItemId" or
            "workflowExecutionId" or
            "commandId" or
            "envelopeId" or
            "idempotencyKey" or
            "sequence";
}
