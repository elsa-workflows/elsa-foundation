using System.Collections.Frozen;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Supplies the durable storage declaration owned by one selected Groundwork feature family.
/// Implementations live with their feature adapters; the host-owned composition module consumes
/// this acyclic seam before a provider is materialized.
/// </summary>
public interface IGroundworkStorageManifestSource
{
    /// <summary>Gets the stable feature identity used for ordering, ownership and diagnostics.</summary>
    string FeatureIdentity { get; }

    /// <summary>Creates the feature's provider-neutral durable storage declaration.</summary>
    ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable declaration of one selected feature family's manifest and durable requirements.
/// </summary>
public sealed class GroundworkStorageManifestDeclaration
{
    public GroundworkStorageManifestDeclaration(
        string featureIdentity,
        StorageManifest manifest,
        IReadOnlyCollection<Type> requiredStoreContracts,
        IReadOnlyCollection<GroundworkStorageRouteRequirement> requiredRoutes,
        IReadOnlyCollection<GroundworkStorageTopologyRequirement> topologyRequirements,
        IReadOnlyCollection<string> coverageRows,
        IReadOnlyDictionary<StorageUnitIdentity, GroundworkStorageScopeClassification>? scopeClassifications = null)
    {
        FeatureIdentity = GroundworkCompositionImmutability.RequiredIdentity(
            featureIdentity,
            nameof(featureIdentity),
            "A stable feature identity is required.");
        Manifest = GroundworkCompositionImmutability.CopyManifest(
            manifest ?? throw new ArgumentNullException(nameof(manifest)));
        RequiredStoreContracts = GroundworkCompositionImmutability.ReadOnlyCopy(
            requiredStoreContracts,
            nameof(requiredStoreContracts));
        RequiredRoutes = GroundworkCompositionImmutability.ReadOnlyCopy(
            requiredRoutes,
            nameof(requiredRoutes));
        TopologyRequirements = GroundworkCompositionImmutability.ReadOnlyCopy(
            topologyRequirements,
            nameof(topologyRequirements));
        CoverageRows = GroundworkCompositionImmutability.ReadOnlyIdentityCopy(
            coverageRows,
            nameof(coverageRows),
            "Coverage row identities cannot be blank.");

        var classifications = scopeClassifications is null
            ? DeriveScopeClassifications(Manifest.StorageUnits)
            : CopyAndValidateScopeClassifications(scopeClassifications, Manifest.StorageUnits);
        ScopeClassifications = classifications.ToFrozenDictionary();
    }

    public string FeatureIdentity { get; }

    public StorageManifest Manifest { get; }

    public IReadOnlyList<Type> RequiredStoreContracts { get; }

    public IReadOnlyList<GroundworkStorageRouteRequirement> RequiredRoutes { get; }

    public IReadOnlyList<GroundworkStorageTopologyRequirement> TopologyRequirements { get; }

    public IReadOnlyList<string> CoverageRows { get; }

    /// <summary>
    /// Gets the explicit Elsa scope classification for every storage unit in <see cref="Manifest"/>.
    /// Privileged access is intentionally not represented here; it is a separate operation capability.
    /// </summary>
    public IReadOnlyDictionary<StorageUnitIdentity, GroundworkStorageScopeClassification> ScopeClassifications { get; }

    private static Dictionary<StorageUnitIdentity, GroundworkStorageScopeClassification> DeriveScopeClassifications(
        IReadOnlyCollection<StorageUnit> storageUnits)
    {
        var result = new Dictionary<StorageUnitIdentity, GroundworkStorageScopeClassification>();
        foreach (var unit in storageUnits)
            result.Add(unit.Identity, ToScopeClassification(unit));
        return result;
    }

    private static Dictionary<StorageUnitIdentity, GroundworkStorageScopeClassification> CopyAndValidateScopeClassifications(
        IReadOnlyDictionary<StorageUnitIdentity, GroundworkStorageScopeClassification> classifications,
        IReadOnlyCollection<StorageUnit> storageUnits)
    {
        ArgumentNullException.ThrowIfNull(classifications);
        var result = new Dictionary<StorageUnitIdentity, GroundworkStorageScopeClassification>(classifications);
        var unitIdentities = storageUnits.Select(unit => unit.Identity).ToFrozenSet();
        var missing = unitIdentities.Except(result.Keys).OrderBy(identity => identity.Value, StringComparer.Ordinal).ToArray();
        var unknown = result.Keys.Except(unitIdentities).OrderBy(identity => identity.Value, StringComparer.Ordinal).ToArray();

        if (missing.Length > 0 || unknown.Length > 0)
        {
            var details = string.Join(
                "; ",
                new[]
                {
                    missing.Length == 0 ? null : $"missing: {string.Join(", ", missing.Select(x => x.Value))}",
                    unknown.Length == 0 ? null : $"unknown: {string.Join(", ", unknown.Select(x => x.Value))}"
                }.Where(x => x is not null));
            throw new ArgumentException(
                $"Scope classifications must identify every manifest storage unit exactly once ({details}).",
                nameof(classifications));
        }

        foreach (var unit in storageUnits)
        {
            var expected = ToScopeClassification(unit);
            if (result[unit.Identity] != expected)
            {
                throw new ArgumentException(
                    $"Scope classification for storage unit '{unit.Identity.Value}' does not match its manifest tenancy.",
                    nameof(classifications));
            }
        }

        return result;
    }

