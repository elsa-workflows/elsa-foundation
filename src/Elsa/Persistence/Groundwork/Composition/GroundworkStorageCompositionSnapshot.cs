using System.Collections.Frozen;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Immutable description of the one validated, host-selected Groundwork target consumed by runtime
/// materialization and deployment schema tooling.
/// </summary>
public sealed class GroundworkStorageCompositionSnapshot
{
    public GroundworkStorageCompositionSnapshot(
        StorageManifest manifest,
        IReadOnlyCollection<GroundworkStorageManifestDeclaration> manifestSources,
        IReadOnlyCollection<GroundworkResolvedPhysicalNameSnapshot> resolvedNames,
        IReadOnlySet<CapabilityId> requiredCapabilities,
        ProviderIdentity provider,
        IReadOnlyCollection<GroundworkStorageTopologyRequirement> topologyRequirements,
        string namingPolicyIdentity,
        IPhysicalNamePolicy physicalNamePolicy,
        string compositionFingerprint,
        PhysicalSchemaTarget physicalTarget)
    {
        Manifest = GroundworkCompositionImmutability.CopyManifest(
            manifest ?? throw new ArgumentNullException(nameof(manifest)),
            orderStorageUnits: true);
        var sourceCopy = GroundworkCompositionImmutability.ReadOnlyCopy(manifestSources, nameof(manifestSources));
        ManifestSources = Array.AsReadOnly(
            sourceCopy
            .OrderBy(source => source.FeatureIdentity, StringComparer.Ordinal)
            .ToArray());

        SelectedFeatures = Array.AsReadOnly(
            ManifestSources.Select(source => source.FeatureIdentity).ToArray());
        if (SelectedFeatures.Distinct(StringComparer.Ordinal).Count() != SelectedFeatures.Count)
            throw new ArgumentException("Selected feature identities must be unique.", nameof(manifestSources));

        StorageUnits = Array.AsReadOnly(Manifest.StorageUnits.ToArray());
        if (StorageUnits.Select(unit => unit.Identity).Distinct().Count() != StorageUnits.Count)
            throw new ArgumentException("Storage-unit identities must be unique.", nameof(manifest));

        var resolvedNameCopy = GroundworkCompositionImmutability.ReadOnlyCopy(resolvedNames, nameof(resolvedNames));
        ResolvedNames = Array.AsReadOnly(
            resolvedNameCopy
            .OrderBy(name => name.FeatureIdentity, StringComparer.Ordinal)
            .ThenBy(name => name.StorageUnit.Value, StringComparer.Ordinal)
            .ThenBy(name => name.ObjectKind)
            .ThenBy(name => name.FeatureDefaultLogicalName, StringComparer.Ordinal)
            .ThenBy(name => name.Identifier, StringComparer.Ordinal)
            .ToArray());

        RequiredCapabilities = (requiredCapabilities ?? throw new ArgumentNullException(nameof(requiredCapabilities)))
            .ToFrozenSet();
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        GroundworkCompositionImmutability.RequiredIdentity(
            Provider.Name,
            nameof(provider),
            "A provider identity is required.");
        GroundworkCompositionImmutability.RequiredIdentity(
            Provider.Version,
            nameof(provider),
            "A provider version is required.");
        var topologyCopy = GroundworkCompositionImmutability.ReadOnlyCopy(
            topologyRequirements,
            nameof(topologyRequirements));
        TopologyRequirements = Array.AsReadOnly(
            topologyCopy
            .OrderBy(requirement => requirement.Identity, StringComparer.Ordinal)
            .ToArray());

        NamingPolicyIdentity = GroundworkCompositionImmutability.RequiredIdentity(
            namingPolicyIdentity,
            nameof(namingPolicyIdentity),
            "A stable naming-policy identity is required.");
        PhysicalNamePolicy = physicalNamePolicy ?? throw new ArgumentNullException(nameof(physicalNamePolicy));
        CompositionFingerprint = GroundworkCompositionImmutability.RequiredIdentity(
            compositionFingerprint,
            nameof(compositionFingerprint),
            "A composition fingerprint is required.");
        PhysicalTarget = physicalTarget ?? throw new ArgumentNullException(nameof(physicalTarget));
        if (PhysicalTarget.ManifestIdentity != Manifest.Identity ||
            PhysicalTarget.ManifestVersion != Manifest.Version ||
            PhysicalTarget.Provider != Provider)
        {
            throw new ArgumentException(
                "The compiled physical target must identify the snapshot manifest version and provider exactly.",
                nameof(physicalTarget));
        }

        var targetUnits = PhysicalTarget.Routes.Select(route => route.StorageUnit).ToHashSet();
        if (!targetUnits.SetEquals(StorageUnits.Select(unit => unit.Identity)))
        {
            throw new ArgumentException(
                "The compiled physical target must contain exactly one route for every snapshot storage unit.",
                nameof(physicalTarget));
        }
    }

