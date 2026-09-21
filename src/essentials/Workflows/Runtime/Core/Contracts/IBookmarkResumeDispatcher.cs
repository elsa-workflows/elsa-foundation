using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Dispatches matched bookmark resume stimuli into the workflow execution agent mailbox.
/// </summary>
public interface IBookmarkResumeDispatcher
{
    /// <param name="dispatchOptions">
    /// Optional per-request dispatch options forwarded verbatim to the workflow execution agent (spec 089 FR-019).
    /// It carries the ambient request scope the in-process inline drain uses to build activity execution contexts, so
    /// a synchronous-mode endpoint's resume can author the live response on the caller's async flow.
    /// <c>null</c> ⇒ <see cref="WorkflowExecutionCommandDispatchOptions.Default"/> (identical to the pre-089 single-arg behavior).
    /// </param>
    ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
        BookmarkResumeDispatchRequest request,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
        CancellationToken cancellationToken = default);
}
