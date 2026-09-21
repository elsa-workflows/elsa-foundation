using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Composition;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

using ElsaIdentityGenerator = Elsa.Primitives.Contracts.IIdentityGenerator;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class ActivitiesDesignRegistrationOwnershipTests
{
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
    public void Pre_registered_host_identity_survives_registration_and_repeat()
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
    }

    [Fact]
    public void Ef_registration_resolves_one_sqlite_context_and_one_definition_store()
    {
        var services = new ServiceCollection();
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });

        var backend = ActivitiesDesignPersistenceBackend.Find(services);
        Assert.NotNull(backend);
        Assert.Equal(ActivitiesDesignPersistenceBackend.EntityFramework, backend!.Name);
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
    public void Framework_availability_default_is_replaced_in_either_order()
    {
        foreach (var frameworkDefaultFirst in new[] { true, false })
        {
            var services = new ServiceCollection();
            if (frameworkDefaultFirst)
                AddFrameworkDefault(services);
            services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" });
            if (!frameworkDefaultFirst)
                AddFrameworkDefault(services);

            var owned = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IActivityAvailabilitySettingsStore));
            var backend = ActivitiesDesignPersistenceBackend.Find(services)!;
            Assert.True(backend.Owns(owned));
            backend.EnsureOwnsRegisteredDescriptors(services);
            var beforeRepeat = services.ToArray();
            services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" });
            AssertDescriptorsUnchanged(beforeRepeat, services);

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scope = provider.CreateScope();
            Assert.IsType<EfActivityDesignStores>(scope.ServiceProvider.GetRequiredService<IActivityAvailabilitySettingsStore>());
        }
    }

    [Fact]
    public void Custom_availability_store_fails_closed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityAvailabilitySettingsStore, CustomAvailabilitySettingsStore>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" }));

        AssertDescriptorsUnchanged(before, services);
    }

    [Fact]
    public void Custom_activity_design_ports_fail_closed_without_partial_mutation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityDefinitionLookup>(_ => throw new InvalidOperationException("custom"));
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" }));

        AssertDescriptorsUnchanged(before, services);
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

    // Mirrors ActivitiesDesignApiFeature, which supplies the in-memory store until persistence is selected.
    private static void AddFrameworkDefault(IServiceCollection services) =>
        services.TryAddSingleton<IActivityAvailabilitySettingsStore, InMemoryActivityAvailabilitySettingsStore>();

    private static void AssertDescriptorsUnchanged(IReadOnlyList<ServiceDescriptor> before, IServiceCollection after)
    {
        Assert.Equal(before.Count, after.Count);
        for (var index = 0; index < before.Count; index++)
            Assert.Same(before[index], after[index]);
    }

    private sealed class HostIdentityGenerator : ElsaIdentityGenerator
    {
        public string Generate() => "host-generated";
    }

    private sealed class CustomAvailabilitySettingsStore : IActivityAvailabilitySettingsStore
    {
        public Task<Elsa.Activities.Design.Core.Models.ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(Elsa.Activities.Design.Core.Models.ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
