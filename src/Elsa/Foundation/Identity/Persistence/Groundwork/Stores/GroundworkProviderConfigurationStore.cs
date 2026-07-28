using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IProviderConfigurationStore"/>. Tenant provider configurations are
/// scoped by <c>tenantId:provider</c>; global provider configurations require explicit global access and
/// privileged global access for writes.
/// </summary>
public sealed class GroundworkProviderConfigurationStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor) : IProviderConfigurationStore, IRevisionAwareProviderConfigurationStore
{
    public async ValueTask<ProviderConfigurationRecord?> FindGlobalAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureGlobalAccess();
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind,
            IdentityCompositeDocumentId.Normalize(provider),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<ProviderConfigurationRecord?> FindForTenantAsync(
        string tenantId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityProviderConfigurationDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, provider),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask SaveAsync(
        ProviderConfigurationRecord configuration,
        CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(configuration, expectedVersion: null, cancellationToken);
    }

    public async ValueTask<IamRevisionedRecord<ProviderConfigurationRecord>?> FindGlobalWithRevisionAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureGlobalAccess();
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind,
            IdentityCompositeDocumentId.Normalize(provider),
            cancellationToken);

        return envelope is null
            ? null
            : new IamRevisionedRecord<ProviderConfigurationRecord>(Map(envelope), GroundworkIamRevisionMapper.Revision(envelope));
    }

    public async ValueTask<IamRevisionedRecord<ProviderConfigurationRecord>?> FindForTenantWithRevisionAsync(
        string tenantId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityProviderConfigurationDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, provider),
            cancellationToken);

        return envelope is null
            ? null
            : new IamRevisionedRecord<ProviderConfigurationRecord>(Map(envelope), GroundworkIamRevisionMapper.Revision(envelope));
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

    private async ValueTask<DocumentStoreWriteResult> SaveCoreAsync(
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
        var content = JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);
        var documentKind = configuration.TenantId is null
            ? IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind
            : IdentityStorageManifest.IdentityProviderConfigurationDocumentKind;
        var documentId = configuration.TenantId is null
            ? IdentityCompositeDocumentId.Normalize(configuration.Provider)
            : IdentityCompositeDocumentId.From(configuration.TenantId, configuration.Provider);
        return await store.SaveAsync(
            new SaveDocumentRequest(
                documentKind,
                documentId,
                IdentityStorageManifest.SchemaVersion,
                content,
                expectedVersion),
            cancellationToken);
    }

    private static ProviderConfigurationRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<IdentityProviderConfigurationDocument>(
            envelope.ContentJson,
            IdentityGroundworkJson.Options)!.Configuration;
}
