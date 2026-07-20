using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Core.Validation;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// Validates one host-selected set of Groundwork storage declarations before a provider is
/// materialized or a schema target is exposed to deployment tooling.
/// </summary>
public sealed class GroundworkStorageCompositionValidator
{
    public GroundworkStorageCompositionValidationResult Validate(
        GroundworkStorageCompositionValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<GroundworkDiagnostic>();
        ValidateFeatureIdentities(request.Declarations, diagnostics);
        ValidateRequiredStoreOwners(request, diagnostics);
        ValidateStorageUnitOwners(request.Declarations, diagnostics);

        if (request.Declarations.Count == 0)
        {
            diagnostics.Add(Error(
                "ELSA-GW-COMPOSITION-SOURCE-MISSING",
                "The selected Groundwork composition does not contain a manifest source.",
                "composition.sources"));
        }

        if (HasCompositionBlockingErrors(diagnostics))
            return Invalid(diagnostics);

        StorageManifest manifest;
        try
        {
            manifest = StorageManifestComposition.Union(
                request.CompositionIdentity,
                request.CompositionOwner,
                request.CompositionVersion,
                request.Declarations.Select(declaration => declaration.Manifest).ToArray());
        }
        catch (StorageManifestCompositionException exception)
        {
            var owners = OwnersOfStorageUnit(request.Declarations, exception.UnitIdentity);
            diagnostics.Add(Error(
                "ELSA-GW-COMPOSITION-UNIT-COLLISION",
                $"Storage unit '{exception.UnitIdentity}' is declared by multiple selected features: {Join(owners)}.",
                $"composition.storageUnits.{exception.UnitIdentity}"));
            return Invalid(diagnostics);
        }
        catch (SharedStorageDefinitionCompositionException exception)
        {
            var owners = OwnersOfSharedBinding(request.Declarations, exception.Binding);
            diagnostics.Add(Error(
                "ELSA-GW-COMPOSITION-SHARED-STORAGE-INCOMPATIBLE",
                $"Shared-storage binding '{exception.Binding}' has incompatible definitions from selected features: {Join(owners)}.",
                $"composition.sharedStorage.{exception.Binding}"));
            return Invalid(diagnostics);
        }

        var ownersByUnit = request.Declarations
            .SelectMany(declaration => declaration.Manifest.StorageUnits.Select(unit =>
                new KeyValuePair<StorageUnitIdentity, string>(unit.Identity, declaration.FeatureIdentity)))
            .ToFrozenDictionary();
        var nameResolution = new GroundworkPhysicalNameResolver().Resolve(
            manifest,
            request.NamingPolicy,
            request.ProviderNameNormalizer,
            ownersByUnit);
        diagnostics.AddRange(nameResolution.Diagnostics);
        diagnostics.AddRange(nameResolution.Collisions.Select(collision => Error(
            "ELSA-GW-COMPOSITION-NAME-COLLISION",
            $"Provider identifier '{collision.Identifier}' in collision scope '{collision.CollisionScope}' " +
            $"is produced by multiple physical objects: {Join(collision.PhysicalObjects.Select(item =>
                $"{item.FeatureIdentity}/{item.StorageUnit.Value}/{item.ObjectKind}/{item.FeatureDefaultLogicalName}"))}.",
            $"composition.physicalNames.{collision.CollisionScope}.{collision.Identifier}")));
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
            return Invalid(diagnostics);

        var routeCompilation = ExecutableStorageRouteCompiler.Compile(nameResolution.Definitions);
        diagnostics.AddRange(routeCompilation.Diagnostics);
        ValidateExecutableRoutes(manifest, routeCompilation.Routes, diagnostics);
        ValidateRouteCapabilities(request, routeCompilation.Routes, diagnostics);
        ValidateTopology(request, diagnostics);

        if (diagnostics.Any(diagnostic => diagnostic.IsError))
            return Invalid(diagnostics);

        PhysicalSchemaTarget target;
        try
        {
            target = (request.TargetCompiler ?? GroundworkRoutePhysicalSchemaTargetCompiler.Instance).Compile(
                manifest,
                request.ProviderCapabilities.Provider,
                nameResolution.NamePolicy,
                routeCompilation.Routes);
        }
        catch (Exception)
        {
            diagnostics.Add(Error(
                "ELSA-GW-COMPOSITION-TARGET-COMPILE",
                $"Provider '{request.ProviderCapabilities.Provider.Name}' could not compile the selected canonical physical target.",
                "composition.target"));
            return Invalid(diagnostics);
        }

        if (!IsExactValidatedTarget(manifest, request.ProviderCapabilities.Provider, routeCompilation.Routes, target))
        {
            diagnostics.Add(Error(
                "ELSA-GW-COMPOSITION-TARGET-MISMATCH",
                $"Provider '{request.ProviderCapabilities.Provider.Name}' compiled a physical target that does not match the validated manifest and executable routes.",
                "composition.target"));
            return Invalid(diagnostics);
        }
        var requiredCapabilities = request.Declarations
            .SelectMany(declaration => declaration.RequiredRoutes)
            .SelectMany(route => route.RequiredCapabilities)
            .ToFrozenSet();
        var topologyRequirements = request.Declarations
            .SelectMany(declaration => declaration.TopologyRequirements)
            .DistinctBy(requirement => requirement.Identity, StringComparer.Ordinal)
            .ToArray();
        var snapshot = new GroundworkStorageCompositionSnapshot(
            manifest,
            request.Declarations,
            nameResolution.ResolvedNames,
            requiredCapabilities,
            request.ProviderCapabilities.Provider,
            topologyRequirements,
            request.NamingPolicy.PolicyIdentity,
            nameResolution.NamePolicy,
            ComputeCompositionFingerprint(request, target.Fingerprint),
            target);

        return new GroundworkStorageCompositionValidationResult(snapshot, diagnostics);
    }

    private static bool IsExactValidatedTarget(
        StorageManifest manifest,
        ProviderIdentity provider,
        IReadOnlyList<ExecutableStorageRoute> validatedRoutes,
        PhysicalSchemaTarget target) =>
        target.ManifestIdentity == manifest.Identity &&
        target.ManifestVersion == manifest.Version &&
        target.Provider == provider &&
        target.Routes.Select(route => route.Fingerprint)
            .SequenceEqual(validatedRoutes.Select(route => route.Fingerprint), StringComparer.Ordinal);

    private static void ValidateFeatureIdentities(
        IReadOnlyCollection<GroundworkStorageManifestDeclaration> declarations,
        ICollection<GroundworkDiagnostic> diagnostics)
    {
        foreach (var group in declarations
                     .GroupBy(declaration => declaration.FeatureIdentity, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            diagnostics.Add(Error(
                "ELSA-GW-COMPOSITION-FEATURE-DUPLICATE",
                $"Feature identity '{group.Key}' has multiple selected manifest declarations.",
                $"composition.features.{group.Key}"));
        }
    }

    private static void ValidateRequiredStoreOwners(
        GroundworkStorageCompositionValidationRequest request,
        ICollection<GroundworkDiagnostic> diagnostics)
    {
        var requiredStores = request.RequiredStoreContracts.ToFrozenSet();
        var declaredStores = request.Declarations
            .SelectMany(declaration => declaration.RequiredStoreContracts)
            .ToFrozenSet();
        foreach (var storeContract in requiredStores
                     .Concat(declaredStores)
                     .Distinct()
                     .OrderBy(StoreIdentity, StringComparer.Ordinal))
        {
            var owners = request.Declarations
                .Where(declaration => declaration.RequiredStoreContracts.Contains(storeContract))
                .Select(declaration => declaration.FeatureIdentity)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var storeIdentity = StoreIdentity(storeContract);
            switch (owners.Length)
            {
                case 0 when requiredStores.Contains(storeContract):
                    diagnostics.Add(Error(
                        "ELSA-GW-COMPOSITION-STORE-MISSING",
                        $"Required public store contract '{storeIdentity}' has no selected durable feature owner.",
                        $"composition.stores.{storeIdentity}"));
                    break;
                case > 1:
                    diagnostics.Add(Error(
                        "ELSA-GW-COMPOSITION-STORE-DUPLICATE",
                        $"Required public store contract '{storeIdentity}' has multiple selected durable feature owners: {Join(owners)}.",
                        $"composition.stores.{storeIdentity}"));
                    break;
            }
        }
    }

    private static void ValidateStorageUnitOwners(
        IReadOnlyCollection<GroundworkStorageManifestDeclaration> declarations,
        ICollection<GroundworkDiagnostic> diagnostics)
    {
        foreach (var group in declarations
                     .SelectMany(declaration => declaration.Manifest.StorageUnits.Select(unit => new
                     {
                         Unit = unit.Identity.Value,
                         declaration.FeatureIdentity
                     }))
                     .GroupBy(item => item.Unit, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var owners = group.Select(item => item.FeatureIdentity)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            diagnostics.Add(Error(
                "ELSA-GW-COMPOSITION-UNIT-COLLISION",
                $"Storage unit '{group.Key}' is declared by multiple selected features: {Join(owners)}.",
                $"composition.storageUnits.{group.Key}"));
        }
    }

    private static void ValidateExecutableRoutes(
        StorageManifest manifest,
        IReadOnlyCollection<ExecutableStorageRoute> routes,
        ICollection<GroundworkDiagnostic> diagnostics)
    {
        var routedUnits = routes.Select(route => route.StorageUnit).ToFrozenSet();
        foreach (var unit in manifest.StorageUnits
                     .Where(unit => !routedUnits.Contains(unit.Identity))
                     .OrderBy(unit => unit.Identity.Value, StringComparer.Ordinal))
        {
            diagnostics.Add(Error(
                "ELSA-GW-COMPOSITION-ROUTE-MISSING",
                $"Storage unit '{unit.Identity.Value}' has no executable physical route.",
                $"composition.storageUnits.{unit.Identity.Value}.route"));
        }
    }

    private static void ValidateRouteCapabilities(
        GroundworkStorageCompositionValidationRequest request,
        IReadOnlyCollection<ExecutableStorageRoute> routes,
        ICollection<GroundworkDiagnostic> diagnostics)
    {
        var routedUnits = routes.Select(route => route.StorageUnit).ToFrozenSet();
        var compiledQueries = routes
            .SelectMany(route => route.CandidateQueryPaths.SelectMany(path =>
                path.QueryIdentities.Select(queryIdentity =>
                    (route.StorageUnit, QueryIdentity: queryIdentity))))
            .ToFrozenSet();
        foreach (var (declaration, requirement) in request.Declarations
                     .SelectMany(declaration => declaration.RequiredRoutes.Select(requirement =>
                         (Declaration: declaration, Requirement: requirement)))
                     .OrderBy(item => item.Declaration.FeatureIdentity, StringComparer.Ordinal)
                     .ThenBy(item => item.Requirement.StorageUnit.Value, StringComparer.Ordinal)
                     .ThenBy(item => item.Requirement.RouteIdentity, StringComparer.Ordinal))
        {
            var ownsUnit = declaration.Manifest.StorageUnits.Any(unit => unit.Identity == requirement.StorageUnit);
            var requiresCompiledQuery = requirement.RequiredCapabilities.Count == 0;
            if (!ownsUnit ||
                !routedUnits.Contains(requirement.StorageUnit) ||
                requiresCompiledQuery &&
                !compiledQueries.Contains((requirement.StorageUnit, requirement.RouteIdentity)))
            {
                diagnostics.Add(Error(
                    "ELSA-GW-COMPOSITION-ROUTE-MISSING",
                    $"Feature '{declaration.FeatureIdentity}' requires route '{requirement.RouteIdentity}' for storage unit '{requirement.StorageUnit.Value}', but that exact route was not compiled for the feature.",
                    RouteTarget(declaration.FeatureIdentity, requirement)));
                continue;
            }

            var requiredPath = new GroundworkActiveStoragePath(
                declaration.FeatureIdentity,
                requirement.StorageUnit,
                requirement.RouteIdentity,
                requirement.RequiredCapabilities);
            if (requirement.RequiredCapabilities.Count == 0 &&
                !request.ProviderCapabilities.HasActivePath(requiredPath))
            {
                diagnostics.Add(Error(
                    "ELSA-GW-COMPOSITION-ROUTE-INACTIVE",
                    $"Feature '{declaration.FeatureIdentity}' requires active route '{requirement.RouteIdentity}' for storage unit '{requirement.StorageUnit.Value}' on provider '{request.ProviderCapabilities.Provider.Name}', but the exact selected path is not active.",
                    RouteTarget(declaration.FeatureIdentity, requirement)));
            }

            foreach (var capability in requirement.RequiredCapabilities.OrderBy(item => item.Value, StringComparer.Ordinal))
            {
                if (!request.ProviderCapabilities.IsActive(requiredPath, capability))
                {
                    diagnostics.Add(Error(
                        "ELSA-GW-COMPOSITION-CAPABILITY-INACTIVE",
                        $"Feature '{declaration.FeatureIdentity}' requires capability '{capability.Value}' on storage unit '{requirement.StorageUnit.Value}' route '{requirement.RouteIdentity}' for provider '{request.ProviderCapabilities.Provider.Name}', but the exact selected path is not active.",
                        CapabilityTarget(declaration.FeatureIdentity, requirement, capability)));
                    continue;
                }

                if (!request.ProviderCapabilities.IsSupported(capability))
                {
                    diagnostics.Add(Error(
                        "ELSA-GW-COMPOSITION-CAPABILITY-UNSUPPORTED",
                        $"Feature '{declaration.FeatureIdentity}' requires capability '{capability.Value}' on storage unit '{requirement.StorageUnit.Value}' route '{requirement.RouteIdentity}', but provider '{request.ProviderCapabilities.Provider.Name}' does not support it.",
                        CapabilityTarget(declaration.FeatureIdentity, requirement, capability)));
                    continue;
                }

                if (!request.ProviderCapabilities.IsEvidenced(capability))
                {
                    diagnostics.Add(Error(
                        "ELSA-GW-COMPOSITION-CAPABILITY-UNEVIDENCED",
                        $"Feature '{declaration.FeatureIdentity}' requires capability '{capability.Value}' on storage unit '{requirement.StorageUnit.Value}' route '{requirement.RouteIdentity}', but provider '{request.ProviderCapabilities.Provider.Name}' has no passing evidence for it.",
                        CapabilityTarget(declaration.FeatureIdentity, requirement, capability)));
                }
            }
        }
    }

    private static void ValidateTopology(
        GroundworkStorageCompositionValidationRequest request,
        ICollection<GroundworkDiagnostic> diagnostics)
    {
        foreach (var (declaration, requirement) in request.Declarations
                     .SelectMany(declaration => declaration.TopologyRequirements.Select(requirement =>
                         (Declaration: declaration, Requirement: requirement)))
                     .OrderBy(item => item.Declaration.FeatureIdentity, StringComparer.Ordinal)
                     .ThenBy(item => item.Requirement.Identity, StringComparer.Ordinal))
        {
            if (request.ProviderCapabilities.SupportsTopology(requirement))
                continue;

            diagnostics.Add(Error(
                "ELSA-GW-COMPOSITION-TOPOLOGY-UNSUPPORTED",
                $"Feature '{declaration.FeatureIdentity}' requires topology capability '{requirement.Identity}', but observed topology '{request.ProviderCapabilities.Topology.TopologyIdentity}' for provider '{request.ProviderCapabilities.Provider.Name}' does not provide it.",
                $"composition.features.{declaration.FeatureIdentity}.topology.{requirement.Identity}"));
        }
    }

    private static bool HasCompositionBlockingErrors(IEnumerable<GroundworkDiagnostic> diagnostics) =>
        diagnostics.Any(diagnostic => diagnostic.Code is
            "ELSA-GW-COMPOSITION-FEATURE-DUPLICATE" or
            "ELSA-GW-COMPOSITION-UNIT-COLLISION" or
            "ELSA-GW-COMPOSITION-SOURCE-MISSING");

    private static IReadOnlyList<string> OwnersOfStorageUnit(
        IEnumerable<GroundworkStorageManifestDeclaration> declarations,
        string unitIdentity) => declarations
        .Where(declaration => declaration.Manifest.StorageUnits.Any(unit =>
            string.Equals(unit.Identity.Value, unitIdentity, StringComparison.Ordinal)))
        .Select(declaration => declaration.FeatureIdentity)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<string> OwnersOfSharedBinding(
        IEnumerable<GroundworkStorageManifestDeclaration> declarations,
        string binding) => declarations
        .Where(declaration => declaration.Manifest.SharedDocumentStorages.Any(storage =>
            string.Equals(storage.Binding.Value, binding, StringComparison.Ordinal)))
        .Select(declaration => declaration.FeatureIdentity)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string RouteTarget(
        string featureIdentity,
        GroundworkStorageRouteRequirement requirement) =>
        $"composition.features.{featureIdentity}.routes.{requirement.StorageUnit.Value}.{requirement.RouteIdentity}";

    private static string CapabilityTarget(
        string featureIdentity,
        GroundworkStorageRouteRequirement requirement,
        CapabilityId capability) =>
        $"{RouteTarget(featureIdentity, requirement)}.capabilities.{capability.Value}";

    private static string StoreIdentity(Type storeContract) =>
        storeContract.FullName ?? storeContract.Name;

    private static string Join(IEnumerable<string> values) => string.Join(", ", values);

    private static string ComputeCompositionFingerprint(
        GroundworkStorageCompositionValidationRequest request,
        string targetFingerprint)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            FormatVersion = 1,
            TargetFingerprint = targetFingerprint,
            CompositionIdentity = request.CompositionIdentity.Value,
            CompositionOwner = request.CompositionOwner.Value,
            CompositionVersion = request.CompositionVersion.Value,
            NamingPolicyIdentity = request.NamingPolicy.PolicyIdentity,
            Provider = new
            {
                request.ProviderCapabilities.Provider.Name,
                request.ProviderCapabilities.Provider.Version,
                request.ProviderCapabilities.Topology.TopologyIdentity,
                ObservedTopologyCapabilities = request.ProviderCapabilities.Topology.ObservedCapabilities
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                ActivePaths = request.ProviderCapabilities.ActivePaths.Select(path => new
                {
                    path.FeatureIdentity,
                    StorageUnit = path.StorageUnit.Value,
                    path.RouteIdentity,
                    Capabilities = path.RequiredCapabilities
                        .Select(capability => capability.Value)
                        .Order(StringComparer.Ordinal)
                        .ToArray()
                }).ToArray()
            },
            RequiredStoreContracts = request.RequiredStoreContracts
                .Select(StoreIdentity)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Declarations = request.Declarations.Select(declaration => new
            {
                declaration.FeatureIdentity,
                ManifestIdentity = declaration.Manifest.Identity.Value,
                ManifestOwner = declaration.Manifest.Owner.Value,
                ManifestVersion = declaration.Manifest.Version.Value,
                RequiredStoreContracts = declaration.RequiredStoreContracts
                    .Select(StoreIdentity)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                RequiredRoutes = declaration.RequiredRoutes
                    .OrderBy(route => route.StorageUnit.Value, StringComparer.Ordinal)
                    .ThenBy(route => route.RouteIdentity, StringComparer.Ordinal)
                    .Select(route => new
                    {
                        StorageUnit = route.StorageUnit.Value,
                        route.RouteIdentity,
                        Capabilities = route.RequiredCapabilities
                            .Select(capability => capability.Value)
                            .Order(StringComparer.Ordinal)
                            .ToArray()
                    }).ToArray(),
                TopologyRequirements = declaration.TopologyRequirements
                    .Select(requirement => requirement.Identity)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                CoverageRows = declaration.CoverageRows
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                ScopeClassifications = declaration.ScopeClassifications
                    .OrderBy(item => item.Key.Value, StringComparer.Ordinal)
                    .Select(item => new
                    {
                        StorageUnit = item.Key.Value,
                        Classification = item.Value.ToString()
                    }).ToArray()
            }).ToArray()
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static GroundworkDiagnostic Error(string code, string message, string target) =>
        GroundworkDiagnostic.Error(code, message, target);

    private static GroundworkStorageCompositionValidationResult Invalid(
        IReadOnlyCollection<GroundworkDiagnostic> diagnostics) => new(null, diagnostics);
}

/// <summary>Immutable input for one composition-validation pass.</summary>
public sealed class GroundworkStorageCompositionValidationRequest
{
    public GroundworkStorageCompositionValidationRequest(
        StorageManifestIdentity compositionIdentity,
        StorageManifestOwner compositionOwner,
        StorageManifestVersion compositionVersion,
        IReadOnlyCollection<GroundworkStorageManifestDeclaration> declarations,
        IReadOnlyCollection<Type> requiredStoreContracts,
        GroundworkStorageNamingPolicyOptions namingPolicy,
        GroundworkProviderCapabilitySnapshot providerCapabilities,
        IProviderPhysicalNameNormalizer providerNameNormalizer,
        IGroundworkPhysicalSchemaTargetCompiler? targetCompiler = null)
    {
        CompositionIdentity = compositionIdentity ?? throw new ArgumentNullException(nameof(compositionIdentity));
        CompositionOwner = compositionOwner ?? throw new ArgumentNullException(nameof(compositionOwner));
        CompositionVersion = compositionVersion ?? throw new ArgumentNullException(nameof(compositionVersion));
        Declarations = CopyDeclarations(declarations);
        RequiredStoreContracts = CopyStoreContracts(requiredStoreContracts);
        NamingPolicy = namingPolicy ?? throw new ArgumentNullException(nameof(namingPolicy));
        ProviderCapabilities = providerCapabilities ?? throw new ArgumentNullException(nameof(providerCapabilities));
        ProviderNameNormalizer = providerNameNormalizer ?? throw new ArgumentNullException(nameof(providerNameNormalizer));
        TargetCompiler = targetCompiler;
    }

    public StorageManifestIdentity CompositionIdentity { get; }

    public StorageManifestOwner CompositionOwner { get; }

    public StorageManifestVersion CompositionVersion { get; }

    public IReadOnlyList<GroundworkStorageManifestDeclaration> Declarations { get; }

    public IReadOnlyList<Type> RequiredStoreContracts { get; }

    public GroundworkStorageNamingPolicyOptions NamingPolicy { get; }

    public GroundworkProviderCapabilitySnapshot ProviderCapabilities { get; }

    public IProviderPhysicalNameNormalizer ProviderNameNormalizer { get; }

    public IGroundworkPhysicalSchemaTargetCompiler? TargetCompiler { get; }

    private static IReadOnlyList<GroundworkStorageManifestDeclaration> CopyDeclarations(
        IReadOnlyCollection<GroundworkStorageManifestDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        if (declarations.Any(declaration => declaration is null))
            throw new ArgumentException("Manifest declarations cannot contain null entries.", nameof(declarations));
        return Array.AsReadOnly(declarations
            .OrderBy(declaration => declaration.FeatureIdentity, StringComparer.Ordinal)
            .ToArray());
    }

    private static IReadOnlyList<Type> CopyStoreContracts(IReadOnlyCollection<Type> requiredStoreContracts)
    {
        ArgumentNullException.ThrowIfNull(requiredStoreContracts);
        if (requiredStoreContracts.Any(store => store is null))
            throw new ArgumentException("Required store contracts cannot contain null entries.", nameof(requiredStoreContracts));
        return Array.AsReadOnly(requiredStoreContracts
            .Distinct()
            .OrderBy(store => store.FullName ?? store.Name, StringComparer.Ordinal)
            .ToArray());
    }
}

/// <summary>Deterministically ordered validation diagnostics and the admitted snapshot, if valid.</summary>
public sealed class GroundworkStorageCompositionValidationResult
{
    public GroundworkStorageCompositionValidationResult(
        GroundworkStorageCompositionSnapshot? snapshot,
        IReadOnlyCollection<GroundworkDiagnostic> diagnostics)
    {
        Snapshot = snapshot;
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics)))
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Target, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray());
        if (Snapshot is not null && Diagnostics.Any(diagnostic => diagnostic.IsError))
            throw new ArgumentException("An invalid composition cannot expose an admitted snapshot.", nameof(snapshot));
    }

    public GroundworkStorageCompositionSnapshot? Snapshot { get; }

    public IReadOnlyList<GroundworkDiagnostic> Diagnostics { get; }

    public bool IsValid => Snapshot is not null && Diagnostics.All(diagnostic => !diagnostic.IsError);
}
