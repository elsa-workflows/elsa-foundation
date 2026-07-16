using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// Defines one host's deployable Groundwork schema. Concrete application composition assemblies
/// remain public and parameterless so Groundwork.Tool can activate the exact source in a separate
/// process; runtime registration consumes the same source types and naming policy.
/// </summary>
public abstract class GroundworkDeploymentSchemaManifestSource : IPhysicalSchemaManifestSource
{
    private readonly Lazy<DeploymentDefinition> definition;

    protected GroundworkDeploymentSchemaManifestSource() =>
        definition = new Lazy<DeploymentDefinition>(CreateDefinition, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The selected feature-owned manifest-source implementation types.</summary>
    protected abstract IReadOnlyCollection<Type> ManifestSourceTypes { get; }

    /// <summary>
    /// Creates the provider-neutral host naming policy. Override this in the host source whenever
    /// runtime names are transformed; the stable policy identity must change with its behavior.
    /// </summary>
    protected virtual GroundworkStorageNamingPolicyOptions CreateStorageNamingPolicy() =>
        GroundworkStorageNamingPolicyOptions.Identity;

    public StorageManifest CreateManifest() => definition.Value.Manifest;

    public IPhysicalNamePolicy CreateNamePolicy() => definition.Value.NamePolicy;

    internal IReadOnlyList<Type> GetManifestSourceTypes() => definition.Value.ManifestSourceTypes;

    internal GroundworkStorageNamingPolicyOptions GetStorageNamingPolicy() => definition.Value.NamingPolicy;

    internal IReadOnlyList<GroundworkStorageManifestDeclaration> GetDeclarations() => definition.Value.Declarations;

    private DeploymentDefinition CreateDefinition()
    {
        var sourceTypes = ValidateSourceTypes(ManifestSourceTypes);
        var sources = sourceTypes.Select(CreateSource).ToArray();
        var declarations = sources
            .OrderBy(source => source.FeatureIdentity, StringComparer.Ordinal)
            .Select(source => source.CreateDeclarationAsync().AsTask().GetAwaiter().GetResult())
            .ToArray();
        var duplicateFeature = declarations
            .GroupBy(declaration => declaration.FeatureIdentity, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateFeature is not null)
        {
            throw new InvalidOperationException(
                $"Deployment schema feature identity '{duplicateFeature.Key}' is selected more than once.");
        }

        var manifest = StorageManifestComposition.Union(
            GroundworkStorageCompositionDescriptor.Identity,
            GroundworkStorageCompositionDescriptor.Owner,
            GroundworkStorageCompositionDescriptor.Version,
            declarations.Select(declaration => declaration.Manifest).ToArray());
        var owners = declarations
            .SelectMany(declaration => declaration.Manifest.StorageUnits.Select(unit =>
                new KeyValuePair<StorageUnitIdentity, string>(unit.Identity, declaration.FeatureIdentity)))
            .ToDictionary();
        var namingPolicy = CreateStorageNamingPolicy()
            ?? throw new InvalidOperationException("The deployment schema returned a null naming policy.");
        var namePolicy = new GroundworkPhysicalNameResolver()
            .CreateHostNamePolicy(manifest, namingPolicy, owners);
        return new DeploymentDefinition(
            sourceTypes,
            Array.AsReadOnly(declarations),
            manifest,
            namingPolicy,
            namePolicy);
    }

    private static IReadOnlyList<Type> ValidateSourceTypes(IReadOnlyCollection<Type> sourceTypes)
    {
        ArgumentNullException.ThrowIfNull(sourceTypes);
        if (sourceTypes.Count == 0)
            throw new InvalidOperationException("A deployment schema must select at least one manifest source.");

        var result = sourceTypes
            .Select(type => type ?? throw new InvalidOperationException("Manifest source types cannot contain null entries."))
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        if (result.Length != sourceTypes.Count)
            throw new InvalidOperationException("A deployment schema cannot select the same manifest source type more than once.");

        foreach (var type in result)
        {
            if (type.IsAbstract || type.IsInterface || !typeof(IGroundworkStorageManifestSource).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"Manifest source type '{type.FullName}' must be a concrete {nameof(IGroundworkStorageManifestSource)} implementation.");
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidOperationException(
                    $"Manifest source type '{type.FullName}' must have a public parameterless constructor.");
            }
        }

        return Array.AsReadOnly(result);
    }

