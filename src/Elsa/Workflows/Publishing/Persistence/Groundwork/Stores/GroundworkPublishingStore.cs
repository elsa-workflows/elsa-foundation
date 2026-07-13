using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

public abstract class GroundworkPublishingStore(
    IDocumentStore store,
    PublishingGroundworkDocumentSerializer serializer,
    string documentKind)
{
    protected IDocumentStore Store { get; } = store;
    protected PublishingGroundworkDocumentSerializer Serializer { get; } = serializer;
    protected string DocumentKind { get; } = documentKind;

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

    protected async ValueTask<IReadOnlyCollection<T>> QueryAsync<T>(string index, string value, CancellationToken cancellationToken)
    {
        var envelopes = await Store.QueryAsync(new DocumentStoreQuery(DocumentKind, index, value), cancellationToken);
        return envelopes.Select(Serializer.Deserialize<T>).ToArray();
    }
}
