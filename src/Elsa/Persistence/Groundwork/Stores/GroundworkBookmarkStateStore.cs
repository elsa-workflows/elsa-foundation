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
public sealed class GroundworkBookmarkStateStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.BookmarkStateDocumentKind), IBookmarkStateStore, IBookmarkStimulusIndex
{
    private readonly IBoundedDocumentStore? _queries = boundedStore ?? store as IBoundedDocumentStore;

    private IBoundedDocumentStore Queries => _queries ?? throw new InvalidOperationException(
        "Bookmark-state queries require an admitted bounded document-store runtime.");

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

        return await QueryBookmarksAsync(
            ElsaRuntimeStorageManifest.ListBookmarksByWorkflowExecutionQuery,
            ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
            workflowExecutionId,
            cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<BookmarkState>> ListByStimulusAsync(string stimulusType, string stimulusHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);

        // The cross-execution index is keyed by stimulus hash only (every provider supports single-field
        // equality). Post-filter by stimulus type in code so a hash shared across two stimulus types can
        // never cross-match; the hash is type-derived in practice so this is a defensive narrowing.
        var bookmarks = await QueryBookmarksAsync(
            ElsaRuntimeStorageManifest.ListBookmarksByStimulusQuery,
            ElsaRuntimeStorageManifest.StimulusHashField,
            stimulusHash,
            cancellationToken);

        return bookmarks
            .Where(bookmark => StringComparer.Ordinal.Equals(bookmark.StimulusType, stimulusType))
            .ToArray();
    }

    public async ValueTask<IReadOnlyCollection<BookmarkState>> ListByStimulusTypeAsync(string stimulusType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);

        return await QueryBookmarksAsync(
            ElsaRuntimeStorageManifest.ListBookmarksByStimulusTypeQuery,
            ElsaRuntimeStorageManifest.StimulusTypeField,
            stimulusType,
            cancellationToken);
    }

    private async ValueTask<IReadOnlyCollection<BookmarkState>> QueryBookmarksAsync(
        string queryIdentity,
        string fieldPath,
        string value,
        CancellationToken cancellationToken)
    {
        var result = await Queries.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                queryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value))]),
            cancellationToken);
        return result.Documents.Select(Serializer.Deserialize<BookmarkState>).ToArray();
    }
}
