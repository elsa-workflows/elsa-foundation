using Groundwork.Core.Manifests;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Elsa.Persistence.Core;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Canonical access bindings for Groundwork test stores and provider fixtures.</summary>
public static class GroundworkTestAccess
{
    public const string DefaultScopeValue = "default";

    public static DocumentStoreAccess DefaultScoped { get; } =
        DocumentStoreAccess.Scoped(new StorageScope(DefaultScopeValue));

    public static IPersistenceAccessContextAccessor DefaultAccessContextAccessor { get; } =
        AccessContext(DefaultScopeValue);

    public static IPersistenceAccessContextAccessor AccessContext(string scope) =>
        new FixedPersistenceAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

    /// <summary>
    /// Uses global access only for an exclusively global manifest. Mixed and scoped manifests represent
    /// ambient application sessions and therefore use the default scope unless a caller supplies access explicitly.
    /// </summary>
    public static DocumentStoreAccess ForManifest(StorageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.StorageUnits.Count > 0 &&
               manifest.StorageUnits.All(unit => unit.Tenancy.Kind == TenancyKind.Global)
            ? DocumentStoreAccess.Global
            : DefaultScoped;
    }

    private sealed class FixedPersistenceAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
