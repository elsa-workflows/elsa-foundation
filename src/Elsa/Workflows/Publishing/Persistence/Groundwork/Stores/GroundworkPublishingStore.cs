using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public abstract class GroundworkPublishingStore
{
    private readonly IBoundedDocumentStore? _queries;

    protected GroundworkPublishingStore(
        IDocumentStore store,
        PublishingGroundworkDocumentSerializer serializer,
        string documentKind,
        IBoundedDocumentStore? queries = null)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        DocumentKind = documentKind;
        _queries = queries;
    }

    protected IDocumentStore Store { get; }
    protected PublishingGroundworkDocumentSerializer Serializer { get; }
    protected string DocumentKind { get; }
    protected IBoundedDocumentStore BoundedStore => _queries ?? throw new InvalidOperationException(
        $"Publishing store '{DocumentKind}' requires a certified bounded document-query runtime.");

    protected async ValueTask<(DocumentEnvelope Envelope, T Document)?> LoadAsync<T>(string id, CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, id, cancellationToken);
        return envelope is null ? null : (envelope, Serializer.Deserialize<T>(envelope));
    }

    protected Task<DocumentStoreWriteResult> SaveAsync<T>(string id, T document, long? expectedVersion, CancellationToken cancellationToken)
    {
        var (schemaVersion, content) = Serializer.Serialize(DocumentKind, document);
        return Store.SaveAsync(new SaveDocumentRequest(DocumentKind, id, schemaVersion, content, expectedVersion), cancellationToken);
    }

    protected async ValueTask<IReadOnlyCollection<T>> QueryAsync<T>(
        string queryIdentity,
        string fieldPath,
        string value,
        CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                queryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value))]),
            cancellationToken);
        return result.Documents.Select(Serializer.Deserialize<T>).ToArray();
    }
}
