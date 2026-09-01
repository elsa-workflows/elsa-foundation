using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IApplicationStore"/>. Applications are keyed by an escaped
/// <c>tenantId:applicationId</c> document id so tenant isolation is enforced before provider I/O.
/// </summary>
public sealed class GroundworkApplicationStore(
    GroundworkIdentityRowStore rows,
    IPersistenceAccessContextAccessor accessContextAccessor) : IApplicationStore, IRevisionAwareApplicationStore
{
    public ValueTask<ApplicationRecord?> FindAsync(
        string tenantId,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityApplicationDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, applicationId),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : Map(row));
    }

    public async ValueTask SaveAsync(ApplicationRecord application, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(application, expectedVersion: null, cancellationToken);
    }

    public ValueTask<IamRevisionedRecord<ApplicationRecord>?> FindWithRevisionAsync(
        string tenantId,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityApplicationDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, applicationId),
            cancellationToken);

        return ValueTask.FromResult(row is null
            ? null
            : new IamRevisionedRecord<ApplicationRecord>(Map(row), GroundworkIamRevisionMapper.Revision(row)));
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

    private ValueTask<GroundworkIdentityWriteResult> SaveCoreAsync(
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
        return ValueTask.FromResult(rows.Save(
            GroundworkIdentityDocumentRows.Write(
                IdentityStorageManifest.IdentityApplicationDocumentKind,
                IdentityCompositeDocumentId.From(application.TenantId, application.Id),
                document,
                expectedVersion),
            cancellationToken));
    }

    private static ApplicationRecord Map(GroundworkIdentityRow row) =>
        GroundworkIdentityDocumentRows.Deserialize<IdentityApplicationDocument>(row).Application;
}
