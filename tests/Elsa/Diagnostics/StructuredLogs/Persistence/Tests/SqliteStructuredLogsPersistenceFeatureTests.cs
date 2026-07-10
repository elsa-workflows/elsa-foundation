using CShells.Lifecycle;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Storage;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Tasks;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Sqlite;
using Elsa.Tasks.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Tests;

public sealed class SqliteStructuredLogsPersistenceFeatureTests
{
    private static ServiceCollection Configure()
    {
        var services = new ServiceCollection();
        new SqliteStructuredLogsPersistenceShellFeature().ConfigureServices(services);
        return services;
    }

    [Fact]
    public void RegistersEfCoreStoreAsTheStructuredLogStore()
    {
        var services = Configure();

        Assert.Contains(services, d => d.ServiceType == typeof(EfCoreStructuredLogStore) && d.Lifetime == ServiceLifetime.Singleton);

        var storeDescriptor = Assert.Single(services, d => d.ServiceType == typeof(IStructuredLogStore));
        Assert.Equal(ServiceLifetime.Singleton, storeDescriptor.Lifetime);
        Assert.NotNull(storeDescriptor.ImplementationFactory);
    }

    [Fact]
    public void RegistersTheDrainingStartupTask()
    {
        var services = Configure();

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IStartupTask) &&
            d.ImplementationType == typeof(StartStructuredLogDrainingStartupTask));
    }

    [Fact]
    public void RegistersTheDrainingShellTerminator()
    {
        var services = Configure();

        Assert.Contains(services, d =>
            d.ServiceType == typeof(StopStructuredLogDrainingShellTerminator) &&
            d.Lifetime == ServiceLifetime.Transient);
        var registration = Assert.Single(services
            .Where(d => d.ServiceType == typeof(ShellTerminatorRegistration))
            .Select(d => d.ImplementationInstance)
            .OfType<ShellTerminatorRegistration>(),
            x => x.TerminatorType == typeof(StopStructuredLogDrainingShellTerminator));
        Assert.Equal(LifecyclePhase.Default, registration.Phase);
        Assert.Equal(0, registration.Order);
    }

    [Fact]
    public void RegistersTheDbContextFactoryAsSingletonToAvoidCaptiveDependency()
    {
        var services = Configure();

        var factoryDescriptor = Assert.Single(services, d => d.ServiceType == typeof(IDbContextFactory<StructuredLogsDbContext>));
        Assert.Equal(ServiceLifetime.Singleton, factoryDescriptor.Lifetime);
    }
}
