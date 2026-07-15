using Elsa.Persistence.Groundwork.Serialization;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Shared base for the Groundwork document-store bridges. Each bridge maps a runtime domain contract onto
/// the provider-neutral <see cref="IDocumentStore"/> and repeats the same serialize→save, load→project and
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
    string documentKind)
{
    /// <summary>The provider-neutral document store the bridge writes through.</summary>
    protected IDocumentStore Store { get; } = store;

    /// <summary>The runtime document serializer that stamps and versions the document content.</summary>
    protected IGroundworkRuntimeDocumentSerializer Serializer { get; } = serializer;

    /// <summary>The document kind this bridge owns.</summary>
    protected string DocumentKind { get; } = documentKind;

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

    /// <summary>Queries the declared <paramref name="index"/> for <paramref name="value"/> and projects every matching document.</summary>
    protected async ValueTask<IReadOnlyList<TResult>> QueryDocumentsAsync<TDocument, TResult>(string index, string value, Func<TDocument, TResult> project, CancellationToken cancellationToken)
    {
        var envelopes = await Store.QueryAsync(new DocumentStoreQuery(DocumentKind, index, value), cancellationToken);
        return envelopes.Select(envelope => project(Serializer.Deserialize<TDocument>(envelope))).ToArray();
    }
}
