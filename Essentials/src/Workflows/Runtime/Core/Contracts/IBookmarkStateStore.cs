using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores split continuation state for durable bookmark resume handles.
/// </summary>
public interface IBookmarkStateStore
{
    /// <summary>Reads the current physical replacement generation with its state; legacy stores refuse.</summary>
    /// <remarks>This observation is not admission, source authority or permission to consume a wait. Providers must
    /// read committed physical state; the EF implementation requires a clean context with no local, enlisted or
    /// ambient transaction and preserves pending caller changes when refusing that precondition.</remarks>
    /// <exception cref="NotSupportedException">The provider cannot observe exact physical generations.</exception>
    /// <exception cref="ArgumentException">An identity is blank or exceeds provider storage bounds.</exception>
    /// <exception cref="InvalidOperationException">Committed observation preconditions are unavailable, or a
    /// provider-specific contextual persistence exception reports a failed read with its original cause.</exception>
    /// <exception cref="InvalidDataException">Stored identity, projection or content is contradictory.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    ValueTask<BookmarkStateObservation?> ObserveAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId); ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException<BookmarkStateObservation?>(new NotSupportedException("Exact bookmark observations are unavailable."));
    }

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
