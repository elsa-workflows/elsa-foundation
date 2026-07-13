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
public sealed class LegacySecretTenantBackfill(IDocumentStore store) : ILegacySecretTenantBackfill
{
    public async ValueTask<int> BackfillAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
#pragma warning disable GW0004 // Existing manifest query seam; migrating it is outside the tenancy prerequisite.
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                SecretsStorageManifest.SecretDocumentKind,
                SecretsStorageManifest.ByCollectionIndex,
                SecretsStorageManifest.SecretCollection),
            cancellationToken);
#pragma warning restore GW0004
        var migrated = 0;

        foreach (var envelope in envelopes)
        {
            var secret = SecretDocumentSerializer.Map(envelope);
            if (!string.IsNullOrWhiteSpace(secret.TenantId))
                continue;

            secret.TenantId = tenantId;
            var targetId = SecretDocumentId.From(tenantId, secret.Name);
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
                    SecretDocumentSerializer.Serialize(secret),
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

        return migrated;
    }
}
