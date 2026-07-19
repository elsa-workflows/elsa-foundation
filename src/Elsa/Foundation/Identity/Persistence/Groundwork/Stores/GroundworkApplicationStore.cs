using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IApplicationStore"/>. Applications are keyed by an escaped
/// <c>tenantId:applicationId</c> document id so tenant isolation is enforced before provider I/O.
/// </summary>
public sealed class GroundworkApplicationStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor) : IApplicationStore, IRevisionAwareApplicationStore
{
    public async ValueTask<ApplicationRecord?> FindAsync(
        string tenantId,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityApplicationDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, applicationId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask SaveAsync(ApplicationRecord application, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(application, expectedVersion: null, cancellationToken);
    }

    public async ValueTask<IamRevisionedRecord<ApplicationRecord>?> FindWithRevisionAsync(
        string tenantId,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityApplicationDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, applicationId),
            cancellationToken);

        return envelope is null
            ? null
            : new IamRevisionedRecord<ApplicationRecord>(Map(envelope), GroundworkIamRevisionMapper.Revision(envelope));
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(
        ApplicationRecord application,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (!GroundworkIamRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return GroundworkIamRevisionMapper.InvalidRevision();

        return GroundworkIamRevisionMapper.ToResult(
            await SaveCoreAsync(application, expectedVersion, cancellationToken));
    }

    private async ValueTask<DocumentStoreWriteResult> SaveCoreAsync(
        ApplicationRecord application,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        accessContextAccessor.EnsureCurrentScope(application.TenantId);

        var document = new IdentityApplicationDocument(
            IdentityCompositeDocumentId.Normalize(application.TenantId),
            IdentityCompositeDocumentId.Normalize(application.Id),
            application);
        var content = JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);
        return await store.SaveAsync(
            new SaveDocumentRequest(
                IdentityStorageManifest.IdentityApplicationDocumentKind,
                IdentityCompositeDocumentId.From(application.TenantId, application.Id),
                IdentityStorageManifest.SchemaVersion,
                content,
                expectedVersion),
            cancellationToken);
    }

    private static ApplicationRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<IdentityApplicationDocument>(
            envelope.ContentJson,
            IdentityGroundworkJson.Options)!.Application;
}
