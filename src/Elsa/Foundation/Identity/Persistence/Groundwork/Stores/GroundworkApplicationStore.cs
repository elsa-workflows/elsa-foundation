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
    IPersistenceAccessContextAccessor accessContextAccessor) : IApplicationStore
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
        ArgumentNullException.ThrowIfNull(application);
        accessContextAccessor.EnsureCurrentScope(application.TenantId);

        var document = new IdentityApplicationDocument(
            IdentityCompositeDocumentId.Normalize(application.TenantId),
            IdentityCompositeDocumentId.Normalize(application.Id),
            application);
        var content = JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);
        await store.SaveAsync(
            new SaveDocumentRequest(
                IdentityStorageManifest.IdentityApplicationDocumentKind,
                IdentityCompositeDocumentId.From(application.TenantId, application.Id),
                IdentityStorageManifest.SchemaVersion,
                content),
            cancellationToken);
    }

    private static ApplicationRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<IdentityApplicationDocument>(
            envelope.ContentJson,
            IdentityGroundworkJson.Options)!.Application;
}
