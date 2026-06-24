using System.Text.Json;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Secrets.Persistence.Groundwork.Stores;

public sealed class GroundworkSecretRepository(IDocumentStore store) : ISecretRepository
{
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
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                SecretsStorageManifest.SecretDocumentKind,
                SecretsStorageManifest.ByCollectionIndex,
                SecretsStorageManifest.SecretCollection),
            cancellationToken);

        return envelopes.Select(Map).Where(x => x.Status != SecretStatus.Deleted).ToArray();
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
