using System.Text.Json;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Secrets.Persistence.Groundwork.Stores;

public sealed class GroundworkSecretRepository(
    IDocumentStore store,
    IBoundedDocumentStore? boundedStore = null) : ISecretRepository
{
    private IBoundedDocumentStore BoundedStore => boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
        "Secret queries require an admitted bounded document-store runtime.");

    public async ValueTask<Secret?> FindAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);

        var envelope = await store.LoadAsync(
            SecretsStorageManifest.SecretDocumentKind,
            normalizedName,
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyCollection<Secret>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                SecretsStorageManifest.SecretDocumentKind,
                SecretsStorageManifest.ListAllQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    SecretsStorageManifest.CollectionField,
                    SecretsStorageManifest.SecretCollection))]),
            cancellationToken);

        return result.Documents.Select(Map).ToArray();
    }

    public async ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var existing = await FindAsync(secret.Name, cancellationToken);

        if (existing is not null)
            return false;

        await SaveAsync(secret, cancellationToken);
        return true;
    }

    public async ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret.Name);

        var document = new SecretDocument(SecretsStorageManifest.SecretCollection, secret);
        var content = JsonSerializer.Serialize(document, SecretsGroundworkJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                SecretsStorageManifest.SecretDocumentKind,
                secret.Name,
                SecretsStorageManifest.SchemaVersion,
                content),
            cancellationToken);
    }

    private static Secret Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<SecretDocument>(envelope.ContentJson, SecretsGroundworkJson.Options)!.Secret;

    private sealed record SecretDocument(string Collection, Secret Secret);
}
