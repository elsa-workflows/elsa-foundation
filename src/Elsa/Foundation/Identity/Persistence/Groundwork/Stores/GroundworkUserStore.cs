using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IUserStore"/>. Users are keyed by an escaped composite
/// <c>tenantId:userId</c> document id and carry an <c>emailKey</c> index (<c>tenantId:email</c>) so
/// email lookups resolve through the declared index rather than a scan.
/// </summary>
public sealed class GroundworkUserStore(
    GroundworkIdentityRowStore rows,
    IPersistenceAccessContextAccessor accessContextAccessor,
    GroundworkIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null,
    IIdentityEmailUniquenessPolicy? emailUniquenessPolicy = null) : IUserStore, IRevisionAwareUserStore
{
    private const int AmbiguousEmailTake = 2;

    private readonly GroundworkIdentityAuthorityAggregateCoordinator _aggregates =
        aggregateCoordinator ?? GroundworkIdentityAuthorityAggregateCoordinator.ForRows(rows);
    private readonly IIdentityEmailUniquenessPolicy _emailUniquenessPolicy =
        emailUniquenessPolicy ?? IdentityEmailUniquenessPolicy.NonUnique;

    public ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : Map(row));
    }

    public ValueTask<IamRevisionedRecord<UserRecord>?> FindWithRevisionAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : new IamRevisionedRecord<UserRecord>(Map(row), GroundworkIamRevisionMapper.Revision(row)));
    }

    public ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var matches = rows.Query(
            IdentityStorageManifest.IdentityUserDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.NormalizedEmailKeyField,
                GroundworkIdentityRowComparison.Equal,
                ScopedLookupKey(tenantId, email)!,
                IdentityV2StorageManifest.IdField,
                Take: AmbiguousEmailTake,
                ExpectedIndex: IdentityV2StorageManifest.UserByNormalizedEmailIndex),
            cancellationToken);
        return ValueTask.FromResult(matches.Count == 1 ? Map(matches[0]) : null);
    }

    public async ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(user, expectedVersion: null, cancellationToken);
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(UserRecord user, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!GroundworkIamRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return GroundworkIamRevisionMapper.InvalidRevision();

        var result = await SaveCoreAsync(user, expectedVersion, cancellationToken);
        return GroundworkIamRevisionMapper.ToResult(result);
    }

    private async ValueTask<GroundworkIdentityWriteResult> SaveCoreAsync(UserRecord user, long? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        accessContextAccessor.EnsureCurrentScope(user.TenantId);

        var documentId = IdentityCompositeDocumentId.From(user.TenantId, user.Id);
        var existing = rows.Read(
            IdentityStorageManifest.IdentityUserDocumentKind,
            documentId,
            cancellationToken);
        var existingDocument = existing is null ? null : GroundworkIdentityDocumentRows.Deserialize<IdentityUserDocument>(existing);

        var document = new IdentityUserDocument(
            user.TenantId,
            user.Id,
            Normalize(user.UserName),
            Normalize(user.Email),
            ScopedLookupKey(user.TenantId, user.UserName),
            ScopedLookupKey(user.TenantId, user.Email),
            user,
            existingDocument?.FrameworkState);
        var result = await _aggregates.SaveUserAsync(
            document,
            expectedVersion,
            _emailUniquenessPolicy.RequireUniqueEmail,
            cancellationToken);
        return result.WriteResult;
    }

    private static string Normalize(string? value) => IdentityCompositeDocumentId.Normalize(value);

    private static string? ScopedLookupKey(string tenantId, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : IdentityDocumentId.From(tenantId, value);

    private static UserRecord Map(GroundworkIdentityRow row) =>
        GroundworkIdentityDocumentRows.Deserialize<IdentityUserDocument>(row).User;
}
