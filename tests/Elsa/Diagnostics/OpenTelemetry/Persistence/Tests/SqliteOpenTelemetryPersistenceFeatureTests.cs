using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Tasks;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Sqlite;
using Elsa.Diagnostics.OpenTelemetry.Providers.InMemory;
using Elsa.Tasks.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Tests;

public sealed class SqliteOpenTelemetryPersistenceFeatureTests
{
    [Fact]
    public void RegistersEfCoreStoreAsReplacementAfterDefaultFeature()
    {
        var services = new ServiceCollection();
        new OpenTelemetryFeature().ConfigureServices(services);
        new SqliteOpenTelemetryPersistenceShellFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(InMemoryOpenTelemetryStore));
        Assert.Contains(services, d => d.ServiceType == typeof(EfCoreOpenTelemetryStore) && d.Lifetime == ServiceLifetime.Singleton);
        var storeDescriptor = services.Last(d => d.ServiceType == typeof(IOpenTelemetryStore));
        Assert.Equal(ServiceLifetime.Singleton, storeDescriptor.Lifetime);
        Assert.NotNull(storeDescriptor.ImplementationFactory);
    }

    [Fact]
    public void PreventsDefaultStoreInterfaceRegistrationWhenPersistenceFeatureRunsFirst()
    {
        var services = new ServiceCollection();
        new SqliteOpenTelemetryPersistenceShellFeature().ConfigureServices(services);
        new OpenTelemetryFeature().ConfigureServices(services);

        var storeDescriptor = Assert.Single(services.Where(d => d.ServiceType == typeof(IOpenTelemetryStore)));
        Assert.Equal(ServiceLifetime.Singleton, storeDescriptor.Lifetime);
        Assert.NotNull(storeDescriptor.ImplementationFactory);
    }

    [Fact]
    public void RegistersTheDrainingStartupTask()
    {
        var services = new ServiceCollection();
        new SqliteOpenTelemetryPersistenceShellFeature().ConfigureServices(services);

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IStartupTask) &&
            d.ImplementationType == typeof(StartOpenTelemetryDrainingStartupTask));
    }

    [Fact]
    public void RegistersTheDbContextFactoryAsSingletonToAvoidCaptiveDependency()
    {
        var services = new ServiceCollection();
        new SqliteOpenTelemetryPersistenceShellFeature().ConfigureServices(services);

        var factoryDescriptor = Assert.Single(services, d => d.ServiceType == typeof(IDbContextFactory<OpenTelemetryDbContext>));
        Assert.Equal(ServiceLifetime.Singleton, factoryDescriptor.Lifetime);
    }
}
