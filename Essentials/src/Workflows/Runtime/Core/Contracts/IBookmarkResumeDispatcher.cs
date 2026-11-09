using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Dispatches matched bookmark resume stimuli into the workflow execution agent mailbox.
/// </summary>
public interface IBookmarkResumeDispatcher
{
    /// <summary>Qualify the actual source/material/execution services for saved stimulus resumes; legacy dispatchers refuse.</summary>
    bool CanDispatchSavedStimulus(IStimulusDeliveryStore sources, IWorkflowExecutableStore material,
        IWorkflowExecutionStateStore executions) => false;

    /// <summary>Resolve an exact persisted resume entry and its immutable input before the accepted queued handoff.</summary>
    /// <exception cref="Exceptions.StimulusDeliveryBoundaryException">Source, entry, adapter or provider support refuses;
    /// unavailable authority is distinguished and no replacement wait may be dispatched.</exception>
    /// <exception cref="Exceptions.RuntimePostCommitOutboxStaleClaimException">The current physical source claim is stale.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask<BookmarkResumeDispatchResult> DispatchAsync(PostCommitDeliveryContext source, string workflowExecutionId,
        string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId); cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<BookmarkResumeDispatchResult>(new Exceptions.StimulusDeliveryBoundaryException(source.SourceOutboxItemId,
            new NotSupportedException("The dispatcher cannot deliver an exact saved stimulus wait."), false));
    }

    /// <param name="dispatchOptions">
    /// Optional per-request dispatch options forwarded verbatim to the workflow execution agent.
    /// It carries the ambient request scope the in-process inline drain uses to build activity execution contexts, so
    /// a synchronous-mode endpoint's resume can author the live response on the caller's async flow.
    /// <c>null</c> ⇒ <see cref="WorkflowExecutionCommandDispatchOptions.Default"/> (identical to the pre-089 single-arg behavior).
    /// </param>
    ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
        BookmarkResumeDispatchRequest request,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
        CancellationToken cancellationToken = default);
}
