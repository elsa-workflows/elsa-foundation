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
    IPersistenceAccessContextAccessor accessContextAccessor) : IProviderConfigurationStore
{
    private const string GlobalDocumentScope = "global";

    public async ValueTask<ProviderConfigurationRecord?> FindGlobalAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureGlobalAccess();
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityProviderConfigurationDocumentKind,
            IdentityCompositeDocumentId.From(GlobalDocumentScope, provider),
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
        await store.SaveAsync(
            new SaveDocumentRequest(
                IdentityStorageManifest.IdentityProviderConfigurationDocumentKind,
                IdentityCompositeDocumentId.From(configuration.TenantId ?? GlobalDocumentScope, configuration.Provider),
                IdentityStorageManifest.SchemaVersion,
                content),
            cancellationToken);
    }

    private static ProviderConfigurationRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<IdentityProviderConfigurationDocument>(
            envelope.ContentJson,
            IdentityGroundworkJson.Options)!.Configuration;
}
