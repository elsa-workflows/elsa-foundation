using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Delivers one committed successful-child signal through the ordinary bookmark resume path.</summary>
public sealed class ParentResumeExecutor(
    IBookmarkResumeDispatcher bookmarkResumeDispatcher,
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionStateStore activityExecutionStateStore,
    IBookmarkStateStore bookmarkStateStore,
    ILogger<ParentResumeExecutor>? logger = null) : IRuntimePostCommitIntentHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<ParentResumeExecutor> _logger = logger ?? NullLogger<ParentResumeExecutor>.Instance;

    public async ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!StringComparer.Ordinal.Equals(intent.Kind, DispatchWorkflowConstants.ResumeParentIntentKind) ||
            intent.Payload is null)
        {
            throw new InvalidOperationException($"ParentResumeExecutor cannot handle post-commit intent '{intent.IntentId}'.");
        }

        var payload = intent.Payload.Value.Deserialize<WorkflowDispatchParentResumePayload>(SerializerOptions)
            ?? throw new InvalidOperationException($"DispatchWorkflow parent-resume intent '{intent.IntentId}' has an invalid payload.");
        var identity = new WorkflowDispatchIdentity(
            payload.ParentWorkflowExecutionId,
            payload.ParentActivityExecutionId);
        ValidateIntent(intent, payload, identity);

        var result = await bookmarkResumeDispatcher.DispatchAsync(
            new BookmarkResumeDispatchRequest(
                workflowExecutionId: payload.ParentWorkflowExecutionId,
                stimulusType: payload.StimulusType,
                stimulusHash: payload.StimulusHash,
                input: intent.Payload.Value,
                idempotencyKey: identity.ParentResumeIdempotencyKey,
                requestedBy: "runtime.dispatch-workflow",
                metadata: new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.DispatchId] = payload.DispatchId,
                    [RuntimeMetadataKeys.ChildWorkflowExecutionId] = payload.ChildWorkflowExecutionId
                }),
            cancellationToken: cancellationToken);

        if (result.Bookmark is { } resolvedBookmark)
            ValidateBookmark(resolvedBookmark, payload);

        if (await IsConsumedOrTerminalAsync(payload, cancellationToken))
        {
            if (payload.Result.Status == WorkflowDispatchStatus.DispatchFailed)
            {
                _logger.LogInformation(
                    new EventId(68108, "WorkflowDispatchFailureResumeConsumed"),
                    "Workflow dispatch failure resume consumed for {DispatchId}, parent {ParentWorkflowExecutionId}, activity {ParentActivityExecutionId}",
                    payload.DispatchId,
                    payload.ParentWorkflowExecutionId,
                    payload.ParentActivityExecutionId);
            }
            return;
        }

        throw new ParentResumeDeferredException(
            payload.DispatchId,
            payload.ParentWorkflowExecutionId);
    }

    private static void ValidateIntent(
        RuntimePostCommitIntent intent,
        WorkflowDispatchParentResumePayload payload,
        WorkflowDispatchIdentity identity)
    {
        if (!StringComparer.Ordinal.Equals(intent.IntentId, identity.ParentResumeIntentId) ||
            !StringComparer.Ordinal.Equals(intent.WorkflowExecutionId, payload.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.ActivityExecutionId, payload.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.IdempotencyKey, identity.ParentResumeIdempotencyKey))
        {
            throw new InvalidOperationException($"DispatchWorkflow parent-resume intent '{intent.IntentId}' does not match its deterministic dispatch identity.");
        }
    }

    private async ValueTask<bool> IsConsumedOrTerminalAsync(
        WorkflowDispatchParentResumePayload payload,
        CancellationToken cancellationToken)
    {
        var workflow = await workflowExecutionStateStore.FindAsync(
            payload.ParentWorkflowExecutionId,
            cancellationToken);
        if (workflow is null || workflow.Status.IsTerminal())
            return true;

        var activity = await activityExecutionStateStore.FindAsync(
            payload.ParentWorkflowExecutionId,
            payload.ParentActivityExecutionId,
            cancellationToken);
        var bookmark = await bookmarkStateStore.FindAsync(
            payload.ParentWorkflowExecutionId,
            payload.BookmarkId,
            cancellationToken);
        if (bookmark is not null)
        {
            ValidateBookmark(bookmark, payload);
            return false;
        }

        return activity?.Status is ActivityExecutionStatus.Completed
            or ActivityExecutionStatus.Faulted
            or ActivityExecutionStatus.Cancelled;
    }

    private static void ValidateBookmark(
        BookmarkState bookmark,
        WorkflowDispatchParentResumePayload payload)
    {
        if (!StringComparer.Ordinal.Equals(bookmark.BookmarkId, payload.BookmarkId) ||
            !StringComparer.Ordinal.Equals(bookmark.WorkflowExecutionId, payload.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(bookmark.ActivityExecutionId, payload.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(bookmark.ResumeTargetId, DispatchWorkflowConstants.CompletionResumeTargetId) ||
            !StringComparer.Ordinal.Equals(bookmark.StimulusType, payload.StimulusType) ||
            !StringComparer.Ordinal.Equals(bookmark.StimulusHash, payload.StimulusHash))
        {
            throw new InvalidOperationException($"DispatchWorkflow bookmark '{bookmark.BookmarkId}' conflicts with dispatch '{payload.DispatchId}'.");
        }
    }
}
