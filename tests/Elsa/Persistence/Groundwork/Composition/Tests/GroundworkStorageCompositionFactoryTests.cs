using Elsa.Events.Core.Contracts;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Composition.Tests;

public sealed class GroundworkStorageCompositionFactoryTests
{
    [Fact]
    public async Task Registered_factory_builds_the_selected_composition_from_scoped_sources()
    {
        var providerIdentity = new ProviderIdentity("groundwork-sqlite", "1.0.0");
        var publishedEvents = new List<IEvent>();
        var services = new ServiceCollection();
        services.AddGroundworkRuntimeStores();
        services.AddGroundworkStorageComposition();
        services.AddScoped<IInlineEventPublisher>(sp => new RecordingInlineEventPublisher(
            sp.GetRequiredService<GroundworkStorageCompositionHandler>(),
            publishedEvents));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<GroundworkStorageCompositionFactory>();
        var source = await factory.CreateSourceAsync(ProviderCapabilities(), ProviderPhysicalNameNormalizer.Identity);

        Assert.Equal("elsa-documents", source.PhysicalTarget.ManifestIdentity.Value);
        Assert.Equal(providerIdentity, source.PhysicalTarget.Provider);
        Assert.Contains(
            source.Snapshot.ManifestSources,
            declaration => declaration.FeatureIdentity == "elsa-workflows-runtime");
        Assert.IsType<OnGroundworkStorageComposing>(Assert.Single(publishedEvents));
    }

