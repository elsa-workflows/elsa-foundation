using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IBookmarkStateStore"/>. It depends only on the provider-neutral
/// <see cref="IDocumentStore"/>; the concrete provider (SQLite, SQL Server, PostgreSQL, MongoDB) is
/// chosen by the host through feature composition and never leaks into this bridge or into runtime
/// domain code.
/// </summary>
public sealed class GroundworkBookmarkStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.BookmarkStateDocumentKind), IBookmarkStateStore, IBookmarkStimulusIndex
{
    public async ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.BookmarkId);

        await SaveDocumentAsync(GroundworkCompositeDocumentId.From(state.WorkflowExecutionId, state.BookmarkId), state, cancellationToken);

        return state;
    }

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);

        var result = await DeleteDocumentAsync(GroundworkCompositeDocumentId.From(workflowExecutionId, bookmarkId), cancellationToken);

        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

    public async ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);

        return await LoadDocumentAsync<BookmarkState, BookmarkState>(
            GroundworkCompositeDocumentId.From(workflowExecutionId, bookmarkId), state => state, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<BookmarkState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await QueryDocumentsAsync<BookmarkState, BookmarkState>(
            ElsaRuntimeStorageManifest.BookmarkStateByWorkflowExecution, workflowExecutionId, state => state, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<BookmarkState>> ListByStimulusAsync(string stimulusType, string stimulusHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);

        // The cross-execution index is keyed by stimulus hash only (every provider supports single-field
        // equality). Post-filter by stimulus type in code so a hash shared across two stimulus types can
        // never cross-match; the hash is type-derived in practice so this is a defensive narrowing.
        var bookmarks = await QueryDocumentsAsync<BookmarkState, BookmarkState>(
            ElsaRuntimeStorageManifest.BookmarkStateByStimulus, stimulusHash, state => state, cancellationToken);

        return bookmarks
            .Where(bookmark => StringComparer.Ordinal.Equals(bookmark.StimulusType, stimulusType))
            .ToArray();
    }
}
