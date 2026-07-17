using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Physicalization;
using Groundwork.Core.Queries;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Composition.Tests;

/// <summary>
/// Direct red tests for Spec 094 T019. These tests intentionally name the T022-T026 API before its
/// implementation. They define one acyclic, host-selected composition rather than preserving the
/// current hard-coded Runtime/Design/Activities/Publishing union.
/// </summary>
public class GroundworkStorageCompositionTests
{
    private static readonly CapabilityId AtomicCommit = WellKnownCapabilities.AtomicCommit;

    [Fact]
    public void Declaration_rejects_blank_feature_identity()
    {
        var exception = Assert.Throws<ArgumentException>(() => new GroundworkStorageManifestDeclaration(
            " ",
            CreateManifest("feature", "unit"),
            [],
            [],
            [],
            []));

        Assert.Contains("feature", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Declaration_defensively_copies_every_collection()
    {
        var stores = new List<Type> { typeof(ITestStore) };
        var routes = new List<GroundworkStorageRouteRequirement>
        {
            new(new StorageUnitIdentity("unit"), "route", new HashSet<CapabilityId> { AtomicCommit })
        };
        var topology = new List<GroundworkStorageTopologyRequirement>
        {
            new("multi-document-transactions")
        };
        var coverageRows = new List<string> { "runtime-checkpoint-commit" };
        var declaration = new GroundworkStorageManifestDeclaration(
            "feature",
            CreateManifest("feature", "unit"),
            stores,
            routes,
            topology,
            coverageRows);

        stores.Clear();
        routes.Clear();
        topology.Clear();
        coverageRows.Clear();

        Assert.Equal(typeof(ITestStore), Assert.Single(declaration.RequiredStoreContracts));
        Assert.Equal("route", Assert.Single(declaration.RequiredRoutes).RouteIdentity);
        Assert.Equal("multi-document-transactions", Assert.Single(declaration.TopologyRequirements).Identity);
        Assert.Equal("runtime-checkpoint-commit", Assert.Single(declaration.CoverageRows));
    }

    [Fact]
    public void Naming_policy_rejects_blank_identity()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new GroundworkStorageNamingPolicyOptions(" ", context => context.FeatureDefaultLogicalName));

        Assert.Contains("identity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Naming_pipeline_applies_host_transformation_before_provider_normalization()
    {
        var calls = new List<string>();
        var naming = new GroundworkStorageNamingPolicyOptions("tenant-prefix-v1", context =>
        {
            calls.Add($"host:{context.ObjectKind}:{context.FeatureDefaultLogicalName}");
            return $"tenant_{context.FeatureDefaultLogicalName}";
        });
        var provider = new DelegateProviderPhysicalNameNormalizer(context =>
        {
            calls.Add($"provider:{context.ObjectKind}:{context.LogicalName}");
            return context.LogicalName.ToUpperInvariant();
        });

        var result = new GroundworkPhysicalNameResolver().Resolve(
            CreateManifest("feature", "unit", "orders"),
            naming,
            provider);

        Assert.True(result.IsValid, string.Join("; ", result.Diagnostics.Select(x => x.Message)));
        var primary = Assert.Single(result.ResolvedNames.Where(x => x.ObjectKind == PhysicalObjectKind.PrimaryStorage));
        Assert.Equal("orders", primary.FeatureDefaultLogicalName);
        Assert.Equal("tenant_orders", primary.LogicalName);
        Assert.Equal("TENANT_ORDERS", primary.Identifier);
        var hostCall = calls.IndexOf("host:PrimaryStorage:orders");
        var providerCall = calls.IndexOf("provider:PrimaryStorage:tenant_orders");
        Assert.True(hostCall >= 0, string.Join(", ", calls));
        Assert.True(providerCall > hostCall, string.Join(", ", calls));
    }

    [Fact]
    public void Context_rejects_duplicate_feature_identity_and_names_both_sources()
    {
        var context = new GroundworkStorageCompositionContext();
        context.Add(CreateDeclaration("feature", "first"));

        var exception = Assert.Throws<GroundworkStorageCompositionException>(() =>
            context.Add(CreateDeclaration("feature", "second")));

        Assert.Contains("feature", exception.Message, StringComparison.Ordinal);
        Assert.Contains("first", exception.Message, StringComparison.Ordinal);
        Assert.Contains("second", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Context_rejects_mutation_after_freeze()
    {
        var context = new GroundworkStorageCompositionContext();
        context.Add(CreateDeclaration("feature", "unit"));
        context.Freeze();

        Assert.True(context.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => context.Add(CreateDeclaration("second", "unit-2")));
    }

    [Fact]
    public void Snapshot_is_deterministically_ordered_and_immutable()
    {
        var result = Validate(
        [
            CreateDeclaration("z-feature", "z-unit"),
            CreateDeclaration("a-feature", "a-unit")
        ]);

        Assert.True(result.IsValid, JoinDiagnostics(result));
        var snapshot = Assert.IsType<GroundworkStorageCompositionSnapshot>(result.Snapshot);
        Assert.Equal(["a-feature", "z-feature"], snapshot.SelectedFeatures);
        Assert.Equal(["a-unit", "z-unit"], snapshot.StorageUnits.Select(x => x.Identity.Value));
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<string>)snapshot.SelectedFeatures).Add("mutation"));
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<GroundworkResolvedPhysicalNameSnapshot>)snapshot.ResolvedNames).Clear());
    }

    [Fact]
    public async Task Handler_invokes_sources_once_in_feature_identity_order()
    {
        var calls = new List<string>();
        IGroundworkStorageManifestSource[] sources =
        [
            new RecordingSource("z-feature", calls),
            new RecordingSource("a-feature", calls)
        ];
        var context = new GroundworkStorageCompositionContext();

        await new GroundworkStorageCompositionHandler(sources)
            .Handle(new OnGroundworkStorageComposing(context), CancellationToken.None);

        Assert.Equal(["a-feature", "z-feature"], calls);
        Assert.Equal(["a-feature", "z-feature"], context.Declarations.Select(x => x.FeatureIdentity));
    }

    [Fact]
    public async Task Handler_propagates_source_error_without_freezing_partial_context()
    {
        var context = new GroundworkStorageCompositionContext();
        var expected = new InvalidOperationException("source-failed");
        IGroundworkStorageManifestSource[] sources =
        [
            new RecordingSource("a-feature", []),
            new ThrowingSource("b-feature", expected)
        ];

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GroundworkStorageCompositionHandler(sources)
                .Handle(new OnGroundworkStorageComposing(context), CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.False(context.IsFrozen);
    }

    [Fact]
    public async Task Handler_observes_cancellation_before_invoking_sources()
    {
        var calls = new List<string>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GroundworkStorageCompositionHandler([new RecordingSource("feature", calls)])
                .Handle(new OnGroundworkStorageComposing(new GroundworkStorageCompositionContext()), cancellation.Token));

        Assert.Empty(calls);
    }

    [Fact]
    public void Validator_rejects_required_store_with_no_owner()
    {
        var result = Validate([CreateDeclaration("feature", "unit")], [typeof(ITestStore)]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, x =>
            x.Code == "ELSA-GW-COMPOSITION-STORE-MISSING" &&
            x.Message.Contains(nameof(ITestStore), StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_rejects_required_store_with_multiple_owners()
    {
        var first = CreateDeclaration("first", "first-unit", [typeof(ITestStore)]);
        var second = CreateDeclaration("second", "second-unit", [typeof(ITestStore)]);

        var result = Validate([second, first], [typeof(ITestStore)]);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics.Where(x => x.Code == "ELSA-GW-COMPOSITION-STORE-DUPLICATE"));
        Assert.Contains("first", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("second", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_reports_both_feature_owners_for_storage_unit_collision()
    {
        var result = Validate(
        [
            CreateDeclaration("first-feature", "shared-unit"),
            CreateDeclaration("second-feature", "shared-unit")
        ]);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics.Where(x => x.Code == "ELSA-GW-COMPOSITION-UNIT-COLLISION"));
        Assert.Contains("first-feature", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("second-feature", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("shared-unit", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_rejects_a_manifest_without_an_executable_physical_route()
    {
        var legacyManifest = CreateManifest("feature", "unit") with
        {
            StorageUnits = CreateManifest("feature", "unit").StorageUnits
                .Select(x => x with { PhysicalStorage = null })
                .ToArray()
        };
        var declaration = CreateDeclaration("feature", legacyManifest);

        var result = Validate([declaration]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, x =>
            x.Code == "GW-PHYSICAL-001" || x.Code == "ELSA-GW-COMPOSITION-ROUTE-MISSING");
    }

    [Fact]
    public void Validator_rejects_required_capability_without_an_active_path()
    {
        var declaration = CreateDeclaration(
            "feature",
            "unit",
            requiredRoutes:
            [
                new GroundworkStorageRouteRequirement(
                    new StorageUnitIdentity("unit"),
                    "atomic-route",
                    new HashSet<CapabilityId> { AtomicCommit })
            ]);
        var capabilities = ProviderSnapshot(activePaths: []);

        var result = Validate([declaration], providerCapabilities: capabilities);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics.Where(x => x.Code == "ELSA-GW-COMPOSITION-CAPABILITY-INACTIVE"));
        Assert.Contains("feature", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("unit", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("atomic-route", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(AtomicCommit.Value, diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("groundwork-sqlite", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_rejects_supported_but_unevidenced_capability()
    {
        var declaration = AtomicDeclaration();
        var capabilities = ProviderSnapshot(
            activePaths: [AtomicPath()],
            supported: new HashSet<CapabilityId> { AtomicCommit },
            evidenced: new HashSet<CapabilityId>());

        var result = Validate([declaration], providerCapabilities: capabilities);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, x => x.Code == "ELSA-GW-COMPOSITION-CAPABILITY-UNEVIDENCED");
    }

    [Fact]
    public void Validator_rejects_unsatisfied_topology_prerequisite()
    {
        var declaration = CreateDeclaration(
            "feature",
            "unit",
            topology: [new GroundworkStorageTopologyRequirement("multi-document-transactions")]);
        var capabilities = ProviderSnapshot(
            topology: new GroundworkProviderTopologySnapshot(
                "groundwork-sqlite",
                "file-backed-distinct-connections",
                new HashSet<string>(["persistent-storage"], StringComparer.Ordinal)));

        var result = Validate([declaration], providerCapabilities: capabilities);

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics.Where(x => x.Code == "ELSA-GW-COMPOSITION-TOPOLOGY-UNSUPPORTED"));
        Assert.Contains("feature", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("multi-document-transactions", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("file-backed-distinct-connections", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Target_fingerprint_is_stable_across_declaration_order()
    {
        var first = CreateDeclaration("first", "first-unit");
        var second = CreateDeclaration("second", "second-unit");

        var left = Validate([first, second]);
        var right = Validate([second, first]);

        Assert.True(left.IsValid, JoinDiagnostics(left));
        Assert.True(right.IsValid, JoinDiagnostics(right));
        Assert.Equal(left.Snapshot!.TargetFingerprint, right.Snapshot!.TargetFingerprint);
    }

    [Fact]
    public void Composition_and_target_fingerprints_keep_their_distinct_authority()
    {
        var declaration = CreateDeclaration("feature", "unit");
        var baseline = Validate([declaration]);
        var changedManifest = Validate([CreateDeclaration("feature", CreateManifest("feature", "unit", schemaVersion: "2.0.0"))]);
        var changedProvider = Validate(
            [declaration],
            providerCapabilities: ProviderSnapshot(provider: new ProviderIdentity("groundwork-sqlite", "2.0.0")));
        var changedPolicy = Validate(
            [declaration],
            naming: new GroundworkStorageNamingPolicyOptions("prefix-v2", context => $"v2_{context.FeatureDefaultLogicalName}"));
        var changedPolicyIdentityOnly = Validate(
            [declaration],
            naming: new GroundworkStorageNamingPolicyOptions("identity-v2", context => context.FeatureDefaultLogicalName));

        Assert.True(baseline.IsValid, JoinDiagnostics(baseline));
        Assert.True(changedManifest.IsValid, JoinDiagnostics(changedManifest));
        Assert.True(changedProvider.IsValid, JoinDiagnostics(changedProvider));
        Assert.True(changedPolicy.IsValid, JoinDiagnostics(changedPolicy));
        Assert.True(changedPolicyIdentityOnly.IsValid, JoinDiagnostics(changedPolicyIdentityOnly));
        Assert.NotEqual(baseline.Snapshot!.CompositionFingerprint, changedManifest.Snapshot!.CompositionFingerprint);
        Assert.NotEqual(baseline.Snapshot.CompositionFingerprint, changedProvider.Snapshot!.CompositionFingerprint);
        Assert.NotEqual(baseline.Snapshot.CompositionFingerprint, changedPolicy.Snapshot!.CompositionFingerprint);
        Assert.NotEqual(baseline.Snapshot.CompositionFingerprint, changedPolicyIdentityOnly.Snapshot!.CompositionFingerprint);
        Assert.Equal(baseline.Snapshot.TargetFingerprint, changedManifest.Snapshot.TargetFingerprint);
        Assert.Equal(baseline.Snapshot.TargetFingerprint, changedPolicyIdentityOnly.Snapshot.TargetFingerprint);
        Assert.NotEqual(baseline.Snapshot.TargetFingerprint, changedProvider.Snapshot!.TargetFingerprint);
        Assert.NotEqual(baseline.Snapshot.TargetFingerprint, changedPolicy.Snapshot!.TargetFingerprint);
    }

    [Theory]
    [InlineData("runtime", "elsa-workflows-runtime")]
    [InlineData("iam", "elsa-identity")]
    [InlineData("secrets", "elsa-secrets")]
    [InlineData("distributed-runtime", "elsa-workflows-runtime-distributed")]
    [InlineData("workflows-design", "elsa-workflows-design")]
    [InlineData("activities-design", "elsa-activities-design")]
    [InlineData("publishing", "elsa-workflows-publishing")]
    public async Task Family_source_declares_exact_manifest_and_public_store_contracts(
        string family,
        string expectedFeatureIdentity)
    {
        var source = CreateFamilySource(family);
        var declaration = await source.CreateDeclarationAsync(CancellationToken.None);

        Assert.Equal(expectedFeatureIdentity, source.FeatureIdentity);
        Assert.Equal(expectedFeatureIdentity, declaration.FeatureIdentity);
        Assert.Equal(expectedFeatureIdentity, declaration.Manifest.Identity.Value);
        Assert.Equal(
            ExpectedStoreContracts(family).OrderBy(x => x.FullName, StringComparer.Ordinal),
            declaration.RequiredStoreContracts.OrderBy(x => x.FullName, StringComparer.Ordinal));
        Assert.All(declaration.Manifest.StorageUnits, unit =>
            Assert.Contains(unit.Identity, declaration.ScopeClassifications.Keys));

        if (family == "runtime")
        {
            var route = Assert.Single(declaration.RequiredRoutes);
            Assert.Equal(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, route.StorageUnit.Value);
            Assert.Equal(RuntimeGroundworkStorageManifestSource.CheckpointCommitRouteIdentity, route.RouteIdentity);
            Assert.Equal([WellKnownCapabilities.AtomicCommit], route.RequiredCapabilities);
            Assert.Equal(
                RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity,
                Assert.Single(declaration.TopologyRequirements).Identity);
        }
    }

    [Fact]
    public async Task Legacy_physicalization_bounds_projected_string_index_keys()
    {
        var declaration = await new RuntimeGroundworkStorageManifestSource()
            .CreateDeclarationAsync(CancellationToken.None);
        var projectedStrings = declaration.Manifest.StorageUnits
            .Select(unit => Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage!.Policy).Definition)
            .SelectMany(definition => definition.ProjectedColumns)
            .Where(column => column.Type == PortablePhysicalType.String)
            .ToArray();

        Assert.NotEmpty(projectedStrings);
        Assert.All(projectedStrings, column =>
            Assert.InRange(
                column.Length ?? 0,
                1,
                LegacyGroundworkStorageManifestPhysicalizer.LegacyStringProjectionLength));
    }

    [Fact]
    public async Task Durable_family_sources_cover_the_exact_ALL32_ledger_denominator_once()
    {
        var sources = new IGroundworkStorageManifestSource[]
        {
            new RuntimeGroundworkStorageManifestSource(),
            new IdentityGroundworkStorageManifestSource(),
            new SecretsGroundworkStorageManifestSource(),
            new DistributedGroundworkStorageManifestSource()
        };
        var actualRows = new List<string>();
        foreach (var source in sources)
        {
            var declaration = await source.CreateDeclarationAsync(CancellationToken.None);
            actualRows.AddRange(declaration.CoverageRows);
        }

        Assert.Equal(32, actualRows.Count);
        Assert.Equal(32, actualRows.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ExpectedAll32CoverageRows, actualRows.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("runtime", typeof(RuntimeGroundworkStorageManifestSource))]
    [InlineData("iam", typeof(IdentityGroundworkStorageManifestSource))]
    [InlineData("secrets", typeof(SecretsGroundworkStorageManifestSource))]
    [InlineData("distributed-runtime", typeof(DistributedGroundworkStorageManifestSource))]
    [InlineData("workflows-design", typeof(WorkflowsDesignGroundworkStorageManifestSource))]
    [InlineData("activities-design", typeof(ActivitiesDesignGroundworkStorageManifestSource))]
    [InlineData("publishing", typeof(PublishingGroundworkStorageManifestSource))]
    public void Family_registration_adds_its_source_scoped_and_idempotently(string family, Type implementationType)
    {
        var services = new ServiceCollection();
        RegisterFamily(services, family);
        RegisterFamily(services, family);

        var descriptor = Assert.Single(services.Where(x =>
            x.ServiceType == typeof(IGroundworkStorageManifestSource) &&
            x.ImplementationType == implementationType));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void Compatibility_facade_returns_snapshot_manifest_without_a_second_fingerprint()
    {
        var result = Validate([CreateDeclaration("feature", "unit")]);
        Assert.True(result.IsValid, JoinDiagnostics(result));
        var snapshot = result.Snapshot!;

        var manifest = GroundworkUnifiedManifest.Create(snapshot);

        Assert.Same(snapshot.Manifest, manifest);
        Assert.DoesNotContain(
            typeof(GroundworkUnifiedManifest).GetMembers(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
            member => member.Name.Contains("Fingerprint", StringComparison.Ordinal));
    }

    [Fact]
    public void Unified_contains_no_hard_coded_family_union_or_domain_project_references()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src/Elsa/Persistence/Groundwork/Unified/GroundworkUnifiedManifest.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "src/Elsa/Persistence/Groundwork/Unified/Elsa.Persistence.Groundwork.Unified.csproj"));

        Assert.DoesNotContain("ElsaRuntimeStorageManifest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkflowsDesignStorageManifest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivitiesDesignStorageManifest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishingGroundworkStorageManifest", source, StringComparison.Ordinal);
        var references = System.Xml.Linq.XDocument.Parse(project)
            .Descendants("ProjectReference")
            .Select(x => (string?)x.Attribute("Include"))
            .Where(x => x is not null)
            .ToArray();
        Assert.EndsWith(
            "/Composition/Elsa.Persistence.Groundwork.Composition.csproj",
            Assert.Single(references)!.Replace('\\', '/'),
            StringComparison.Ordinal);
    }

    private static GroundworkStorageCompositionValidationResult Validate(
        IReadOnlyCollection<GroundworkStorageManifestDeclaration> declarations,
        IReadOnlyCollection<Type>? requiredStores = null,
        GroundworkStorageNamingPolicyOptions? naming = null,
        GroundworkProviderCapabilitySnapshot? providerCapabilities = null)
    {
        var request = new GroundworkStorageCompositionValidationRequest(
            new StorageManifestIdentity("elsa-host-storage"),
            new StorageManifestOwner("elsa.host"),
            new StorageManifestVersion("1.0.0"),
            declarations,
            requiredStores ?? [],
            naming ?? GroundworkStorageNamingPolicyOptions.Identity,
            providerCapabilities ?? ProviderSnapshot(),
            ProviderPhysicalNameNormalizer.Identity);
        return new GroundworkStorageCompositionValidator().Validate(request);
    }

    private static GroundworkProviderCapabilitySnapshot ProviderSnapshot(
        ProviderIdentity? provider = null,
        GroundworkProviderTopologySnapshot? topology = null,
        IReadOnlyCollection<GroundworkActiveStoragePath>? activePaths = null,
        IReadOnlySet<CapabilityId>? supported = null,
        IReadOnlySet<CapabilityId>? evidenced = null)
    {
        provider ??= new ProviderIdentity("groundwork-sqlite", "1.0.0");
        topology ??= new GroundworkProviderTopologySnapshot(
            provider.Name,
            "file-backed-distinct-connections",
            new HashSet<string>(["persistent-storage", "independent-clients"], StringComparer.Ordinal));
        var report = new ProviderCapabilityReport(
            provider,
            supported ?? new HashSet<CapabilityId> { AtomicCommit },
            evidenced ?? new HashSet<CapabilityId> { AtomicCommit },
            IndexCapabilities.All,
            Enum.GetValues<PortableQueryOperation>().ToHashSet(),
            Enum.GetValues<ConcurrencyKind>().ToHashSet(),
            []);
        return new GroundworkProviderCapabilitySnapshot(report, topology, activePaths ?? []);
    }

    private static GroundworkStorageManifestDeclaration AtomicDeclaration() => CreateDeclaration(
        "feature",
        "unit",
        requiredRoutes:
        [
            new GroundworkStorageRouteRequirement(
                new StorageUnitIdentity("unit"),
                "atomic-route",
                new HashSet<CapabilityId> { AtomicCommit })
        ]);

    private static GroundworkActiveStoragePath AtomicPath() => new(
        "feature",
        new StorageUnitIdentity("unit"),
        "atomic-route",
        new HashSet<CapabilityId> { AtomicCommit });

    private static readonly string[] ExpectedAll32CoverageRows =
    [
        "distributed-command-transport",
        "distributed-execution-placement",
        "iam-application",
        "iam-claim-mapping",
        "iam-credential",
        "iam-external-identity",
        "iam-provider-configuration-global",
        "iam-provider-configuration-tenant",
        "iam-role",
        "iam-tenant-membership",
        "iam-user",
        "runtime-activity-execution-inspection",
        "runtime-activity-execution-state",
        "runtime-bookmark-state",
        "runtime-checkpoint-commit",
        "runtime-diagnostics-settings",
        "runtime-durable-timer",
        "runtime-durable-value-state",
        "runtime-executable-source-reference",
        "runtime-execution-liveness",
        "runtime-incident-state",
        "runtime-post-commit-outbox",
        "runtime-publication-projection-state",
        "runtime-recurring-trigger-schedule",
        "runtime-scheduler-poison",
        "runtime-scheduler-state",
        "runtime-scheduler-work-queue",
        "runtime-trigger-binding",
        "runtime-workflow-executable",
        "runtime-workflow-execution-state",
        "runtime-workflow-hold-state",
        "secrets-repository"
    ];

    private static GroundworkStorageManifestDeclaration CreateDeclaration(
        string featureIdentity,
        string unitIdentity,
        IReadOnlyCollection<Type>? stores = null,
        IReadOnlyCollection<GroundworkStorageRouteRequirement>? requiredRoutes = null,
        IReadOnlyCollection<GroundworkStorageTopologyRequirement>? topology = null) =>
        CreateDeclaration(
            featureIdentity,
            CreateManifest(featureIdentity, unitIdentity),
            stores,
            requiredRoutes,
            topology);

    private static GroundworkStorageManifestDeclaration CreateDeclaration(
        string featureIdentity,
        StorageManifest manifest,
        IReadOnlyCollection<Type>? stores = null,
        IReadOnlyCollection<GroundworkStorageRouteRequirement>? requiredRoutes = null,
        IReadOnlyCollection<GroundworkStorageTopologyRequirement>? topology = null) =>
        new(
            featureIdentity,
            manifest,
            stores ?? [],
            requiredRoutes ?? [],
            topology ?? [],
            []);

    private static StorageManifest CreateManifest(
        string featureIdentity,
        string unitIdentity,
        string? physicalName = null,
        string schemaVersion = "1.0.0")
    {
        var unit = new StorageUnit(
            new StorageUnitIdentity(unitIdentity),
            unitIdentity,
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Scoped,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [],
            [],
            PhysicalizationPolicy.Portable)
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(PhysicalTableDefinition.DedicatedDocumentTable(
                    physicalName ?? $"{unitIdentity}_documents")))
        };
        return new StorageManifest(
            new StorageManifestIdentity(featureIdentity),
            new StorageManifestOwner($"{featureIdentity}.owner"),
            new StorageManifestVersion(schemaVersion),
            [unit],
            new HashSet<string> { "schema-history", "optimistic-concurrency" },
            []);
    }

    private static IGroundworkStorageManifestSource CreateFamilySource(string family) => family switch
    {
        "runtime" => new RuntimeGroundworkStorageManifestSource(),
        "iam" => new IdentityGroundworkStorageManifestSource(),
        "secrets" => new SecretsGroundworkStorageManifestSource(),
        "distributed-runtime" => new DistributedGroundworkStorageManifestSource(),
        "workflows-design" => new WorkflowsDesignGroundworkStorageManifestSource(),
        "activities-design" => new ActivitiesDesignGroundworkStorageManifestSource(),
        "publishing" => new PublishingGroundworkStorageManifestSource(),
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
    };

    private static IReadOnlyCollection<Type> ExpectedStoreContracts(string family) => family switch
    {
        "runtime" =>
        [
            typeof(IBookmarkStateStore),
            typeof(IWorkflowExecutableStore),
            typeof(IWorkflowExecutableSourceReferenceStore),
            typeof(IActivityExecutionStateStore),
            typeof(IActivityExecutionInspectionStore),
            typeof(IActivityExecutionInspectionWriter),
            typeof(IWorkflowExecutionStateStore),
            typeof(IWorkflowTestScopeStore),
            typeof(IWorkflowTestScopeAdmissionStore),
            typeof(IWorkflowTestScopeCleanupStore),
            typeof(IDurableValueStateStore),
            typeof(ISchedulerStateStore),
            typeof(IExecutionLivenessStateStore),
            typeof(IWorkflowHoldStateStore),
            typeof(IIncidentStateStore),
            typeof(IPostCommitOutboxLookupStore),
            typeof(IRuntimeCheckpointCommitStore),
            typeof(IRuntimePostCommitOutboxClaimStore),
            typeof(IRuntimePostCommitOutboxClaimCompletionStore),
            typeof(IRuntimePostCommitOutboxStore),
            typeof(IWorkflowDispatchStore),
            typeof(IWorkflowDispatchQueryStore),
            typeof(IWorkflowDispatchDeleteStore),
            typeof(IWorkflowDispatchRetentionRootStore),
            typeof(IWorkflowDispatchAdmissionStore),
            typeof(IWorkflowDispatchCancellationStore),
            typeof(IWorkflowDispatchRedriveStore),
            typeof(IWorkflowSchedulerPoisonStore),
            typeof(IWorkflowSchedulerWorkQueue),
            typeof(IDurableTimerStore),
            typeof(IWorkflowTriggerBindingStore),
            typeof(IRecurringTriggerScheduleStore)
        ],
        "iam" =>
        [
            typeof(IUserStore),
            typeof(IRoleStore),
            typeof(IApplicationStore),
            typeof(ICredentialStore),
            typeof(IClaimMappingStore),
            typeof(IProviderConfigurationStore),
            typeof(IExternalIdentityStore),
            typeof(ITenantMembershipStore)
        ],
        "secrets" => [typeof(ISecretRepository)],
        "distributed-runtime" => [typeof(IExecutionPlacementStore), typeof(IExecutionCommandTransport)],
        "workflows-design" =>
        [
            typeof(IWorkflowDefinitionStore),
            typeof(IWorkflowDefinitionVersionStore),
            typeof(IWorkflowDefinitionDraftStore),
            typeof(IWorkflowDefinitionListProjectionStore),
            typeof(IWorkflowDefinitionVersionLayoutStore)
        ],
        "activities-design" =>
        [
            typeof(IActivityDefinitionStore),
            typeof(IActivityDefinitionVersionStore),
            typeof(IActivityAvailabilitySettingsStore)
        ],
        "publishing" =>
        [
            typeof(IPublicationSlotStore),
            typeof(IPublicationRecordStore),
            typeof(IPublicationPolicyStore),
            typeof(IPublicationProjectionIntentStore),
            typeof(IPublicationSnapshotReviewStore)
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
    };

    private static void RegisterFamily(IServiceCollection services, string family)
    {
        switch (family)
        {
            case "runtime":
                services.AddGroundworkRuntimeStores();
                break;
            case "iam":
                services.AddGroundworkIdentityStores();
                break;
            case "secrets":
                services.AddGroundworkSecretsStore();
                break;
            case "distributed-runtime":
                services.AddGroundworkDistributedRuntimeStores();
                break;
            case "workflows-design":
                services.AddGroundworkWorkflowsDesignStores();
                break;
            case "activities-design":
                services.AddGroundworkActivitiesDesignStores();
                break;
            case "publishing":
                services.AddGroundworkPublishingStores();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(family), family, null);
        }
    }

    private static string JoinDiagnostics(GroundworkStorageCompositionValidationResult result) =>
        string.Join("; ", result.Diagnostics.Select(x => $"{x.Code}: {x.Message}"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private interface ITestStore;

    private sealed class RecordingSource(string featureIdentity, List<string> calls)
        : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity { get; } = featureIdentity;

        public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(FeatureIdentity);
            return ValueTask.FromResult(CreateDeclaration(FeatureIdentity, $"{FeatureIdentity}-unit"));
        }
    }

    private sealed class ThrowingSource(string featureIdentity, Exception exception)
        : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity { get; } = featureIdentity;

        public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<GroundworkStorageManifestDeclaration>(exception);
    }
}
