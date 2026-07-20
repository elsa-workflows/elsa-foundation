using Elsa.Persistence.Groundwork.Serialization;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Shared base for the Groundwork document-store bridges. Each bridge maps a runtime domain contract onto
/// the scoped <see cref="IDocumentStore"/> adapter and repeats the same serialize→save, load→project and
/// query→project plumbing. This base factors out that mechanical store interaction while every bridge keeps
/// its own document-envelope shape, key composition and query set.
/// </summary>
/// <remarks>
/// The persisted format is unchanged by construction: the base never owns a document record, an id or a
/// serializer call shape — subclasses still build their exact document instances and pass them through
/// <see cref="SaveDocumentAsync{TDocument}"/> / <see cref="LoadDocumentAsync{TDocument,TResult}"/> /
/// <see cref="QueryDocumentsAsync{TDocument,TResult}"/>. The golden-fixture drift suite pins that shape.
/// </remarks>
public abstract class GroundworkDocumentStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    string documentKind,
    IBoundedDocumentStore? boundedStore = null)
{
    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    /// <summary>The scoped document-store adapter the bridge writes through.</summary>
    protected IDocumentStore Store { get; } = store;

    /// <summary>The runtime document serializer that stamps and versions the document content.</summary>
    protected IGroundworkRuntimeDocumentSerializer Serializer { get; } = serializer;

    /// <summary>The document kind this bridge owns.</summary>
    protected string DocumentKind { get; } = documentKind;

    /// <summary>The admitted bounded-query runtime compiled for this bridge's document kind.</summary>
    protected IBoundedDocumentStore BoundedStore => _boundedStore ?? throw new InvalidOperationException(
        $"Document queries for '{DocumentKind}' require an admitted bounded document-store runtime.");

    /// <summary>Serialises <paramref name="document"/> under this bridge's kind and upserts it under <paramref name="documentId"/>.</summary>
    protected Task<DocumentStoreWriteResult> SaveDocumentAsync<TDocument>(
        string documentId,
        TDocument document,
        CancellationToken cancellationToken,
        long? expectedVersion = null)
    {
        var (schemaVersion, content) = Serializer.Serialize(DocumentKind, document);
        return Store.SaveAsync(new SaveDocumentRequest(DocumentKind, documentId, schemaVersion, content, expectedVersion), cancellationToken);
    }

    /// <summary>Deletes the document with <paramref name="documentId"/> and returns the store write result.</summary>
    protected Task<DocumentStoreWriteResult> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken) =>
        Store.DeleteAsync(new DeleteDocumentRequest(DocumentKind, documentId), cancellationToken);

    /// <summary>Loads the document with <paramref name="documentId"/> and projects it, or returns null when absent.</summary>
    protected async ValueTask<TResult?> LoadDocumentAsync<TDocument, TResult>(string documentId, Func<TDocument, TResult> project, CancellationToken cancellationToken)
        where TResult : class
    {
        var envelope = await Store.LoadAsync(DocumentKind, documentId, cancellationToken);
        return envelope is null ? null : project(Serializer.Deserialize<TDocument>(envelope));
    }

    /// <summary>Executes one declared equality query and projects every matching document.</summary>
    protected async ValueTask<IReadOnlyList<TResult>> QueryDocumentsAsync<TDocument, TResult>(
        string queryIdentity,
        string fieldPath,
        string value,
        Func<TDocument, TResult> project,
        CancellationToken cancellationToken)
    {
        var documents = await BoundedDocumentQueryPager.QueryAllAsync(
            BoundedStore,
            DocumentKind,
            queryIdentity,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value))],
            cancellationToken);
        return documents
            .Select(envelope => project(Serializer.Deserialize<TDocument>(envelope)))
            .ToArray();
    }
}

