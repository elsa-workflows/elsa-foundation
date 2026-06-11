using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Dispatches matched bookmark resume stimuli into the workflow execution agent mailbox.
/// </summary>
public interface IBookmarkResumeDispatcher
{
    ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request, CancellationToken cancellationToken = default);
}
