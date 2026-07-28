using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="ICredentialStore"/>. Credentials are keyed by an escaped
/// <c>tenantId:credentialId</c> document id and store only the hashed secret material supplied by the
/// provider-neutral contract.
/// </summary>
public sealed class GroundworkCredentialStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor) : ICredentialStore, IRevisionAwareCredentialStore
{
    public async ValueTask<CredentialRecord?> FindAsync(
        string tenantId,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityCredentialDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, credentialId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask SaveAsync(CredentialRecord credential, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(credential, expectedVersion: null, cancellationToken);
    }

    public async ValueTask<IamRevisionedRecord<CredentialRecord>?> FindWithRevisionAsync(
        string tenantId,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityCredentialDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, credentialId),
            cancellationToken);

        return envelope is null
            ? null
            : new IamRevisionedRecord<CredentialRecord>(Map(envelope), GroundworkIamRevisionMapper.Revision(envelope));
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

    private async ValueTask<DocumentStoreWriteResult> SaveCoreAsync(
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
        var content = JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);
        return await store.SaveAsync(
            new SaveDocumentRequest(
                IdentityStorageManifest.IdentityCredentialDocumentKind,
                IdentityCompositeDocumentId.From(credential.TenantId, credential.Id),
                IdentityStorageManifest.SchemaVersion,
                content,
                expectedVersion),
            cancellationToken);
    }

    private static CredentialRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<IdentityCredentialDocument>(
            envelope.ContentJson,
            IdentityGroundworkJson.Options)!.Credential;
}