    private static IGroundworkStorageManifestSource CreateSource(Type sourceType) =>
        (IGroundworkStorageManifestSource)(Activator.CreateInstance(sourceType)
            ?? throw new InvalidOperationException($"Manifest source type '{sourceType.FullName}' returned null."));

    private sealed record DeploymentDefinition(
        IReadOnlyList<Type> ManifestSourceTypes,
        IReadOnlyList<GroundworkStorageManifestDeclaration> Declarations,
        StorageManifest Manifest,
        GroundworkStorageNamingPolicyOptions NamingPolicy,
        IPhysicalNamePolicy NamePolicy);
}

/// <summary>Immutable runtime guard for one explicitly selected deployment schema source.</summary>
internal sealed class GroundworkDeploymentSchemaSelection
{
    public GroundworkDeploymentSchemaSelection(
        Type sourceType,
        IReadOnlyCollection<GroundworkStorageManifestDeclaration> declarations,
        GroundworkStorageNamingPolicyOptions namingPolicy)
    {
        SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
        ArgumentNullException.ThrowIfNull(declarations);
        Declarations = Array.AsReadOnly(declarations
            .OrderBy(declaration => declaration.FeatureIdentity, StringComparer.Ordinal)
            .ToArray());
        NamingPolicy = namingPolicy ?? throw new ArgumentNullException(nameof(namingPolicy));
    }

    public Type SourceType { get; }

    private IReadOnlyList<GroundworkStorageManifestDeclaration> Declarations { get; }

    private GroundworkStorageNamingPolicyOptions NamingPolicy { get; }

    internal void EnsureExactNamingPolicy(GroundworkStorageNamingPolicyOptions namingPolicy)
    {
        if (ReferenceEquals(NamingPolicy, namingPolicy))
            return;

        throw new GroundworkStorageCompositionException(
            $"Runtime Groundwork naming policy does not match deployment schema source '{SourceType.FullName}'. " +
            "Configure host naming through that deployment source only.");
    }

    internal void EnsureExactDeclarations(IReadOnlyCollection<GroundworkStorageManifestDeclaration> declarations)
    {
        var actual = declarations.OrderBy(declaration => declaration.FeatureIdentity, StringComparer.Ordinal).ToArray();
        if (Declarations.Count == actual.Length && Declarations.All(expected =>
                actual.Count(candidate => candidate.FeatureIdentity == expected.FeatureIdentity) == 1 &&
                HasSameDefinition(
                    expected,
                    actual.Single(candidate => candidate.FeatureIdentity == expected.FeatureIdentity))))
            return;

        throw new GroundworkStorageCompositionException(
            $"Runtime Groundwork declarations do not match deployment schema source '{SourceType.FullName}'. " +
            $"Expected [{string.Join(", ", Declarations.Select(item => item.FeatureIdentity))}]; " +
            $"actual [{string.Join(", ", actual.Select(item => item.FeatureIdentity))}].");
    }

    private static bool HasSameDefinition(
        GroundworkStorageManifestDeclaration expected,
        GroundworkStorageManifestDeclaration actual) =>
        expected.FeatureIdentity == actual.FeatureIdentity &&
        expected.Manifest.HasSameDefinitionAs(actual.Manifest) &&
        expected.RequiredStoreContracts.ToHashSet().SetEquals(actual.RequiredStoreContracts) &&
        RoutesEqual(expected.RequiredRoutes, actual.RequiredRoutes) &&
        expected.TopologyRequirements.Select(item => item.Identity).ToHashSet(StringComparer.Ordinal)
            .SetEquals(actual.TopologyRequirements.Select(item => item.Identity)) &&
        expected.CoverageRows.ToHashSet(StringComparer.Ordinal).SetEquals(actual.CoverageRows) &&
        expected.ScopeClassifications.Count == actual.ScopeClassifications.Count &&
        expected.ScopeClassifications.All(item =>
            actual.ScopeClassifications.TryGetValue(item.Key, out var classification) &&
            classification == item.Value);

    private static bool RoutesEqual(
        IReadOnlyCollection<GroundworkStorageRouteRequirement> expected,
        IReadOnlyCollection<GroundworkStorageRouteRequirement> actual) =>
        expected.Count == actual.Count && expected.All(route => actual.Count(candidate =>
            candidate.StorageUnit == route.StorageUnit &&
            candidate.RouteIdentity == route.RouteIdentity &&
            candidate.RequiredCapabilities.SetEquals(route.RequiredCapabilities)) == 1);
}
