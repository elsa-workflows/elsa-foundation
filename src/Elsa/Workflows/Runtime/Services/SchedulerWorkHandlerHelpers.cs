using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Shared, behavior-preserving helpers for the scheduler work-handlers (#412). Consolidates the
/// duplication families that were copied verbatim (or near-verbatim) across the Invoke / Complete /
/// Resume / Schedule / Start / Checkpoint / CreateBookmark handlers in both
/// <c>Elsa.Workflows.Runtime</c> and <c>Elsa.Activities.Runtime</c>. Public (not internal) so the
/// Activities-assembly handlers can consume it without <c>InternalsVisibleTo</c> (constitution
/// §2.23.3), matching the existing <see cref="ActivityOutputPublisher"/> precedent.
/// </summary>
public static class SchedulerWorkHandlerHelpers
{
    /// <summary>
    /// Guards that the loaded executable artifact matches the pinned snapshot the work item was
    /// scheduled against. The exception message is keyed on <see cref="RuntimeSchedulerWorkItem.CommandKind"/>,
    /// which renders identically to the per-handler command label the local copies hard-coded.
    /// </summary>
    public static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"{workItem.CommandKind} scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }

    /// <summary>
    /// Resolves an executable node by id, throwing the canonical "references executable node …, which
    /// is missing from executable artifact …" error when absent. <paramref name="commandLabel"/> is
    /// passed in (rather than derived from <see cref="RuntimeSchedulerWorkItem.CommandKind"/>) so each
    /// caller reproduces its exact historical message text (#412 item 4, per the preserve-wording ruling).
    /// </summary>
    public static ExecutableNode ResolveExecutableNode(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutable executable,
        string executableNodeId,
        string commandLabel)
    {
        if (executable.NodesById.TryGetValue(executableNodeId, out var executableNode))
            return executableNode;

        throw new InvalidOperationException(
            $"{commandLabel} scheduler work item '{workItem.WorkItemId}' references executable node '{executableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");
    }

    /// <summary>
    /// Shared payload-deserialization boilerplate (#412 item 1). Reproduces, byte-for-byte, the
    /// null-guard → deserialize-or-null-throw → catch-wrap shape the handlers each duplicated. The
    /// three message fragments, the <paramref name="deserialize"/> delegate (some handlers reuse a
    /// per-work-item memo instead of a fresh parse), and the <paramref name="isPayloadValidationException"/>
    /// catch-filter (which differs per payload type — a ParamName whitelist vs. a dedicated validation
    /// exception vs. Start's deliberately-broad filter) are all supplied by the caller so no observable
    /// behavior changes.
    /// </summary>
    public static T DeserializePayload<T>(
        RuntimeSchedulerWorkItem workItem,
        string requiresPayloadMessage,
        string resolvedToNullMessage,
        string invalidPayloadMessage,
        Func<RuntimeSchedulerWorkItem, JsonElement, T?> deserialize,
        Func<Exception, bool> isPayloadValidationException)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException(requiresPayloadMessage);

        try
        {
            return deserialize(workItem, payload)
                   ?? throw new InvalidOperationException(resolvedToNullMessage);
        }
        catch (Exception exception) when (isPayloadValidationException(exception))
        {
            throw new InvalidOperationException(invalidPayloadMessage, exception);
        }
    }

    /// <summary>
    /// Builds the <see cref="RuntimePostCommitIntentKinds.EnqueueSchedulerWork"/> post-commit intent
    /// that enqueues a follow-up scheduler work item after the source commit lands (#412 item 2).
    /// Extracted from five verbatim copies. The <c>intentId</c> and <c>idempotencyKey</c> strings and
    /// the intent kind are §E6-frozen persisted identifiers and are reproduced exactly.
    /// </summary>
    public static RuntimePostCommitIntent NewEnqueueSchedulerWorkIntent(
        RuntimeSchedulerWorkItem sourceWorkItem,
        string activityExecutionId,
        RuntimeSchedulerWorkItem schedulerWorkItem,
        DateTimeOffset recordedAt) =>
        new(
            intentId: $"{sourceWorkItem.WorkItemId}:post-commit:{schedulerWorkItem.WorkItemId}",
            workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: recordedAt,
            activityExecutionId: activityExecutionId,
            idempotencyKey: $"{sourceWorkItem.IdempotencyKey}:post-commit:{schedulerWorkItem.IdempotencyKey}",
            payload: JsonSerializer.SerializeToElement(schedulerWorkItem),
            metadata: sourceWorkItem.CommandMetadata);
}
