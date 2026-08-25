using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IProviderConfigurationStore"/>. Tenant provider configurations are
/// scoped by <c>tenantId:provider</c>; global provider configurations require explicit global access and
/// privileged global access for writes.
/// </summary>
public sealed class GroundworkProviderConfigurationStore(
    GroundworkIdentityRowStore rows,
    IPersistenceAccessContextAccessor accessContextAccessor) : IProviderConfigurationStore, IRevisionAwareProviderConfigurationStore
{
    public ValueTask<ProviderConfigurationRecord?> FindGlobalAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureGlobalAccess();
        var row = rows.Read(
            IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind,
            IdentityCompositeDocumentId.Normalize(provider),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : Map(row));
    }

    public ValueTask<ProviderConfigurationRecord?> FindForTenantAsync(
        string tenantId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityProviderConfigurationDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, provider),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : Map(row));
    }

    public async ValueTask SaveAsync(
        ProviderConfigurationRecord configuration,
        CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(configuration, expectedVersion: null, cancellationToken);
    }

    public ValueTask<IamRevisionedRecord<ProviderConfigurationRecord>?> FindGlobalWithRevisionAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureGlobalAccess();
        var row = rows.Read(
            IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind,
            IdentityCompositeDocumentId.Normalize(provider),
            cancellationToken);

        return ValueTask.FromResult(row is null
            ? null
            : new IamRevisionedRecord<ProviderConfigurationRecord>(Map(row), GroundworkIamRevisionMapper.Revision(row)));
    }

    public ValueTask<IamRevisionedRecord<ProviderConfigurationRecord>?> FindForTenantWithRevisionAsync(
        string tenantId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityProviderConfigurationDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, provider),
            cancellationToken);

        return ValueTask.FromResult(row is null
            ? null
            : new IamRevisionedRecord<ProviderConfigurationRecord>(Map(row), GroundworkIamRevisionMapper.Revision(row)));
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(
        ProviderConfigurationRecord configuration,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (!GroundworkIamRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return GroundworkIamRevisionMapper.InvalidRevision();

        var result = await SaveCoreAsync(configuration, expectedVersion, cancellationToken);
        return GroundworkIamRevisionMapper.ToResult(result);
    }

    private ValueTask<GroundworkIdentityWriteResult> SaveCoreAsync(
        ProviderConfigurationRecord configuration,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.TenantId is null)
            accessContextAccessor.EnsurePrivilegedGlobalAccess();
        else
            accessContextAccessor.EnsureCurrentScope(configuration.TenantId);

        var document = new IdentityProviderConfigurationDocument(
            configuration.TenantId is null ? null : IdentityCompositeDocumentId.Normalize(configuration.TenantId),
            IdentityCompositeDocumentId.Normalize(configuration.Provider),
            configuration);
        var documentKind = configuration.TenantId is null
            ? IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind
            : IdentityStorageManifest.IdentityProviderConfigurationDocumentKind;
        var documentId = configuration.TenantId is null
            ? IdentityCompositeDocumentId.Normalize(configuration.Provider)
            : IdentityCompositeDocumentId.From(configuration.TenantId, configuration.Provider);
        return ValueTask.FromResult(rows.Save(
            GroundworkIdentityDocumentRows.Write(
                documentKind,
                documentId,
                document,
                expectedVersion),
            cancellationToken));
    }

    private static ProviderConfigurationRecord Map(GroundworkIdentityRow row) =>
        GroundworkIdentityDocumentRows.Deserialize<IdentityProviderConfigurationDocument>(row).Configuration;
}
