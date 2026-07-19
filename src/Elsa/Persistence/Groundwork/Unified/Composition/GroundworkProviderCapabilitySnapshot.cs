using System.Collections.Frozen;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// Immutable provider admission evidence. A capability is available only where package support,
/// passing evidence and one exact selected feature/unit/route path intersect.
/// </summary>
public sealed class GroundworkProviderCapabilitySnapshot
{
    private readonly IReadOnlyDictionary<ActivePathIdentity, GroundworkActiveStoragePath> activePathsByIdentity;

    public GroundworkProviderCapabilitySnapshot(
        ProviderCapabilityReport capabilityReport,
        GroundworkProviderTopologySnapshot topology,
        IReadOnlyCollection<GroundworkActiveStoragePath> activePaths)
    {
        ArgumentNullException.ThrowIfNull(capabilityReport);
        ArgumentNullException.ThrowIfNull(capabilityReport.Provider);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(activePaths);
        if (!string.Equals(capabilityReport.Provider.Name, topology.ProviderIdentity, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Observed topology provider '{topology.ProviderIdentity}' does not match capability provider '{capabilityReport.Provider.Name}'.",
                nameof(topology));
        }

        CapabilityReport = Copy(capabilityReport);
        Topology = topology;
        var pathCopy = activePaths.ToArray();
        if (pathCopy.Any(path => path is null))
            throw new ArgumentException("Active storage paths cannot contain null entries.", nameof(activePaths));

        var duplicate = pathCopy
            .GroupBy(ActivePathIdentity.From)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Active storage path '{duplicate.Key}' is declared more than once.",
                nameof(activePaths));
        }

        ActivePaths = Array.AsReadOnly(pathCopy
            .OrderBy(path => path.FeatureIdentity, StringComparer.Ordinal)
            .ThenBy(path => path.StorageUnit.Value, StringComparer.Ordinal)
            .ThenBy(path => path.RouteIdentity, StringComparer.Ordinal)
            .ToArray());
        activePathsByIdentity = ActivePaths.ToFrozenDictionary(ActivePathIdentity.From);
        AvailableCapabilities = ActivePaths
            .SelectMany(path => path.RequiredCapabilities)
            .Where(capability =>
                CapabilityReport.SupportedCapabilities.Contains(capability) &&
                CapabilityReport.EvidencedCapabilities.Contains(capability))
            .ToFrozenSet();
    }

    public static GroundworkProviderCapabilitySnapshot ForFeatureRoutes(
        ProviderCapabilityReport capabilityReport,
        GroundworkProviderTopologySnapshot topology,
        string featureIdentity,
        IReadOnlyCollection<GroundworkStorageRouteRequirement> routes) =>
        new(
            capabilityReport,
            topology,
            routes.Select(route => new GroundworkActiveStoragePath(
                featureIdentity,
                route.StorageUnit,
                route.RouteIdentity,
                route.RequiredCapabilities)).ToArray());

    public ProviderCapabilityReport CapabilityReport { get; }

    public ProviderIdentity Provider => CapabilityReport.Provider;

    public GroundworkProviderTopologySnapshot Topology { get; }

    public IReadOnlyList<GroundworkActiveStoragePath> ActivePaths { get; }

    public IReadOnlySet<CapabilityId> AvailableCapabilities { get; }

    public bool IsCapabilityAvailable(GroundworkActiveStoragePath path, CapabilityId capability)
    {
        ArgumentNullException.ThrowIfNull(path);
        return IsSupported(capability) &&
               IsEvidenced(capability) &&
               path.RequiredCapabilities.Contains(capability) &&
               IsActive(path, capability);
    }

    public bool IsSupported(CapabilityId capability) =>
        CapabilityReport.SupportedCapabilities.Contains(capability);

    public bool IsEvidenced(CapabilityId capability) =>
        CapabilityReport.EvidencedCapabilities.Contains(capability);

    public bool HasActivePath(GroundworkActiveStoragePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return activePathsByIdentity.ContainsKey(ActivePathIdentity.From(path));
    }

    public bool IsActive(GroundworkActiveStoragePath path, CapabilityId capability)
    {
        ArgumentNullException.ThrowIfNull(path);
        return activePathsByIdentity.TryGetValue(ActivePathIdentity.From(path), out var active) &&
               active.RequiredCapabilities.Contains(capability);
    }

    public bool SupportsTopology(GroundworkStorageTopologyRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return Topology.ObservedCapabilities.Contains(requirement.Identity);
    }

    private static ProviderCapabilityReport Copy(ProviderCapabilityReport report) => new(
        new ProviderIdentity(report.Provider.Name, report.Provider.Version),
        report.SupportedCapabilities.ToFrozenSet(),
        report.EvidencedCapabilities.ToFrozenSet(),
        new IndexCapabilities(
            report.Indexes.SupportedValueKinds.ToFrozenSet(),
            report.Indexes.SupportsUniqueIndexes,
            report.Indexes.SupportsSortableIndexes,
            report.Indexes.SupportedMissingValueBehaviors.ToFrozenSet()),
        report.SupportedQueryOperations.ToFrozenSet(),
        report.SupportedConcurrencyModes.ToFrozenSet(),
        Array.AsReadOnly(report.Warnings.ToArray()));

    private readonly record struct ActivePathIdentity(
        string FeatureIdentity,
        StorageUnitIdentity StorageUnit,
        string RouteIdentity)
    {
        public static ActivePathIdentity From(GroundworkActiveStoragePath path) =>
            new(path.FeatureIdentity, path.StorageUnit, path.RouteIdentity);

        public override string ToString() => $"{FeatureIdentity}/{StorageUnit.Value}/{RouteIdentity}";
    }
}

