using System.Reflection;
using Elsa.Workbench.Tests.BackgroundActivities;
using Elsa.Cluster.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workbench.BackgroundActivities.Tests;

internal static class NativeHostingWitnessTestServices
{
    public static (IServiceCollection Services, ConfigurationManager Configuration) Create(
        NativeHostingWitnessDisposalCounts? disposalCounts = null)
    {
        IServiceCollection services = new ServiceCollection();
        var configuration = CreateValidConfiguration();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IClusterMembership>(NativeHostingWitnessTestProxy<IClusterMembership>.Create());
        services.AddSingleton<IWorkflowExecutionActorProvider>(NativeHostingWitnessTestProxy<IWorkflowExecutionActorProvider>.Create());
        services.AddSingleton<IExecutionPlacementService>(NativeHostingWitnessTestProxy<IExecutionPlacementService>.Create());

        var ownership = ServiceDescriptor.Scoped<IRuntimeExecutionOwnershipService>(_ =>
            NativeHostingWitnessTestProxy<IRuntimeExecutionOwnershipService>.Create(
                disposalCounts is null ? null : new Action(disposalCounts.OwnershipDisposed)));
        var placement = ServiceDescriptor.Scoped<IExecutionPlacementStore>(_ =>
            NativeHostingWitnessTestProxy<IExecutionPlacementStore>.Create(
                disposalCounts is null ? null : new Action(disposalCounts.PlacementDisposed)));
        var transport = ServiceDescriptor.Scoped<IExecutionCommandTransport>(_ =>
            NativeHostingWitnessTestProxy<IExecutionCommandTransport>.Create(
                disposalCounts is null ? null : new Action(disposalCounts.TransportDisposed)));
        services.Add(ownership);
        services.Add(placement);
        services.Add(transport);

        ExecutionPlacementStoreBackend.Register(services,
            new ExecutionPlacementStoreBackend(ExecutionPlacementStoreBackend.EntityFramework, placement));
        ExecutionCommandTransportBackend.Register(services,
            new ExecutionCommandTransportBackend(ExecutionCommandTransportBackend.EntityFramework, transport));

        return (services, configuration);
    }

    public static ConfigurationManager CreateValidConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration[FeatureKey("WorkflowsRuntimeEntityFrameworkCore", "ConnectionString")] =
            Connection("runtime.db");
        configuration["Elsa:Cluster:Membership:EntityFrameworkCore:ConnectionString"] =
            Connection("membership.db");
        configuration[FeatureKey("WorkflowsRuntimeDistributedEntityFrameworkCorePersistence", "ConnectionString")] =
            Connection("placement.db");
        configuration[FeatureKey("WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence", "ConnectionString")] =
            Connection("transport.db");
        configuration["CShells:Shells:default:Features:FileSystemDistributedLocking:LocksFolderPath"] =
            Path.Combine(Path.GetTempPath(), "native-hosting-witness-locks");
        return configuration;
    }

    public static void Register(IServiceCollection services) =>
        NativeHostingWitnessRegistration.Register(
            services,
            Path.Combine(Path.GetTempPath(), "native-hosting-witness-journal"),
            "test-role");

    public static ServiceDescriptor SingleDescriptor<TService>(IServiceCollection services) =>
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TService) && !descriptor.IsKeyedService);

    public static void ReplacePlacementStoreDescriptor(IServiceCollection services, ServiceDescriptor replacement)
    {
        var backend = ExecutionPlacementStoreBackend.Find(services)!;
        services.Remove(SingleDescriptor<IExecutionPlacementStore>(services));
        RemoveBackend(services, backend);
        services.Add(replacement);
        ExecutionPlacementStoreBackend.Register(services, new ExecutionPlacementStoreBackend(backend.Name, replacement));
    }

    public static void ReplaceCommandTransportDescriptor(IServiceCollection services, ServiceDescriptor replacement)
    {
        var backend = ExecutionCommandTransportBackend.Find(services)!;
        services.Remove(SingleDescriptor<IExecutionCommandTransport>(services));
        RemoveBackend(services, backend);
        services.Add(replacement);
        ExecutionCommandTransportBackend.Register(services, new ExecutionCommandTransportBackend(backend.Name, replacement));
    }

    public static void RemoveBackend<TBackend>(IServiceCollection services, TBackend? backend) where TBackend : class
    {
        if (backend is null)
            return;
        var descriptor = services.Single(item => ReferenceEquals(item.ImplementationInstance, backend));
        services.Remove(descriptor);
    }

    public static void AddMismatchedPlacementBackend(IServiceCollection services)
    {
        var backend = ExecutionPlacementStoreBackend.Find(services)!;
        RemoveBackend(services, backend);
        var unregistered = ServiceDescriptor.Scoped<IExecutionPlacementStore>(_ =>
            NativeHostingWitnessTestProxy<IExecutionPlacementStore>.Create());
        ExecutionPlacementStoreBackend.Register(services, new ExecutionPlacementStoreBackend(backend.Name, unregistered));
    }

    public static void AddMismatchedCommandTransportBackend(IServiceCollection services)
    {
        var backend = ExecutionCommandTransportBackend.Find(services)!;
        RemoveBackend(services, backend);
        var unregistered = ServiceDescriptor.Scoped<IExecutionCommandTransport>(_ =>
            NativeHostingWitnessTestProxy<IExecutionCommandTransport>.Create());
        ExecutionCommandTransportBackend.Register(services, new ExecutionCommandTransportBackend(backend.Name, unregistered));
    }

    private static string FeatureKey(string feature, string setting) =>
        $"CShells:Shells:default:Features:{feature}:{setting}";

    private static string Connection(string fileName) =>
        $"Data Source={Path.Combine(Path.GetTempPath(), fileName)};Pooling=False";
}

internal sealed class NativeHostingWitnessDisposalCounts
{
    private int _ownership;
    private int _placement;
    private int _transport;

    public int Ownership => Volatile.Read(ref _ownership);
    public int Placement => Volatile.Read(ref _placement);
    public int Transport => Volatile.Read(ref _transport);

    public void OwnershipDisposed() => Interlocked.Increment(ref _ownership);
    public void PlacementDisposed() => Interlocked.Increment(ref _placement);
    public void TransportDisposed() => Interlocked.Increment(ref _transport);
}

public class NativeHostingWitnessTestProxy<TService> : DispatchProxy, IDisposable, IAsyncDisposable where TService : class
{
    private Action? _disposed;

    public static TService Create(Action? disposed = null)
    {
        var proxy = DispatchProxy.Create<TService, NativeHostingWitnessTestProxy<TService>>();
        ((NativeHostingWitnessTestProxy<TService>)(object)proxy)._disposed = disposed;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new NotSupportedException($"The {typeof(TService).Name} test proxy is not expected to be invoked.");

    public void Dispose() => _disposed?.Invoke();

    public ValueTask DisposeAsync()
    {
        _disposed?.Invoke();
        return ValueTask.CompletedTask;
    }
}
