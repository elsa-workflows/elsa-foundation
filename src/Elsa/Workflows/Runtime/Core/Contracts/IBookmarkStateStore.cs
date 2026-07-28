using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for durable bookmark resume handles.
/// </summary>
public interface IBookmarkStateStore
{
    /// <summary>
    /// Inserts or replaces state for the bookmark key.
    /// </summary>
    ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes state for the given workflow execution ID and bookmark ID.
    /// </summary>
    ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the bookmark state for the given workflow execution ID and bookmark ID, or <see langword="null"/> if not found.
    /// </summary>
    ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one finite, deterministic live-view page of bookmark states for the given workflow execution ID.
    /// </summary>
    ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(
        BookmarkStatePageQuery query,
        CancellationToken cancellationToken = default);
}
