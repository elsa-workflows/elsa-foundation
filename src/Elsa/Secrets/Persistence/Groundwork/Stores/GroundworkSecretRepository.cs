using System.Text.Json;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Secrets.Persistence.Groundwork.Stores;

public sealed class GroundworkSecretRepository(
    IDocumentStore store,
    IBoundedDocumentStore? boundedStore = null) : ISecretRepository, IRevisionAwareSecretRepository, IPagedSecretRepository
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

    public async ValueTask<SecretRevisionedRecord?> FindWithRevisionAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);

        var envelope = await store.LoadAsync(
            SecretsStorageManifest.SecretDocumentKind,
            normalizedName,
            cancellationToken);

        return envelope is null ? null : new SecretRevisionedRecord(Map(envelope), SecretRevisionMapper.Revision(envelope));
    }

    public async ValueTask<IReadOnlyCollection<Secret>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await BoundedStore.QueryAsync(
            ListQuery(skip: null, take: null),
            cancellationToken);
        return result.Documents.Select(Map).ToArray();
    }

    public async ValueTask<SecretRepositoryPage> ListPageAsync(SecretRepositoryListRequest request, CancellationToken cancellationToken = default)
    {
        var result = await QueryPageAsync(request, cancellationToken);
        return new SecretRepositoryPage(result.Documents.Select(Map).ToArray(), result.TotalCount);
    }

    public async ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var result = await SaveCoreAsync(secret, expectedVersion: 0, cancellationToken);
        return result.Status is DocumentStoreWriteStatus.Saved;
    }

    public async ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        var result = await SaveCoreAsync(secret, expectedVersion: null, cancellationToken);
        if (result.Status is not DocumentStoreWriteStatus.Saved)
            throw new InvalidOperationException($"Could not save secret '{secret.Name}'; Groundwork returned {result.Status}.");
    }

    public async ValueTask<SecretRevisionSaveResult> SaveWithRevisionAsync(Secret secret, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!SecretRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return SecretRevisionMapper.InvalidRevision();

        var result = await SaveCoreAsync(secret, expectedVersion, cancellationToken);
        return SecretRevisionMapper.ToResult(result);
    }

    private Task<DocumentQueryResult> QueryPageAsync(SecretRepositoryListRequest request, CancellationToken cancellationToken) =>
        BoundedStore.QueryAsync(
            ListQuery(request.NormalizedSkip, request.NormalizedTake),
            cancellationToken);

    private static DocumentQuery ListQuery(int? skip, int? take) => new(
        SecretsStorageManifest.SecretDocumentKind,
        SecretsStorageManifest.ListAllQuery,
        [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
            SecretsStorageManifest.CollectionField,
            SecretsStorageManifest.SecretCollection))],
        [],
        skip,
        take);

    private async ValueTask<DocumentStoreWriteResult> SaveCoreAsync(Secret secret, long? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret.Name);

        var document = new SecretDocument(SecretsStorageManifest.SecretCollection, secret);
        var content = JsonSerializer.Serialize(document, SecretsGroundworkJson.Options);

        return await store.SaveAsync(
            new SaveDocumentRequest(
                SecretsStorageManifest.SecretDocumentKind,
                secret.Name,
                SecretsStorageManifest.SchemaVersion,
                content,
                expectedVersion),
            cancellationToken);
    }

    private static Secret Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<SecretDocument>(envelope.ContentJson, SecretsGroundworkJson.Options)!.Secret;

    private sealed record SecretDocument(string Collection, Secret Secret);
}
