using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Validation;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// Resolves the host's provider-neutral transformation through Groundwork's provider renderer and
/// retains owner-aware evidence for every effective physical name.
/// </summary>
public sealed class GroundworkPhysicalNameResolver
{
    public GroundworkPhysicalNameResolutionResult Resolve(
        StorageManifest manifest,
        GroundworkStorageNamingPolicyOptions namingPolicy,
        IProviderPhysicalNameNormalizer providerNormalizer)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var owners = manifest.StorageUnits.ToDictionary(
            unit => unit.Identity,
            _ => manifest.Identity.Value);
        return Resolve(manifest, namingPolicy, providerNormalizer, owners);
    }

    public GroundworkPhysicalNameResolutionResult Resolve(
        StorageManifest manifest,
        GroundworkStorageNamingPolicyOptions namingPolicy,
        IProviderPhysicalNameNormalizer providerNormalizer,
        IReadOnlyDictionary<StorageUnitIdentity, string> featureOwners)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(namingPolicy);
        ArgumentNullException.ThrowIfNull(providerNormalizer);
        ArgumentNullException.ThrowIfNull(featureOwners);

        var owners = CopyOwners(manifest, featureOwners);
        var hostPolicy = new DelegatePhysicalNamePolicy(context => namingPolicy.Transform(
            new GroundworkStorageNamingContext(
                owners[ResolveOwningStorageUnit(context.StorageUnit, manifest)],
                context.StorageUnit,
                context.ObjectKind,
                context.FeatureDefaultLogicalName)));
        var resolution = PhysicalStorageResolver.Resolve(manifest, hostPolicy, providerNormalizer);
        var entries = CreateEntries(resolution.Definitions, owners);
        var collisions = CreateCollisions(entries);

        return new GroundworkPhysicalNameResolutionResult(
            hostPolicy,
            resolution.Definitions,
            entries.Select(entry => entry.Snapshot).ToArray(),
            collisions,
            resolution.Diagnostics);
    }

    private static IReadOnlyDictionary<StorageUnitIdentity, string> CopyOwners(
        StorageManifest manifest,
        IReadOnlyDictionary<StorageUnitIdentity, string> featureOwners)
    {
        var units = manifest.StorageUnits.Select(unit => unit.Identity).ToHashSet();
        var missing = units.Except(featureOwners.Keys)
            .OrderBy(identity => identity.Value, StringComparer.Ordinal)
            .ToArray();
        var unknown = featureOwners.Keys.Except(units)
            .OrderBy(identity => identity.Value, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length != 0 || unknown.Length != 0)
        {
            throw new ArgumentException(
                "Physical-name ownership must identify every manifest storage unit exactly once.",
                nameof(featureOwners));
        }

        var result = new Dictionary<StorageUnitIdentity, string>();
        foreach (var (storageUnit, featureIdentity) in featureOwners)
        {
            if (string.IsNullOrWhiteSpace(featureIdentity))
            {
                throw new ArgumentException(
                    $"Physical-name owner for storage unit '{storageUnit.Value}' cannot be blank.",
                    nameof(featureOwners));
            }

            result.Add(storageUnit, featureIdentity);
        }

        return result;
    }

    private static StorageUnitIdentity ResolveOwningStorageUnit(
        StorageUnitIdentity namingOwner,
        StorageManifest manifest)
    {
        if (!namingOwner.Value.StartsWith("shared:", StringComparison.Ordinal))
            return namingOwner;

        var binding = namingOwner.Value["shared:".Length..];
        return manifest.StorageUnits
            .Where(unit => UsesSharedBinding(unit, binding))
            .Select(unit => unit.Identity)
            .OrderBy(identity => identity.Value, StringComparer.Ordinal)
            .FirstOrDefault() ?? throw new InvalidOperationException(
                $"Shared physical-name owner '{namingOwner.Value}' has no selected storage-unit owner.");
    }

    private static bool UsesSharedBinding(StorageUnit unit, string binding) =>
        unit.PhysicalStorage?.Policy switch
        {
            PhysicalStoragePolicy.ExplicitPolicy { Definition.SharedStorage: { } shared } =>
                string.Equals(shared.Value, binding, StringComparison.Ordinal),
            PhysicalStoragePolicy.DefaultPolicy { SharedStorage: { } shared } =>
                string.Equals(shared.Value, binding, StringComparison.Ordinal),
            _ => false
        };

    private static IReadOnlyList<ResolvedNameEntry> CreateEntries(
        IReadOnlyCollection<ProviderPhysicalTableDefinition> definitions,
        IReadOnlyDictionary<StorageUnitIdentity, string> owners) => definitions
        .SelectMany(definition => definition.Names.Select(name => new ResolvedNameEntry(
            definition,
            name,
            new GroundworkResolvedPhysicalNameSnapshot(
                owners[definition.Resolved.StorageUnit],
                definition.Resolved.StorageUnit,
                name.ObjectKind,
                name.FeatureDefaultLogicalName,
                name.LogicalName,
                name.Identifier,
                name.CollisionScope))))
        .OrderBy(entry => entry.Snapshot.FeatureIdentity, StringComparer.Ordinal)
        .ThenBy(entry => entry.Snapshot.StorageUnit.Value, StringComparer.Ordinal)
        .ThenBy(entry => entry.Snapshot.ObjectKind)
        .ThenBy(entry => entry.Snapshot.FeatureDefaultLogicalName, StringComparer.Ordinal)
        .ThenBy(entry => entry.Snapshot.Identifier, StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<GroundworkPhysicalNameCollisionSnapshot> CreateCollisions(
        IReadOnlyCollection<ResolvedNameEntry> entries) => entries
        .GroupBy(entry => (entry.Name.CollisionScope, entry.Name.Identifier))
        .Where(group => !IsExactSharedObject(group))
        .Select(group => new GroundworkPhysicalNameCollisionSnapshot(
            group.Key.CollisionScope,
            group.Key.Identifier,
            group.Select(entry => entry.Snapshot).ToArray()))
        .OrderBy(collision => collision.CollisionScope, StringComparer.Ordinal)
        .ThenBy(collision => collision.Identifier, StringComparer.Ordinal)
        .ToArray();

    private static bool IsExactSharedObject(IEnumerable<ResolvedNameEntry> group)
    {
        var entries = group.ToArray();
        if (entries.Length < 2)
            return true;

        var first = entries[0];
        var binding = first.Definition.Resolved.SharedStorageDefinition?.Binding;
        return binding is not null &&
               first.Name.ObjectKind is PhysicalObjectKind.PrimaryStorage or PhysicalObjectKind.EnvelopeField &&
               entries.All(entry =>
                   entry.Definition.Definition.Form == PhysicalStorageForm.SharedDocuments &&
                   entry.Definition.Resolved.SharedStorageDefinition?.Binding == binding &&
                   entry.Name.ObjectKind == first.Name.ObjectKind &&
                   entry.Name.NamingOwner == first.Name.NamingOwner &&
                   entry.Name.FeatureDefaultLogicalName == first.Name.FeatureDefaultLogicalName &&
                   entry.Name.LogicalName == first.Name.LogicalName);
    }

    private sealed record ResolvedNameEntry(
        ProviderPhysicalTableDefinition Definition,
        ProviderPhysicalObjectName Name,
        GroundworkResolvedPhysicalNameSnapshot Snapshot);
}