/// <summary>One selected Elsa execution path and the capabilities it actually exercises.</summary>
public sealed class GroundworkActiveStoragePath
{
    public GroundworkActiveStoragePath(
        string featureIdentity,
        StorageUnitIdentity storageUnit,
        string routeIdentity,
        IReadOnlySet<CapabilityId> requiredCapabilities)
    {
        FeatureIdentity = string.IsNullOrWhiteSpace(featureIdentity)
            ? throw new ArgumentException("A stable feature identity is required.", nameof(featureIdentity))
            : featureIdentity;
        StorageUnit = storageUnit ?? throw new ArgumentNullException(nameof(storageUnit));
        if (string.IsNullOrWhiteSpace(storageUnit.Value))
            throw new ArgumentException("A storage-unit identity is required.", nameof(storageUnit));
        RouteIdentity = string.IsNullOrWhiteSpace(routeIdentity)
            ? throw new ArgumentException("A stable route identity is required.", nameof(routeIdentity))
            : routeIdentity;
        RequiredCapabilities = (requiredCapabilities ?? throw new ArgumentNullException(nameof(requiredCapabilities)))
            .ToFrozenSet();
    }

    public string FeatureIdentity { get; }

    public StorageUnitIdentity StorageUnit { get; }

    public string RouteIdentity { get; }

    public IReadOnlySet<CapabilityId> RequiredCapabilities { get; }
}

/// <summary>
/// Sanitized topology facts observed from the selected provider target during admission.
/// Configuration intent alone must never populate <see cref="ObservedCapabilities"/>.
/// </summary>
public sealed class GroundworkProviderTopologySnapshot
{
    public GroundworkProviderTopologySnapshot(
        string providerIdentity,
        string topologyIdentity,
        IReadOnlySet<string> observedCapabilities)
    {
        ProviderIdentity = string.IsNullOrWhiteSpace(providerIdentity)
            ? throw new ArgumentException("A provider identity is required.", nameof(providerIdentity))
            : providerIdentity;
        TopologyIdentity = string.IsNullOrWhiteSpace(topologyIdentity)
            ? throw new ArgumentException("An observed topology identity is required.", nameof(topologyIdentity))
            : topologyIdentity;
        ArgumentNullException.ThrowIfNull(observedCapabilities);
        if (observedCapabilities.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Observed topology capabilities cannot be blank.", nameof(observedCapabilities));
        ObservedCapabilities = observedCapabilities.ToFrozenSet(StringComparer.Ordinal);
    }

    public string ProviderIdentity { get; }

    public string TopologyIdentity { get; }

    public IReadOnlySet<string> ObservedCapabilities { get; }
}
