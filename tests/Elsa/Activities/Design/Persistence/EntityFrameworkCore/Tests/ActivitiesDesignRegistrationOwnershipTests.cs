using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Composition;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

using ElsaIdentityGenerator = Elsa.Primitives.Contracts.IIdentityGenerator;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class ActivitiesDesignRegistrationOwnershipTests
{
    [Fact]
    public void Groundwork_repeat_with_same_target_is_a_no_op_and_does_not_duplicate_units()
    {
        var services = new ServiceCollection();
        services.AddGroundworkActivitiesDesignStores("design");

        var before = services.ToArray();
        var backend = ActivitiesDesignPersistenceBackend.Find(services);
        Assert.NotNull(backend);
        var registry = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
            .ImplementationInstance as GroundworkStorageUnitRegistry;
        Assert.NotNull(registry);
        var beforeRegistrations = registry!.Registrations.ToArray();

        services.AddGroundworkActivitiesDesignStores("design");

        Assert.Same(backend, ActivitiesDesignPersistenceBackend.Find(services));
        AssertDescriptorsUnchanged(before, services);
        Assert.Equal(beforeRegistrations, registry.Registrations);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ActivitiesDesignPersistenceBackend));
    }

    [Fact]
    public void Groundwork_conflicting_target_fails_before_descriptor_registry_or_binding_mutation()
    {
        var services = new ServiceCollection();
        services.AddGroundworkActivitiesDesignStores("design-a");

        var before = services.ToArray();
        var registry = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
            .ImplementationInstance as GroundworkStorageUnitRegistry;
        var bindings = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(Elsa.Persistence.Groundwork.Targets.GroundworkManifestBindings))
            .ImplementationInstance as Elsa.Persistence.Groundwork.Targets.GroundworkManifestBindings;
        Assert.NotNull(registry);
        Assert.NotNull(bindings);
        var beforeRegistrations = registry!.Registrations.ToArray();
        var beforeBindings = bindings!.Capture();

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkActivitiesDesignStores("design-b"));

        AssertDescriptorsUnchanged(before, services);
        Assert.Equal(beforeRegistrations, registry.Registrations);
        AssertBindingsUnchanged(beforeBindings, bindings);
    }

    [Fact]
    public void Ef_repeat_with_same_provider_and_options_is_a_no_op()
    {
        var services = new ServiceCollection();
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=activities.db" });

        var before = services.ToArray();
        var backend = ActivitiesDesignPersistenceBackend.Find(services);
        Assert.NotNull(backend);

        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "sqlite", ConnectionString = "Data Source=activities.db" });

        Assert.Same(backend, ActivitiesDesignPersistenceBackend.Find(services));
        AssertDescriptorsUnchanged(before, services);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ActivitiesDesignSqliteDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(DbContextOptions<ActivitiesDesignSqliteDbContext>));
    }

    [Fact]
    public void Ef_conflicting_options_fail_before_descriptor_mutation()
    {
        var services = new ServiceCollection();
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=activities-a.db" });
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddActivitiesDesignEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=activities-b.db"
        }));

        AssertDescriptorsUnchanged(before, services);
    }

    [Fact]
    public void Pre_registered_host_identity_is_shared_across_ef_repeat_and_groundwork_switch()
    {
        var services = new ServiceCollection();
        var hostIdentity = new HostIdentityGenerator();
        services.AddSingleton<ElsaIdentityGenerator>(hostIdentity);

        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" });
        Assert.Same(hostIdentity, services.Single(descriptor => descriptor.ServiceType == typeof(ElsaIdentityGenerator)).ImplementationInstance);

        var beforeRepeat = services.ToArray();
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "sqlite" });
        AssertDescriptorsUnchanged(beforeRepeat, services);
        Assert.Same(hostIdentity, services.Single(descriptor => descriptor.ServiceType == typeof(ElsaIdentityGenerator)).ImplementationInstance);

        services.AddGroundworkActivitiesDesignStores();
        Assert.Same(hostIdentity, services.Single(descriptor => descriptor.ServiceType == typeof(ElsaIdentityGenerator)).ImplementationInstance);
    }

    [Fact]
    public void Groundwork_to_ef_switch_removes_groundwork_artifacts_and_resolves_one_sqlite_context()
    {
        var services = new ServiceCollection();
        services.AddGroundworkActivitiesDesignStores();

        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });

        var backend = ActivitiesDesignPersistenceBackend.Find(services);
        Assert.NotNull(backend);
        Assert.Equal(ActivitiesDesignPersistenceBackend.EntityFramework, backend!.Name);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(GroundworkV2ActivityDesignStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(GroundworkReusableActivityStores));
        Assert.Empty(ActivitiesUnitRegistrations(services));
        Assert.DoesNotContain(Bindings(services).Capture().Bindings, binding => binding.Key == typeof(ActivitiesDesignGroundworkStorageManifestSource));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ActivitiesDesignDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ActivitiesDesignSqliteDbContext));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(DbContextOptions<ActivitiesDesignSqliteDbContext>));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IActivityDefinitionStore));
        Assert.Equal(typeof(EfActivityDesignStores), Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfActivityDesignStores)).ImplementationType);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsType<ActivitiesDesignSqliteDbContext>(scope.ServiceProvider.GetRequiredService<ActivitiesDesignDbContext>());
        Assert.IsType<EfActivityDesignStores>(scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>());
    }

    [Fact]
    public void Groundwork_to_ef_switch_preserves_pre_registered_shared_catalog_and_other_lane_binding()
    {
        var services = new ServiceCollection();
        var registry = new GroundworkStorageUnitRegistry();
        var bindings = new Elsa.Persistence.Groundwork.Targets.GroundworkManifestBindings();
        services.AddSingleton(registry);
        services.AddSingleton(bindings);

        var workflowUnit = StorageUnit.Declare("workflow-design-preexisting", "workflow_design")
            .String("id", 128)
            .Key("id")
            .Build();
        registry.Declare(workflowUnit, "workflow");
        bindings.Bind(typeof(PreRegisteredWorkflowDesignLane), "workflow");
        var preRegistered = Assert.Single(registry.Registrations);

        services.AddGroundworkActivitiesDesignStores("activities");
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });

        var backend = ActivitiesDesignPersistenceBackend.Find(services);
        Assert.NotNull(backend);
        Assert.Equal(ActivitiesDesignPersistenceBackend.EntityFramework, backend!.Name);
        Assert.Same(registry, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        Assert.Equal([preRegistered], registry.Registrations);
        Assert.Equal("workflow", bindings.TargetFor(typeof(PreRegisteredWorkflowDesignLane)));
        Assert.DoesNotContain(bindings.Capture().Bindings, binding => binding.Key == typeof(ActivitiesDesignGroundworkStorageManifestSource));
    }

    [Fact]
    public void Framework_availability_default_is_replaced_by_either_backend_in_either_order()
    {
        AssertReplacesFrameworkDefault(services => services.AddGroundworkActivitiesDesignStores(), typeof(GroundworkActivityAvailabilitySettingsStore));
        AssertReplacesFrameworkDefault(services => services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" }), typeof(EfActivityDesignStores));
    }

    [Fact]
    public void Custom_availability_store_fails_closed_for_both_backends()
    {
        var ef = new ServiceCollection();
        ef.AddSingleton<IActivityAvailabilitySettingsStore, CustomAvailabilitySettingsStore>();
        var efBefore = ef.ToArray();
        Assert.Throws<InvalidOperationException>(() => ef.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" }));
        AssertDescriptorsUnchanged(efBefore, ef);

        var groundwork = new ServiceCollection();
        groundwork.AddSingleton<IActivityAvailabilitySettingsStore, CustomAvailabilitySettingsStore>();
        var groundworkBefore = groundwork.ToArray();
        Assert.Throws<InvalidOperationException>(() => groundwork.AddGroundworkActivitiesDesignStores());
        AssertDescriptorsUnchanged(groundworkBefore, groundwork);
    }

    [Fact]
    public void Failed_groundwork_to_ef_switch_restores_withdrawn_units_and_lane_binding()
    {
        var services = new ServiceCollection();
        services.AddGroundworkActivitiesDesignStores("activities");
        var registry = Registry(services);
        var bindings = Bindings(services);
        var beforeRegistrations = registry.Registrations.ToArray();
        var beforeBindings = bindings.Capture();
        // A stray EF artifact is only detected after the Groundwork backend has been withdrawn.
        services.AddSingleton(new ActivitiesDesignEntityFrameworkCoreOptions());
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" }));

        AssertDescriptorsUnchanged(before, services);
        Assert.Equal(beforeRegistrations, registry.Registrations);
        AssertBindingsUnchanged(beforeBindings, bindings);
        Assert.Equal(ActivitiesDesignPersistenceBackend.Groundwork, ActivitiesDesignPersistenceBackend.Find(services)!.Name);
    }

    private static void AssertReplacesFrameworkDefault(Action<IServiceCollection> register, Type expectedImplementation)
    {
        foreach (var frameworkDefaultFirst in new[] { true, false })
        {
            var services = new ServiceCollection();
            if (frameworkDefaultFirst)
                AddFrameworkDefault(services);
            register(services);
            if (!frameworkDefaultFirst)
                AddFrameworkDefault(services);

            var owned = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IActivityAvailabilitySettingsStore));
            var backend = ActivitiesDesignPersistenceBackend.Find(services)!;
            Assert.True(backend.Owns(owned));
            backend.EnsureOwnsRegisteredDescriptors(services);
            var beforeRepeat = services.ToArray();
            register(services);
            AssertDescriptorsUnchanged(beforeRepeat, services);

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scope = provider.CreateScope();
            Assert.IsType(expectedImplementation, scope.ServiceProvider.GetRequiredService<IActivityAvailabilitySettingsStore>());
        }
    }

    // Mirrors ActivitiesDesignApiFeature, which supplies the in-memory store until persistence is selected.
    private static void AddFrameworkDefault(IServiceCollection services) =>
        services.TryAddSingleton<IActivityAvailabilitySettingsStore, InMemoryActivityAvailabilitySettingsStore>();

    private static IEnumerable<GroundworkStorageUnitRegistration> ActivitiesUnitRegistrations(IServiceCollection services)
    {
        var activitiesUnitIds = ActivitiesDesignStorageManifest.CreateUnits().Select(unit => unit.Id.Value).ToHashSet(StringComparer.Ordinal);
        return Registry(services).Registrations.Where(registration => activitiesUnitIds.Contains(registration.Unit.Id.Value));
    }

    private static Elsa.Persistence.Groundwork.Targets.GroundworkManifestBindings Bindings(IServiceCollection services) =>
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(Elsa.Persistence.Groundwork.Targets.GroundworkManifestBindings))
            .ImplementationInstance as Elsa.Persistence.Groundwork.Targets.GroundworkManifestBindings
        ?? throw new InvalidOperationException("Groundwork manifest bindings were not registered.");

    [Fact]
    public void Ef_to_groundwork_switch_removes_ef_artifacts_and_publishes_one_groundwork_catalog()
    {
        var services = new ServiceCollection();
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });

        services.AddGroundworkActivitiesDesignStores();

        var backend = ActivitiesDesignPersistenceBackend.Find(services);
        Assert.NotNull(backend);
        Assert.Equal(ActivitiesDesignPersistenceBackend.Groundwork, backend!.Name);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ActivitiesDesignEntityFrameworkCoreOptions));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ActivitiesDesignDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ActivitiesDesignSqliteDbContext));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfActivityDesignStores));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry));
        Assert.Equal(ActivitiesDesignStorageManifest.CreateUnits().Count, Registry(services).Registrations.Count);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IActivityDefinitionStore));
        Assert.Equal(typeof(GroundworkActivityDefinitionStore), Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IActivityDefinitionStore)).ImplementationType);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsType<GroundworkActivityDefinitionStore>(scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>());
    }

    [Fact]
    public void Custom_activity_design_ports_fail_closed_without_partial_mutation()
    {
        var ef = new ServiceCollection();
        ef.AddSingleton<IActivityDefinitionLookup>(_ => throw new InvalidOperationException("custom"));
        var efBefore = ef.ToArray();
        Assert.Throws<InvalidOperationException>(() => ef.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" }));
        AssertDescriptorsUnchanged(efBefore, ef);

        var groundwork = new ServiceCollection();
        groundwork.AddSingleton<IActivityDefinitionLookup>(_ => throw new InvalidOperationException("custom"));
        var groundworkBefore = groundwork.ToArray();
        Assert.Throws<InvalidOperationException>(() => groundwork.AddGroundworkActivitiesDesignStores());
        AssertDescriptorsUnchanged(groundworkBefore, groundwork);
    }

    [Fact]
    public void Ef_corruption_fails_closed_without_mutating_the_corrupt_collection()
    {
        var services = new ServiceCollection();
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" });
        var backend = ActivitiesDesignPersistenceBackend.Find(services);
        Assert.NotNull(backend);
        services.Remove(backend.Descriptors[0]);
        var corrupt = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" }));

        AssertDescriptorsUnchanged(corrupt, services);
        Assert.Same(backend, ActivitiesDesignPersistenceBackend.Find(services));
    }

    [Fact]
    public void Groundwork_corruption_fails_closed_without_mutating_the_corrupt_collection()
    {
        var services = new ServiceCollection();
        services.AddGroundworkActivitiesDesignStores();
        var backend = ActivitiesDesignPersistenceBackend.Find(services);
        Assert.NotNull(backend);
        services.Remove(backend.Descriptors[0]);
        var corrupt = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkActivitiesDesignStores());

        AssertDescriptorsUnchanged(corrupt, services);
        Assert.Same(backend, ActivitiesDesignPersistenceBackend.Find(services));
    }

    [Fact]
    public void Groundwork_registration_rolls_back_service_registry_and_bindings_when_unit_declaration_fails()
    {
        var services = new ServiceCollection();
        var registry = new GroundworkStorageUnitRegistry();
        var bindings = new Elsa.Persistence.Groundwork.Targets.GroundworkManifestBindings();
        services.AddSingleton(registry);
        services.AddSingleton(bindings);
        registry.Declare(StorageUnit.Declare(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, "conflicting")
            .String(ActivitiesDesignStorageManifest.IdField, ActivitiesDesignStorageManifest.MaximumIdLength)
            .Key(ActivitiesDesignStorageManifest.IdField)
            .Build());
        var before = services.ToArray();
        var beforeRegistrations = registry.Registrations.ToArray();
        var beforeBindings = bindings.Capture();

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkActivitiesDesignStores());

        AssertDescriptorsUnchanged(before, services);
        Assert.Equal(beforeRegistrations, registry.Registrations);
        AssertBindingsUnchanged(beforeBindings, bindings);
        Assert.Null(ActivitiesDesignPersistenceBackend.Find(services));
    }

    private static GroundworkStorageUnitRegistry Registry(IServiceCollection services) =>
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
            .ImplementationInstance as GroundworkStorageUnitRegistry
        ?? throw new InvalidOperationException("Groundwork storage-unit registry was not registered.");

    private static void AssertDescriptorsUnchanged(IReadOnlyList<ServiceDescriptor> before, IServiceCollection after)
    {
        Assert.Equal(before.Count, after.Count);
        for (var index = 0; index < before.Count; index++)
            Assert.Same(before[index], after[index]);
    }

    private static void AssertBindingsUnchanged(
        Elsa.Persistence.Groundwork.Targets.GroundworkManifestBindingsSnapshot before,
        Elsa.Persistence.Groundwork.Targets.GroundworkManifestBindings after)
    {
        var current = after.Capture();
        Assert.Equal(before.Bindings, current.Bindings);
        Assert.True(before.ExplicitBindings.SetEquals(current.ExplicitBindings));
    }

    private sealed class HostIdentityGenerator : ElsaIdentityGenerator
    {
        public string Generate() => "host-generated";
    }

    private sealed class PreRegisteredWorkflowDesignLane
    {
    }

    private sealed class CustomAvailabilitySettingsStore : IActivityAvailabilitySettingsStore
    {
        public Task<Elsa.Activities.Design.Core.Models.ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(Elsa.Activities.Design.Core.Models.ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