public sealed class GroundworkPhysicalNameResolutionResult
{
    public GroundworkPhysicalNameResolutionResult(
        IPhysicalNamePolicy namePolicy,
        IReadOnlyCollection<ProviderPhysicalTableDefinition> definitions,
        IReadOnlyCollection<GroundworkResolvedPhysicalNameSnapshot> resolvedNames,
        IReadOnlyCollection<GroundworkPhysicalNameCollisionSnapshot> collisions,
        IReadOnlyCollection<GroundworkDiagnostic> diagnostics)
    {
        NamePolicy = namePolicy ?? throw new ArgumentNullException(nameof(namePolicy));
        Definitions = Array.AsReadOnly((definitions ?? throw new ArgumentNullException(nameof(definitions)))
            .OrderBy(definition => definition.Resolved.StorageUnit.Value, StringComparer.Ordinal)
            .ToArray());
        ResolvedNames = Array.AsReadOnly((resolvedNames ?? throw new ArgumentNullException(nameof(resolvedNames)))
            .OrderBy(name => name.FeatureIdentity, StringComparer.Ordinal)
            .ThenBy(name => name.StorageUnit.Value, StringComparer.Ordinal)
            .ThenBy(name => name.ObjectKind)
            .ThenBy(name => name.FeatureDefaultLogicalName, StringComparer.Ordinal)
            .ThenBy(name => name.Identifier, StringComparer.Ordinal)
            .ToArray());
        Collisions = Array.AsReadOnly((collisions ?? throw new ArgumentNullException(nameof(collisions)))
            .OrderBy(collision => collision.CollisionScope, StringComparer.Ordinal)
            .ThenBy(collision => collision.Identifier, StringComparer.Ordinal)
            .ToArray());
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)))
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Target, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>The exact host logical-name policy used to produce this resolution.</summary>
    public IPhysicalNamePolicy NamePolicy { get; }

    public IReadOnlyList<ProviderPhysicalTableDefinition> Definitions { get; }

    public IReadOnlyList<GroundworkResolvedPhysicalNameSnapshot> ResolvedNames { get; }

    public IReadOnlyList<GroundworkPhysicalNameCollisionSnapshot> Collisions { get; }

    public IReadOnlyList<GroundworkDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.All(diagnostic => !diagnostic.IsError);
}

/// <summary>Owner-aware evidence for one provider identifier collision.</summary>
public sealed class GroundworkPhysicalNameCollisionSnapshot
{
    public GroundworkPhysicalNameCollisionSnapshot(
        string collisionScope,
        string identifier,
        IReadOnlyCollection<GroundworkResolvedPhysicalNameSnapshot> physicalObjects)
    {
        CollisionScope = string.IsNullOrWhiteSpace(collisionScope)
            ? throw new ArgumentException("A collision scope is required.", nameof(collisionScope))
            : collisionScope;
        Identifier = string.IsNullOrWhiteSpace(identifier)
            ? throw new ArgumentException("A provider identifier is required.", nameof(identifier))
            : identifier;
        PhysicalObjects = Array.AsReadOnly((physicalObjects ?? throw new ArgumentNullException(nameof(physicalObjects)))
            .OrderBy(item => item.FeatureIdentity, StringComparer.Ordinal)
            .ThenBy(item => item.StorageUnit.Value, StringComparer.Ordinal)
            .ThenBy(item => item.ObjectKind)
            .ThenBy(item => item.FeatureDefaultLogicalName, StringComparer.Ordinal)
            .ToArray());
    }

    public string CollisionScope { get; }

    public string Identifier { get; }

    public IReadOnlyList<GroundworkResolvedPhysicalNameSnapshot> PhysicalObjects { get; }
}
