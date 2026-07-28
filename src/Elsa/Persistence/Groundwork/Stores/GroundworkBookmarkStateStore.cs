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

    public async ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(
        BookmarkStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await Queries.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.ListBookmarksByWorkflowExecutionQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, query.WorkflowExecutionId)],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.BookmarkIdField)],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);

        return new RuntimeStorePage<BookmarkState>(
            query,
            result.Documents.Select(Serializer.Deserialize<BookmarkState>).ToArray(),
            result.NextContinuation);
    }

    public async ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(
        BookmarkStimulusPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await QueryStimulusPageAsync(
            ElsaRuntimeStorageManifest.ListBookmarksByStimulusAndTypeQuery,
            query,
            [
                Equal(
                    ElsaRuntimeStorageManifest.BookmarkStimulusLookupKeyField,
                    StimulusLookupKey.FromPair(query.StimulusType, query.StimulusHash))
            ],
            cancellationToken);
    }

    public async ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusTypePageAsync(
        BookmarkStimulusTypePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await QueryStimulusPageAsync(
            ElsaRuntimeStorageManifest.ListBookmarksByStimulusTypeQuery,
            query,
            [
                Equal(
                    ElsaRuntimeStorageManifest.BookmarkStimulusTypeLookupKeyField,
                    StimulusLookupKey.FromType(query.StimulusType))
            ],
            cancellationToken);
    }

    private async ValueTask<RuntimeStorePage<BookmarkState>> QueryStimulusPageAsync(
        string queryIdentity,
        RuntimeStorePageRequest query,
        IReadOnlyList<DocumentQueryClause> clauses,
        CancellationToken cancellationToken)
    {
        var result = await Queries.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                queryIdentity,
                clauses,
                [
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
                    new DocumentQueryOrder(ElsaRuntimeStorageManifest.BookmarkIdField)
                ],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);
        return new RuntimeStorePage<BookmarkState>(
            query,
            result.Documents.Select(Serializer.Deserialize<BookmarkState>).ToArray(),
            result.NextContinuation);
    }

    private static DocumentQueryClause Equal(string fieldPath, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value));
}