    public StorageManifestIdentity CompositionIdentity => Manifest.Identity;

    public StorageManifestVersion CompositionVersion => Manifest.Version;

    public IReadOnlyList<string> SelectedFeatures { get; }

    public IReadOnlyList<GroundworkStorageManifestDeclaration> ManifestSources { get; }

    public StorageManifest Manifest { get; }

    public IReadOnlyList<StorageUnit> StorageUnits { get; }

    public IReadOnlySet<CapabilityId> RequiredCapabilities { get; }

    public ProviderIdentity Provider { get; }

    public IReadOnlyList<GroundworkStorageTopologyRequirement> TopologyRequirements { get; }

    public string NamingPolicyIdentity { get; }

    /// <summary>The exact host logical-name policy shared by runtime and deployment tooling.</summary>
    public IPhysicalNamePolicy PhysicalNamePolicy { get; }

    /// <summary>
    /// Fingerprints the Elsa host selection, contributor manifest versions, naming-policy identity,
    /// durable requirements and the effective Groundwork physical target.
    /// </summary>
    public string CompositionFingerprint { get; }

    public IReadOnlyList<GroundworkResolvedPhysicalNameSnapshot> ResolvedNames { get; }

    /// <summary>The validated Groundwork physical target used for read-only runtime admission.</summary>
    public PhysicalSchemaTarget PhysicalTarget { get; }

    /// <summary>
    /// Gets Groundwork's exact physical target fingerprint. Runtime and Groundwork.Tool compare this
    /// value directly; it intentionally remains distinct from <see cref="CompositionFingerprint"/>.
    /// </summary>
    public string TargetFingerprint => PhysicalTarget.Fingerprint;
}

/// <summary>Immutable evidence for one physical name after host and provider transformation.</summary>
public sealed record GroundworkResolvedPhysicalNameSnapshot
{
    public GroundworkResolvedPhysicalNameSnapshot(
        string featureIdentity,
        StorageUnitIdentity storageUnit,
        PhysicalObjectKind objectKind,
        string featureDefaultLogicalName,
        string logicalName,
        string identifier,
        string collisionScope)
    {
        FeatureIdentity = GroundworkCompositionImmutability.RequiredIdentity(
            featureIdentity,
            nameof(featureIdentity),
            "A stable feature identity is required.");
        StorageUnit = storageUnit ?? throw new ArgumentNullException(nameof(storageUnit));
        GroundworkCompositionImmutability.RequiredIdentity(
            storageUnit.Value,
            nameof(storageUnit),
            "A storage-unit identity is required.");
        ObjectKind = Enum.IsDefined(objectKind)
            ? objectKind
            : throw new ArgumentOutOfRangeException(nameof(objectKind), objectKind, null);
        FeatureDefaultLogicalName = GroundworkCompositionImmutability.RequiredIdentity(
            featureDefaultLogicalName,
            nameof(featureDefaultLogicalName),
            "A feature-default logical name is required.");
        LogicalName = GroundworkCompositionImmutability.RequiredIdentity(
            logicalName,
            nameof(logicalName),
            "A transformed logical name is required.");
        Identifier = GroundworkCompositionImmutability.RequiredIdentity(
            identifier,
            nameof(identifier),
            "A provider identifier is required.");
        CollisionScope = GroundworkCompositionImmutability.RequiredIdentity(
            collisionScope,
            nameof(collisionScope),
            "A provider collision scope is required.");
    }

    public string FeatureIdentity { get; }

    public StorageUnitIdentity StorageUnit { get; }

    public PhysicalObjectKind ObjectKind { get; }

    public string FeatureDefaultLogicalName { get; }

    public string LogicalName { get; }

    public string Identifier { get; }

    public string CollisionScope { get; }
}