/// <summary>Executes admitted document routes page-by-page without truncating the logical result set.</summary>
public static class BoundedDocumentQueryPager
{
    public static async ValueTask<IReadOnlyList<DocumentEnvelope>> QueryAllAsync(
        IBoundedDocumentStore store,
        string documentKind,
        string queryIdentity,
        IReadOnlyList<DocumentQueryClause> clauses,
        CancellationToken cancellationToken)
    {
        var documents = new List<DocumentEnvelope>();
        var seenContinuations = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await store.QueryAsync(
                new DocumentQuery(
                    documentKind,
                    queryIdentity,
                    clauses,
                    take: ElsaGroundworkQueryRoutes.MaximumResultCount,
                    continuation: continuation),
                cancellationToken);
            if (result.Documents.Count > ElsaGroundworkQueryRoutes.MaximumResultCount)
            {
                throw new InvalidOperationException(
                    $"Document query '{queryIdentity}' provider page exceeded the requested bound.");
            }

            documents.AddRange(result.Documents);

            if (result.NextContinuation is null)
                break;
            if (!seenContinuations.Add(result.NextContinuation))
            {
                throw new InvalidOperationException(
                    $"Document query '{queryIdentity}' repeated a previously seen continuation.");
            }

            continuation = result.NextContinuation;
        } while (true);

        return documents;
    }

    /// <summary>
    /// Exhausts an offset-paged route. Callers must supply an order admitted by the route so that every
    /// offset addresses a stable, deterministic result sequence.
    /// </summary>
    public static async ValueTask<IReadOnlyList<DocumentEnvelope>> QueryAllOffsetAsync(
        IBoundedDocumentStore store,
        string documentKind,
        string queryIdentity,
        IReadOnlyList<DocumentQueryClause> clauses,
        IReadOnlyList<DocumentQueryOrder> order,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryIdentity);
        ArgumentNullException.ThrowIfNull(clauses);
        ArgumentNullException.ThrowIfNull(order);
        if (order.Count == 0)
        {
            throw new InvalidOperationException(
                $"Document query '{queryIdentity}' cannot be exhausted with offset paging without a deterministic declared order.");
        }

        var documents = new List<DocumentEnvelope>();
        var seenDocuments = new HashSet<(string? Scope, string Id)>();
        long? totalCount = null;
        var skip = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await store.QueryAsync(
                new DocumentQuery(
                    documentKind,
                    queryIdentity,
                    clauses,
                    order,
                    skip,
                    ElsaGroundworkQueryRoutes.MaximumResultCount),
                cancellationToken);
            if (result.Documents.Count > ElsaGroundworkQueryRoutes.MaximumResultCount)
            {
                throw new InvalidOperationException(
                    $"Document query '{queryIdentity}' provider page exceeded the requested bound.");
            }
            if (result.TotalCount < 0)
            {
                throw new InvalidOperationException(
                    $"Document query '{queryIdentity}' provider returned a negative total count.");
            }
            if (totalCount is not null && totalCount != result.TotalCount)
            {
                throw new InvalidOperationException(
                    $"Document query '{queryIdentity}' provider total count changed while offset paging.");
            }

            totalCount ??= result.TotalCount;
            if (documents.Count + (long)result.Documents.Count > totalCount)
            {
                throw new InvalidOperationException(
                    $"Document query '{queryIdentity}' provider pages exceeded the reported total count.");
            }

            if (result.Documents.Count == 0)
            {
                if (documents.Count != totalCount)
                    throw new InvalidOperationException(
                        $"Document query '{queryIdentity}' provider stopped before the reported total count was exhausted.");
                break;
            }

            foreach (var document in result.Documents)
            {
                if (!seenDocuments.Add((document.Scope?.Value, document.Id)))
                {
                    throw new InvalidOperationException(
                        $"Document query '{queryIdentity}' repeated a previously seen storage identity while offset paging.");
                }
            }

            documents.AddRange(result.Documents);
            skip = checked(skip + result.Documents.Count);
            if (documents.Count == totalCount)
                break;
        }

        return documents;
    }
}
