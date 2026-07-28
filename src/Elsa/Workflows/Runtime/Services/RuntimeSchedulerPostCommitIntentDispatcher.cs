using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeSchedulerPostCommitIntentDispatcher(
    IWorkflowSchedulerWorkQueue schedulerWorkQueue,
    IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor = null,
    RuntimeInProcessHopFastPathOptions? inProcessHopFastPathOptions = null)
    : IRuntimePostCommitIntentDispatcher, IRuntimePostCommitIntentHandler
{
    private readonly RuntimeInProcessHopFastPathOptions _fastPathOptions =
        inProcessHopFastPathOptions ?? new RuntimeInProcessHopFastPathOptions();

    public async ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();

        if (!StringComparer.Ordinal.Equals(intent.Kind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork))
            throw new InvalidOperationException($"Unsupported runtime post-commit intent kind '{intent.Kind}'.");

        var workItem = ResolveWorkItem(intent);

        if (!StringComparer.Ordinal.Equals(intent.WorkflowExecutionId, workItem.WorkflowExecutionId))
            throw new InvalidOperationException($"Scheduler work post-commit intent '{intent.IntentId}' targets workflow execution '{intent.WorkflowExecutionId}', but scheduler work item '{workItem.WorkItemId}' targets workflow execution '{workItem.WorkflowExecutionId}'.");

        await schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    // WU-3 / spec 109 (ADR 0031 follow-up (a)): the in-process-hop fast path. When a live drain owns this exact
    // execution's delivery AND the checkpoint committer published the continuation onto that drain's carrier, take the
    // already-materialized work item and skip the JSON round-trip. The durable intent payload stays authoritative: any
    // absence (fast path off, coalescing overlay, sweep/recovery with no live-drain scope, a different execution's
    // scope, or a durable store that stripped the in-memory conduit) falls through to deserialize, producing an
    // identical work item — memory is a cache, the serialized form is truth. The exec-id validation below runs for both
    // paths, so a cached item is held to the same contract as a deserialized one.
    private RuntimeSchedulerWorkItem ResolveWorkItem(RuntimePostCommitIntent intent)
    {
        if (_fastPathOptions.Enabled &&
            liveDrainDeliveryAccessor?.Current is { } scope &&
            scope.AppliesTo(intent.WorkflowExecutionId) &&
            scope.TryTakeHopWorkItem(intent.IntentId, out var cached))
            return cached;

        return DeserializeWorkItem(intent);
    }

    private static RuntimeSchedulerWorkItem DeserializeWorkItem(RuntimePostCommitIntent intent)
    {
        if (intent.Payload is not { } payload)
            throw new InvalidOperationException($"Scheduler work post-commit intent '{intent.IntentId}' requires a scheduler work payload.");

        try
        {
            return payload.Deserialize<RuntimeSchedulerWorkItem>()
                   ?? throw new InvalidOperationException("Scheduler work post-commit intent payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsSchedulerWorkItemValidationException(argumentException))
        {
            throw new InvalidOperationException($"Scheduler work post-commit intent '{intent.IntentId}' payload is not valid scheduler work.", exception);
        }
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