    [Fact]
    public void Registration_is_idempotent_and_preserves_a_host_naming_policy()
    {
        var naming = new GroundworkStorageNamingPolicyOptions(
            "host-prefix-v1",
            context => $"host_{context.FeatureDefaultLogicalName}");
        var services = new ServiceCollection();
        services.AddSingleton(naming);

        services.AddGroundworkStorageComposition();
        services.AddGroundworkStorageComposition();

        using var provider = services.BuildServiceProvider();
        Assert.Same(naming, provider.GetRequiredService<GroundworkStorageNamingPolicyOptions>());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStorageCompositionValidator));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStorageCompositionHandler));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStorageCompositionFactory));
    }

    [Fact]
    public void Deployment_schema_registration_is_idempotent_and_rejects_a_different_authority()
    {
        var services = new ServiceCollection();
        services.AddGroundworkStorageComposition<RuntimeDeploymentSchema>();
        services.AddGroundworkStorageComposition<RuntimeDeploymentSchema>();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(global::Groundwork.Core.SchemaEvolution.IPhysicalSchemaManifestSource));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IGroundworkStorageManifestSource));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddGroundworkStorageComposition<SecretsDeploymentSchema>());
        Assert.Contains(nameof(RuntimeDeploymentSchema), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretsDeploymentSchema), exception.Message, StringComparison.Ordinal);

        var reverse = new ServiceCollection();
        reverse.AddGroundworkStorageComposition<SecretsDeploymentSchema>();
        var reverseException = Assert.Throws<InvalidOperationException>(() =>
            reverse.AddGroundworkStorageComposition<RuntimeDeploymentSchema>());
        Assert.Equal(exception.Message, reverseException.Message);
    }

    [Fact]
    public void Reference_deployment_schema_selects_identity_only_through_the_identity_variant()
    {
        var defaultManifest = new GroundworkAllFeaturesDeploymentSchema().CreateManifest();
        var identityManifest = new GroundworkAllFeaturesWithIdentityDeploymentSchema().CreateManifest();

        Assert.DoesNotContain(
            defaultManifest.StorageUnits,
            unit => unit.Identity.Value == IdentityStorageManifest.IdentityUserDocumentKind);
        Assert.Contains(
            identityManifest.StorageUnits,
            unit => unit.Identity.Value == IdentityStorageManifest.IdentityUserDocumentKind);
        Assert.Contains(
            identityManifest.StorageUnits,
            unit => unit.Identity.Value == ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind);
        Assert.True(identityManifest.StorageUnits.Count > defaultManifest.StorageUnits.Count);
    }

    [Fact]
    public void Reference_deployment_schema_unions_exact_workflow_and_activity_physical_definitions()
    {
        var manifest = new GroundworkAllFeaturesDeploymentSchema().CreateManifest();
        var expectedUnits = WorkflowsDesignStorageManifest.Create().StorageUnits
            .Concat(ActivitiesDesignStorageManifest.Create().StorageUnits)
            .ToArray();

        Assert.Equal("elsa-documents", manifest.Identity.Value);
        Assert.Equal("elsa.documents", manifest.Owner.Value);
        Assert.Equal("1.0.0", manifest.Version.Value);
        foreach (var expected in expectedUnits)
        {
            var actual = Assert.Single(manifest.StorageUnits, unit => unit.Identity == expected.Identity);
            var expectedStorage = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(
                Assert.IsType<StorageUnitPhysicalStorage>(expected.PhysicalStorage).Policy);
            var actualStorage = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(
                Assert.IsType<StorageUnitPhysicalStorage>(actual.PhysicalStorage).Policy);

            Assert.Equal(expectedStorage.Definition, actualStorage.Definition);
            Assert.Equal(
                expected.PhysicalStorage.LogicalIndexes.Select(index => index.Identity),
                actual.PhysicalStorage.LogicalIndexes.Select(index => index.Identity));
            Assert.Equal(
                expected.PhysicalStorage.BoundedQueries.Select(query => query.Identity),
                actual.PhysicalStorage.BoundedQueries.Select(query => query.Identity));
        }
    }

    [Fact]
    public void Deployment_schema_exposes_one_host_naming_policy_for_both_design_families()
    {
        var source = new PrefixedDesignDeploymentSchema();
        var manifest = source.CreateManifest();
        var namePolicy = source.CreateNamePolicy();

        Assert.Equal(
            "host_workflowDefinition",
            namePolicy.ResolveName(new PhysicalNameContext(
                new StorageUnitIdentity(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind),
                PhysicalObjectKind.PrimaryStorage,
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind)));
        Assert.Equal(
            "host_activityDefinition",
            namePolicy.ResolveName(new PhysicalNameContext(
                new StorageUnitIdentity(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind),
                PhysicalObjectKind.PrimaryStorage,
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind)));
        Assert.Contains(
            manifest.StorageUnits,
            unit => unit.Identity.Value == WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind);
        Assert.Contains(
            manifest.StorageUnits,
            unit => unit.Identity.Value == ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind);
    }

    [Fact]
    public void Identity_reference_deployment_schema_registers_as_the_exact_runtime_authority()
    {
        var services = new ServiceCollection();
        services.AddGroundworkStorageComposition<GroundworkAllFeaturesWithIdentityDeploymentSchema>();

        using var provider = services.BuildServiceProvider();
        var source = provider.GetRequiredService<global::Groundwork.Core.SchemaEvolution.IPhysicalSchemaManifestSource>();
        var manifest = source.CreateManifest();

        Assert.IsType<GroundworkAllFeaturesWithIdentityDeploymentSchema>(source);
        Assert.Contains(
            manifest.StorageUnits,
            unit => unit.Identity.Value == IdentityStorageManifest.IdentityUserDocumentKind);
    }

    [Fact]
    public async Task Deployment_schema_rejects_runtime_declarations_added_outside_its_authority()
    {
        var services = new ServiceCollection();
        services.AddGroundworkStorageComposition<RuntimeDeploymentSchema>();
        services.AddGroundworkSecretsStore();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<GroundworkStorageCompositionException>(() =>
            scope.ServiceProvider.GetRequiredService<GroundworkStorageCompositionFactory>()
                .CreateSourceAsync(
                    ProviderCapabilities(),
                    ProviderPhysicalNameNormalizer.Identity)
                .AsTask());

        Assert.Contains(nameof(RuntimeDeploymentSchema), exception.Message, StringComparison.Ordinal);
        Assert.Contains("elsa-secrets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deployment_schema_rejects_a_late_runtime_naming_policy_override()
    {
        var services = new ServiceCollection();
        services.AddGroundworkStorageComposition<RuntimeDeploymentSchema>();
        services.AddSingleton(new GroundworkStorageNamingPolicyOptions(
            "late-override-v1",
            context => $"late_{context.FeatureDefaultLogicalName}"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<GroundworkStorageCompositionException>(() =>
            scope.ServiceProvider.GetRequiredService<GroundworkStorageCompositionFactory>()
                .CreateSourceAsync(
                    ProviderCapabilities(),
                    ProviderPhysicalNameNormalizer.Identity)
                .AsTask());

        Assert.Contains(nameof(RuntimeDeploymentSchema), exception.Message, StringComparison.Ordinal);
        Assert.Contains("naming policy", exception.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingInlineEventPublisher(
        GroundworkStorageCompositionHandler handler,
        ICollection<IEvent> publishedEvents) : IInlineEventPublisher
    {
        public async Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            publishedEvents.Add(@event);
            await handler.Handle((OnGroundworkStorageComposing)@event, cancellationToken);
        }
    }

    public sealed class RuntimeDeploymentSchema : GroundworkDeploymentSchemaManifestSource
    {
        protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
            [typeof(RuntimeGroundworkStorageManifestSource)];
    }

    public sealed class SecretsDeploymentSchema : GroundworkDeploymentSchemaManifestSource
    {
        protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
            [typeof(SecretsGroundworkStorageManifestSource)];
    }

    public sealed class PrefixedDesignDeploymentSchema : GroundworkDeploymentSchemaManifestSource
    {
        protected override IReadOnlyCollection<Type> ManifestSourceTypes =>
        [
            typeof(WorkflowsDesignGroundworkStorageManifestSource),
            typeof(ActivitiesDesignGroundworkStorageManifestSource)
        ];

        protected override GroundworkStorageNamingPolicyOptions CreateStorageNamingPolicy() =>
            new("design-host-prefix-v1", context => $"host_{context.FeatureDefaultLogicalName}");
    }

    private static GroundworkProviderCapabilitySnapshot ProviderCapabilities()
    {
        var provider = new ProviderIdentity("groundwork-sqlite", "1.0.0");
        return GroundworkProviderCapabilitySnapshot.ForFeatureRoutes(
            new ProviderCapabilityReport(
                provider,
                new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit },
                new HashSet<CapabilityId> { WellKnownCapabilities.AtomicCommit },
                IndexCapabilities.All,
                Enum.GetValues<PortableQueryOperation>().ToHashSet(),
                Enum.GetValues<ConcurrencyKind>().ToHashSet(),
                []),
            new GroundworkProviderTopologySnapshot(
                provider.Name,
                "sqlite-file",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            RuntimeGroundworkStorageManifestSource.FeatureName,
            [RuntimeGroundworkStorageManifestSource.CreateCheckpointCommitRouteRequirement()]);
    }
}