    private static GroundworkStorageScopeClassification ToScopeClassification(StorageUnit unit) =>
        unit.Tenancy.Kind switch
        {
            TenancyKind.Scoped => GroundworkStorageScopeClassification.TenantScoped,
            TenancyKind.Global => GroundworkStorageScopeClassification.ExplicitlyGlobal,
            _ => throw new ArgumentException(
                $"Storage unit '{unit.Identity.Value}' uses tenancy '{unit.Tenancy.Kind}', which is not an admitted Elsa scope classification.",
                nameof(unit))
        };
}

/// <summary>Elsa's admitted storage-scope classifications for a selected manifest unit.</summary>
public enum GroundworkStorageScopeClassification
{
    TenantScoped,
    ExplicitlyGlobal
}

/// <summary>Declares the capabilities needed by one executable storage route.</summary>
public sealed class GroundworkStorageRouteRequirement
{
    public GroundworkStorageRouteRequirement(
        StorageUnitIdentity storageUnit,
        string routeIdentity,
        IReadOnlySet<CapabilityId> requiredCapabilities)
    {
        StorageUnit = storageUnit ?? throw new ArgumentNullException(nameof(storageUnit));
        GroundworkCompositionImmutability.RequiredIdentity(
            storageUnit.Value,
            nameof(storageUnit),
            "A storage-unit identity is required.");
        RouteIdentity = GroundworkCompositionImmutability.RequiredIdentity(
            routeIdentity,
            nameof(routeIdentity),
            "A stable route identity is required.");
        RequiredCapabilities = (requiredCapabilities ?? throw new ArgumentNullException(nameof(requiredCapabilities)))
            .ToFrozenSet();
    }

    public StorageUnitIdentity StorageUnit { get; }

    public string RouteIdentity { get; }

    public IReadOnlySet<CapabilityId> RequiredCapabilities { get; }
}

/// <summary>Declares one stable provider-topology prerequisite.</summary>
public sealed record GroundworkStorageTopologyRequirement
{
    public GroundworkStorageTopologyRequirement(string identity) =>
        Identity = GroundworkCompositionImmutability.RequiredIdentity(
            identity,
            nameof(identity),
            "A stable topology-requirement identity is required.");

    public string Identity { get; }
}

internal static class GroundworkCompositionImmutability
{
    public static string RequiredIdentity(string value, string parameterName, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message, parameterName) : value;

    public static IReadOnlyList<T> ReadOnlyCopy<T>(IReadOnlyCollection<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Any(value => value is null))
            throw new ArgumentException("Collections cannot contain null entries.", parameterName);
        return Array.AsReadOnly(values.ToArray());
    }

    public static IReadOnlyList<string> ReadOnlyIdentityCopy(
        IReadOnlyCollection<string> values,
        string parameterName,
        string message)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException(message, parameterName);
        return Array.AsReadOnly(values.ToArray());
    }

    public static StorageManifest CopyManifest(StorageManifest manifest, bool orderStorageUnits = false)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifest.Identity);
        ArgumentNullException.ThrowIfNull(manifest.Owner);
        ArgumentNullException.ThrowIfNull(manifest.Version);
        RequiredIdentity(manifest.Identity.Value, nameof(manifest), "A manifest identity is required.");
        RequiredIdentity(manifest.Owner.Value, nameof(manifest), "A manifest owner is required.");
        RequiredIdentity(manifest.Version.Value, nameof(manifest), "A manifest version is required.");
        var units = manifest.StorageUnits
            .Select(CopyStorageUnit)
            .OrderByIf(orderStorageUnits, unit => unit.Identity.Value, StringComparer.Ordinal)
            .ToArray();
        var sharedStorages = manifest.SharedDocumentStorages
            .OrderByIf(orderStorageUnits, storage => storage.Binding.Value, StringComparer.Ordinal)
            .ToArray();

        return manifest with
        {
            StorageUnits = Array.AsReadOnly(units),
            RequiredCapabilities = manifest.RequiredCapabilities.ToFrozenSet(StringComparer.Ordinal),
            CompatibilityNotes = Array.AsReadOnly(manifest.CompatibilityNotes.ToArray()),
            SharedDocumentStorages = Array.AsReadOnly(sharedStorages)
        };
    }

    private static StorageUnit CopyStorageUnit(StorageUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(unit.Identity);
        RequiredIdentity(unit.Identity.Value, nameof(unit), "A storage-unit identity is required.");
        return unit with
        {
            Indexes = Array.AsReadOnly(unit.Indexes.ToArray()),
            Queries = Array.AsReadOnly(unit.Queries.ToArray()),
            PhysicalStorage = unit.PhysicalStorage is null
                ? null
                : new StorageUnitPhysicalStorage(
                    unit.PhysicalStorage.ProvisioningMode,
                    unit.PhysicalStorage.Policy,
                    unit.PhysicalStorage.LogicalIndexes,
                    unit.PhysicalStorage.BoundedQueries,
                    unit.PhysicalStorage.NameOverrides,
                    unit.PhysicalStorage.BoundedMutations)
        };
    }

    private static IEnumerable<T> OrderByIf<T, TKey>(
        this IEnumerable<T> source,
        bool condition,
        Func<T, TKey> keySelector,
        IComparer<TKey> comparer) =>
        condition ? source.OrderBy(keySelector, comparer) : source;
}
