using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Secrets.Persistence.Groundwork.Stores;

public interface ILegacySecretTenantBackfill
{
    ValueTask<int> BackfillAsync(string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit operator-controlled migration for pre-tenancy secret documents. Legacy rows remain
/// invisible to tenant-scoped repository operations until this service is called with a target tenant.
/// </summary>
public sealed class LegacySecretTenantBackfill(
    IDocumentStore store,
    IBoundedDocumentStore? boundedStore = null) : ILegacySecretTenantBackfill
{
    private IBoundedDocumentStore BoundedStore => boundedStore ?? store as IBoundedDocumentStore
        ?? throw new InvalidOperationException("Legacy secret backfill requires an admitted bounded document-store runtime.");

    public async ValueTask<int> BackfillAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var migrated = 0;

        while (true)
        {
            var page = await BoundedStore.QueryAsync(
                new DocumentQuery(
                    SecretsStorageManifest.SecretDocumentKind,
                    SecretsStorageManifest.LegacyBackfillQuery,
                    [
                        DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                            SecretsStorageManifest.CollectionField,
                            SecretsStorageManifest.SecretCollection)),
                        DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                            SecretsStorageManifest.TenantIdField,
                            null))
                    ],
                    [],
                    take: 250),
                cancellationToken);
            if (page.Documents.Count == 0)
                return migrated;

            foreach (var envelope in page.Documents)
            {
                var document = GroundworkSecretRepository.Deserialize(envelope.ContentJson);
                var secret = document.Secret;
                if (!string.IsNullOrWhiteSpace(document.TenantId) &&
                    !string.IsNullOrWhiteSpace(secret.TenantId) &&
                    !string.Equals(document.TenantId, secret.TenantId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Secret document contains conflicting tenant identities.");
                if (!string.IsNullOrWhiteSpace(secret.TenantId))
                    continue;

                secret.TenantId = tenantId;
                var targetId = GroundworkSecretRepository.DocumentId(tenantId, secret.Name);
                await using var unitOfWork = await store.BeginAsync(
                    DocumentCommitScope.Of(SecretsStorageManifest.SecretDocumentKind),
                    cancellationToken);
                if (await unitOfWork.LoadAsync(SecretsStorageManifest.SecretDocumentKind, targetId, cancellationToken) is not null)
                    throw new InvalidOperationException($"Cannot backfill legacy secret '{secret.Name}' because that tenant already has a secret with the same name.");

                var saved = await unitOfWork.SaveAsync(
                    new SaveDocumentRequest(
                        SecretsStorageManifest.SecretDocumentKind,
                        targetId,
                        SecretsStorageManifest.SchemaVersion,
                        System.Text.Json.JsonSerializer.Serialize(
                            GroundworkSecretRepository.SecretDocument.FromSecret(secret),
                            SecretsGroundworkJson.Options),
                        ExpectedVersion: 0),
                    cancellationToken);
                if (saved.Status != DocumentStoreWriteStatus.Saved)
                    throw new InvalidOperationException($"Cannot backfill legacy secret '{secret.Name}' because its tenant target changed while migration was running.");

                var deleted = await unitOfWork.DeleteAsync(
                    new DeleteDocumentRequest(
                        SecretsStorageManifest.SecretDocumentKind,
                        envelope.Id,
                        envelope.Version),
                    cancellationToken);
                if (deleted.Status != DocumentStoreWriteStatus.Deleted)
                    throw new InvalidOperationException($"Legacy secret '{secret.Name}' changed while tenant backfill was running.");

                await unitOfWork.CommitAsync(cancellationToken);
                migrated++;
            }
        }
    }
}
