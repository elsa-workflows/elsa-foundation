using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Serialization.Core;
using Elsa.Primitives.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Groundwork.Store;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkActivitiesDesignRegistrationTests
{
    [Fact]
    public void Fork_receipt_and_candidate_units_remain_admitted_in_the_clean_v2_catalog()
    {
        var services = new ServiceCollection();
        services.AddGroundworkActivitiesDesignStores();

        var registry = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
            .ImplementationInstance as GroundworkStorageUnitRegistry;
        Assert.NotNull(registry);
        Assert.Contains(registry!.Registrations, registration =>
            registration.Unit.Id.Value == ActivitiesDesignStorageManifest.ActivityForkReceiptDocumentKind);
        Assert.Contains(registry.Registrations, registration =>
            registration.Unit.Id.Value == ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind);
    }

    [Fact]
    public void Registers_read_ports_commands_and_lookup_as_groundwork_implementations()
    {
        var services = new ServiceCollection();
        services.AddGroundworkActivitiesDesignStores();

        AssertImplementation<IActivityDefinitionStore, GroundworkActivityDefinitionStore>(services);
        AssertImplementation<IActivityDefinitionVersionStore, GroundworkActivityDefinitionVersionStore>(services);
        AssertImplementation<IAddActivityDefinitionCommand, GroundworkAddActivityDefinitionCommand>(services);
        AssertImplementation<IAddActivityDefinitionVersionCommand, GroundworkAddActivityDefinitionVersionCommand>(services);
        AssertImplementation<IActivityAvailabilitySettingsStore, GroundworkActivityAvailabilitySettingsStore>(services);
        AssertImplementation<IActivityDefinitionManagementProjectionStore, GroundworkActivityDefinitionManagementProjectionStore>(services);
        AssertImplementation<IActivityDefinitionLookup, ActivityDefinitionLookup>(services);
        AssertImplementation<IDesignAtomicWriter, GroundworkDesignAtomicWrite>(services);

        AssertScopedOnce<GroundworkReusableActivityStores>(services);
        AssertScopedOnce<GroundworkRecommendedActivityDefinitionPickerStore>(services);
        AssertScopedOnce<GroundworkActivityManagementProjectionWriter>(services);
        AssertScopedOnce<GroundworkActivityManagementProjectionRetention>(services);
        AssertScopedOnce<GroundworkActivityDependencyProjection>(services);
        AssertScopedOnce<GroundworkActivityUpgradePlanStore>(services);
    }

    [Fact]
    public void Groundwork_registration_overrides_a_prior_store()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityDefinitionStore, PriorStore>();
        services.AddGroundworkActivitiesDesignStores();

        AssertImplementation<IActivityDefinitionStore, GroundworkActivityDefinitionStore>(services);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IActivityDefinitionStore));
    }

    [Fact]
    public void Groundwork_registration_preserves_a_prior_design_atomic_writer()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDesignAtomicWriter, PriorDesignAtomicWriter>();
        services.AddGroundworkActivitiesDesignStores();

        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == typeof(IDesignAtomicWriter));
        Assert.Equal(typeof(PriorDesignAtomicWriter), descriptor.ImplementationType);
    }

    [Fact]
    public void Groundwork_registration_allows_one_post_composition_design_atomic_writer_replacement()
    {
        var services = new ServiceCollection();
        services.AddGroundworkActivitiesDesignStores();
        services.Replace(ServiceDescriptor.Scoped<IDesignAtomicWriter, PriorDesignAtomicWriter>());

        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == typeof(IDesignAtomicWriter));
        Assert.Equal(typeof(PriorDesignAtomicWriter), descriptor.ImplementationType);
    }

    [Fact]
    public void Repeated_registration_keeps_shared_scoped_adapters_registered_once()
    {
        var services = new ServiceCollection();
        services.AddGroundworkActivitiesDesignStores();
        services.AddGroundworkActivitiesDesignStores();

        foreach (var serviceType in new[]
                 {
                     typeof(IActivityDefinitionStore),
                     typeof(IActivityDefinitionVersionStore),
                     typeof(IAddActivityDefinitionCommand),
                     typeof(IAddActivityDefinitionVersionCommand),
                     typeof(IActivityDefinitionManagementProjectionStore),
                     typeof(IActivityDefinitionLookup),
                     typeof(IDesignAtomicWriter)
                 })
            Assert.Single(services, descriptor => descriptor.ServiceType == serviceType);

        AssertScopedOnce<GroundworkReusableActivityStores>(services);
        AssertScopedOnce<GroundworkRecommendedActivityDefinitionPickerStore>(services);
        AssertScopedOnce<GroundworkActivityManagementProjectionWriter>(services);
        AssertScopedOnce<GroundworkActivityManagementProjectionRetention>(services);
        AssertScopedOnce<GroundworkActivityDependencyProjection>(services);
        AssertScopedOnce<GroundworkActivityUpgradePlanStore>(services);
    }

    private static void AssertImplementation<TContract, TImplementation>(IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == typeof(TContract));
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    private static void AssertScopedOnce<TService>(IServiceCollection services) =>
        AssertScopedOnce(services, typeof(TService));

    private static void AssertScopedOnce(IServiceCollection services, Type serviceType)
    {
        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == serviceType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    private sealed class PriorStore : IActivityDefinitionStore
    {
        public Task<Core.Entities.ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Core.Entities.ActivityDefinition?> FindAsync(Core.Filters.ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Core.Entities.ActivityDefinition>> ListAsync(Core.Filters.ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Core.Entities.ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PriorDesignAtomicWriter : IDesignAtomicWriter
    {
        public Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(
            GroundworkDesignAtomicWriteRequest request,
            Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GroundworkDesignAtomicWriteResult> ExecuteAsync(
            GroundworkDesignAtomicWriteRequest request,
            Func<CancellationToken, Task>? beforeAttempt,
            Func<GroundworkDesignAtomicWriteContext, CancellationToken, Task<GroundworkDesignAtomicWriteStageResult>> stage,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
