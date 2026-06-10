using System.Text.Json;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.RuntimeEntities;

public sealed class GroundworkRuntimeEntityStore(IDocumentStore documentStore)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<DocumentStoreWriteResult> SaveDefinitionAsync(RuntimeEntityDefinition definition, long? expectedVersion = null, CancellationToken cancellationToken = default) =>
        documentStore.SaveAsync(new SaveDocumentRequest(
            RuntimeEntityManifestFactory.DefinitionDocumentKind,
            definition.EntityType,
            "1.0.0",
            JsonSerializer.Serialize(definition, SerializerOptions),
            expectedVersion),
            cancellationToken);

    public async Task<RuntimeEntityDefinition?> LoadDefinitionAsync(string entityType, CancellationToken cancellationToken = default)
    {
        var envelope = await documentStore.LoadAsync(RuntimeEntityManifestFactory.DefinitionDocumentKind, entityType, cancellationToken);
        return envelope is null
            ? null
            : JsonSerializer.Deserialize<RuntimeEntityDefinition>(envelope.ContentJson, SerializerOptions);
    }

    public Task<DocumentStoreWriteResult> SaveInstanceAsync(
        RuntimeEntityDefinition definition,
        string id,
        string contentJson,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default) =>
        documentStore.SaveAsync(new SaveDocumentRequest(
            RuntimeEntityManifestFactory.InstanceDocumentKind(definition),
            id,
            "1.0.0",
            contentJson,
            expectedVersion),
            cancellationToken);

    public Task<DocumentEnvelope?> LoadInstanceAsync(RuntimeEntityDefinition definition, string id, CancellationToken cancellationToken = default) =>
        documentStore.LoadAsync(RuntimeEntityManifestFactory.InstanceDocumentKind(definition), id, cancellationToken);

    public Task<IReadOnlyList<DocumentEnvelope>> QueryInstancesAsync(
        RuntimeEntityDefinition definition,
        string indexName,
        string value,
        CancellationToken cancellationToken = default) =>
        documentStore.QueryAsync(new DocumentStoreQuery(RuntimeEntityManifestFactory.InstanceDocumentKind(definition), indexName, value), cancellationToken);

    public Task<DocumentStoreWriteResult> DeleteInstanceAsync(
        RuntimeEntityDefinition definition,
        string id,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default) =>
        documentStore.DeleteAsync(new DeleteDocumentRequest(RuntimeEntityManifestFactory.InstanceDocumentKind(definition), id, expectedVersion), cancellationToken);
}
