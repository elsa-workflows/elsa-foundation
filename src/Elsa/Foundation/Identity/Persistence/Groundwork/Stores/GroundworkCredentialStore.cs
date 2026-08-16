using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="ICredentialStore"/>. Credentials are keyed by an escaped
/// <c>tenantId:credentialId</c> document id and store only the hashed secret material supplied by the
/// provider-neutral contract.
/// </summary>
public sealed class GroundworkCredentialStore(
    GroundworkIdentityRowStore rows,
    IPersistenceAccessContextAccessor accessContextAccessor) : ICredentialStore, IRevisionAwareCredentialStore
{
    public ValueTask<CredentialRecord?> FindAsync(
        string tenantId,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityCredentialDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, credentialId),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : Map(row));
    }

    public async ValueTask SaveAsync(CredentialRecord credential, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(credential, expectedVersion: null, cancellationToken);
    }

    public ValueTask<IamRevisionedRecord<CredentialRecord>?> FindWithRevisionAsync(
        string tenantId,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityCredentialDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, credentialId),
            cancellationToken);

        return ValueTask.FromResult(row is null
            ? null
            : new IamRevisionedRecord<CredentialRecord>(Map(row), GroundworkIamRevisionMapper.Revision(row)));
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(
        CredentialRecord credential,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (!GroundworkIamRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return GroundworkIamRevisionMapper.InvalidRevision();

        return GroundworkIamRevisionMapper.ToResult(
            await SaveCoreAsync(credential, expectedVersion, cancellationToken));
    }

    private ValueTask<GroundworkIdentityWriteResult> SaveCoreAsync(
        CredentialRecord credential,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        accessContextAccessor.EnsureCurrentScope(credential.TenantId);

        var document = new IdentityCredentialDocument(
            IdentityCompositeDocumentId.Normalize(credential.TenantId),
            IdentityCompositeDocumentId.Normalize(credential.Id),
            credential);
        return ValueTask.FromResult(rows.Save(
            GroundworkIdentityDocumentRows.Write(
                IdentityStorageManifest.IdentityCredentialDocumentKind,
                IdentityCompositeDocumentId.From(credential.TenantId, credential.Id),
                document,
                expectedVersion),
            cancellationToken));
    }

    private static CredentialRecord Map(GroundworkIdentityRow row) =>
        GroundworkIdentityDocumentRows.Deserialize<IdentityCredentialDocument>(row).Credential;
}
