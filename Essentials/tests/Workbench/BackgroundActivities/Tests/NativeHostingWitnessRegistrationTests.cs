using Elsa.Cluster.Core.Contracts;
using Elsa.Workbench.Tests.BackgroundActivities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Workbench.BackgroundActivities.Tests;

public class NativeHostingWitnessRegistrationTests
{
    [Fact]
    public void Register_decorates_each_scoped_contract_and_refreshes_backend_ownership()
    {
        var (services, _) = NativeHostingWitnessTestServices.Create();

        NativeHostingWitnessTestServices.Register(services);

        Assert.Equal(ServiceLifetime.Scoped, NativeHostingWitnessTestServices.SingleDescriptor<IRuntimeExecutionOwnershipService>(services).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, NativeHostingWitnessTestServices.SingleDescriptor<IExecutionPlacementStore>(services).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, NativeHostingWitnessTestServices.SingleDescriptor<IExecutionCommandTransport>(services).Lifetime);

        var placementBackend = Assert.IsType<ExecutionPlacementStoreBackend>(ExecutionPlacementStoreBackend.Find(services));
        var transportBackend = Assert.IsType<ExecutionCommandTransportBackend>(ExecutionCommandTransportBackend.Find(services));
        Assert.Equal(ExecutionPlacementStoreBackend.EntityFramework, placementBackend.Name);
        Assert.Equal(ExecutionCommandTransportBackend.EntityFramework, transportBackend.Name);
        placementBackend.EnsureOwnsRegisteredContract(services);
        transportBackend.EnsureOwnsRegisteredContract(services);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData("missing-placement-backend", "requires a selected execution placement backend")]
    [InlineData("in-memory-placement-backend", "requires the EF execution placement backend")]
    [InlineData("mismatched-placement-backend", "no longer exclusively owns IExecutionPlacementStore")]
    [InlineData("missing-transport-backend", "requires a selected command transport backend")]
    [InlineData("in-memory-transport-backend", "requires the EF command transport backend")]
    [InlineData("mismatched-transport-backend", "no longer exclusively owns IExecutionCommandTransport")]
    [InlineData("missing-membership", "requires exactly one IClusterMembership registration, found 0")]
    [InlineData("duplicate-membership", "requires exactly one IClusterMembership registration, found 2")]
    [InlineData("missing-actor-provider", "requires exactly one IWorkflowExecutionActorProvider registration, found 0")]
    [InlineData("duplicate-actor-provider", "requires exactly one IWorkflowExecutionActorProvider registration, found 2")]
    [InlineData("missing-placement-service", "requires exactly one IExecutionPlacementService registration, found 0")]
    [InlineData("duplicate-placement-service", "requires exactly one IExecutionPlacementService registration, found 2")]
    [InlineData("missing-ownership", "requires exactly one scoped IRuntimeExecutionOwnershipService registration")]
    [InlineData("duplicate-ownership", "requires exactly one scoped IRuntimeExecutionOwnershipService registration")]
    [InlineData("singleton-ownership", "requires exactly one scoped IRuntimeExecutionOwnershipService registration")]
    [InlineData("transient-ownership", "requires exactly one scoped IRuntimeExecutionOwnershipService registration")]
    [InlineData("missing-placement-store", "no longer exclusively owns IExecutionPlacementStore")]
    [InlineData("duplicate-placement-store", "no longer exclusively owns IExecutionPlacementStore")]
    [InlineData("singleton-placement-store", "requires exactly one scoped IExecutionPlacementStore registration")]
    [InlineData("transient-placement-store", "requires exactly one scoped IExecutionPlacementStore registration")]
    [InlineData("missing-command-transport", "no longer exclusively owns IExecutionCommandTransport")]
    [InlineData("duplicate-command-transport", "no longer exclusively owns IExecutionCommandTransport")]
    [InlineData("singleton-command-transport", "requires exactly one scoped IExecutionCommandTransport registration")]
    [InlineData("transient-command-transport", "requires exactly one scoped IExecutionCommandTransport registration")]
    public void Register_fails_fast_for_invalid_service_registration(string scenario, string expectedMessage)
    {
        var (services, _) = NativeHostingWitnessTestServices.Create();
        MutateRegistration(services, scenario);

        var exception = Assert.Throws<InvalidOperationException>(() => NativeHostingWitnessTestServices.Register(services));

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Theory]
    [InlineData("non-sqlite-provider")]
    [InlineData("relative-data-source")]
    [InlineData("relative-lock-folder")]
    public void Resolved_observation_settings_reject_unsafe_configuration(string scenario)
    {
        var (services, configuration) = NativeHostingWitnessTestServices.Create();
        SetInvalidConfiguration(configuration, scenario);
        NativeHostingWitnessTestServices.Register(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("requires SQLite providers", exception.Message);
    }

    private static void MutateRegistration(IServiceCollection services, string scenario)
    {
        switch (scenario)
        {
            case "missing-placement-backend":
                NativeHostingWitnessTestServices.RemoveBackend(services, ExecutionPlacementStoreBackend.Find(services));
                break;
            case "in-memory-placement-backend":
                ReplacePlacementBackendName(services, ExecutionPlacementStoreBackend.InMemory);
                break;
            case "mismatched-placement-backend":
                NativeHostingWitnessTestServices.AddMismatchedPlacementBackend(services);
                break;
            case "missing-transport-backend":
                NativeHostingWitnessTestServices.RemoveBackend(services, ExecutionCommandTransportBackend.Find(services));
                break;
            case "in-memory-transport-backend":
                ReplaceTransportBackendName(services, ExecutionCommandTransportBackend.InMemory);
                break;
            case "mismatched-transport-backend":
                NativeHostingWitnessTestServices.AddMismatchedCommandTransportBackend(services);
                break;
            case "missing-membership":
                services.RemoveAll<IClusterMembership>();
                break;
            case "duplicate-membership":
                services.AddSingleton<IClusterMembership>(NativeHostingWitnessTestProxy<IClusterMembership>.Create());
                break;
            case "missing-actor-provider":
                services.RemoveAll<IWorkflowExecutionActorProvider>();
                break;
            case "duplicate-actor-provider":
                services.AddSingleton<IWorkflowExecutionActorProvider>(NativeHostingWitnessTestProxy<IWorkflowExecutionActorProvider>.Create());
                break;
            case "missing-placement-service":
                services.RemoveAll<IExecutionPlacementService>();
                break;
            case "duplicate-placement-service":
                services.AddSingleton<IExecutionPlacementService>(NativeHostingWitnessTestProxy<IExecutionPlacementService>.Create());
                break;
            case "missing-ownership":
                services.RemoveAll<IRuntimeExecutionOwnershipService>();
                break;
            case "duplicate-ownership":
                services.Add(ServiceDescriptor.Scoped<IRuntimeExecutionOwnershipService>(
                    _ => NativeHostingWitnessTestProxy<IRuntimeExecutionOwnershipService>.Create()));
                break;
            case "singleton-ownership":
                ReplaceOwnershipLifetime(services, ServiceLifetime.Singleton);
                break;
            case "transient-ownership":
                ReplaceOwnershipLifetime(services, ServiceLifetime.Transient);
                break;
            case "missing-placement-store":
                services.Remove(NativeHostingWitnessTestServices.SingleDescriptor<IExecutionPlacementStore>(services));
                break;
            case "duplicate-placement-store":
                services.Add(ServiceDescriptor.Scoped<IExecutionPlacementStore>(
                    _ => NativeHostingWitnessTestProxy<IExecutionPlacementStore>.Create()));
                break;
            case "singleton-placement-store":
                ReplacePlacementStoreLifetime(services, ServiceLifetime.Singleton);
                break;
            case "transient-placement-store":
                ReplacePlacementStoreLifetime(services, ServiceLifetime.Transient);
                break;
            case "missing-command-transport":
                services.Remove(NativeHostingWitnessTestServices.SingleDescriptor<IExecutionCommandTransport>(services));
                break;
            case "duplicate-command-transport":
                services.Add(ServiceDescriptor.Scoped<IExecutionCommandTransport>(
                    _ => NativeHostingWitnessTestProxy<IExecutionCommandTransport>.Create()));
                break;
            case "singleton-command-transport":
                ReplaceCommandTransportLifetime(services, ServiceLifetime.Singleton);
                break;
            case "transient-command-transport":
                ReplaceCommandTransportLifetime(services, ServiceLifetime.Transient);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown registration test scenario.");
        }
    }

    private static void ReplacePlacementBackendName(IServiceCollection services, string name)
    {
        var descriptor = NativeHostingWitnessTestServices.SingleDescriptor<IExecutionPlacementStore>(services);
        var oldBackend = ExecutionPlacementStoreBackend.Find(services)!;
        NativeHostingWitnessTestServices.RemoveBackend(services, oldBackend);
        ExecutionPlacementStoreBackend.Register(services, new ExecutionPlacementStoreBackend(name, descriptor));
    }

    private static void ReplaceTransportBackendName(IServiceCollection services, string name)
    {
        var descriptor = NativeHostingWitnessTestServices.SingleDescriptor<IExecutionCommandTransport>(services);
        var oldBackend = ExecutionCommandTransportBackend.Find(services)!;
        NativeHostingWitnessTestServices.RemoveBackend(services, oldBackend);
        ExecutionCommandTransportBackend.Register(services, new ExecutionCommandTransportBackend(name, descriptor));
    }

    private static void ReplaceOwnershipLifetime(IServiceCollection services, ServiceLifetime lifetime)
    {
        services.Remove(NativeHostingWitnessTestServices.SingleDescriptor<IRuntimeExecutionOwnershipService>(services));
        services.Add(ServiceDescriptor.Describe(
            typeof(IRuntimeExecutionOwnershipService),
            _ => NativeHostingWitnessTestProxy<IRuntimeExecutionOwnershipService>.Create(),
            lifetime));
    }

    private static void ReplacePlacementStoreLifetime(IServiceCollection services, ServiceLifetime lifetime)
    {
        var replacement = ServiceDescriptor.Describe(
            typeof(IExecutionPlacementStore),
            _ => NativeHostingWitnessTestProxy<IExecutionPlacementStore>.Create(),
            lifetime);
        NativeHostingWitnessTestServices.ReplacePlacementStoreDescriptor(services, replacement);
    }

    private static void ReplaceCommandTransportLifetime(IServiceCollection services, ServiceLifetime lifetime)
    {
        var replacement = ServiceDescriptor.Describe(
            typeof(IExecutionCommandTransport),
            _ => NativeHostingWitnessTestProxy<IExecutionCommandTransport>.Create(),
            lifetime);
        NativeHostingWitnessTestServices.ReplaceCommandTransportDescriptor(services, replacement);
    }

    private static void SetInvalidConfiguration(ConfigurationManager configuration, string scenario)
    {
        switch (scenario)
        {
            case "non-sqlite-provider":
                configuration["CShells:Shells:default:Features:WorkflowsRuntimeEntityFrameworkCore:Provider"] = "SqlServer";
                break;
            case "relative-data-source":
                configuration["CShells:Shells:default:Features:WorkflowsRuntimeEntityFrameworkCore:ConnectionString"] = "Data Source=relative.db";
                break;
            case "relative-lock-folder":
                configuration["CShells:Shells:default:Features:FileSystemDistributedLocking:LocksFolderPath"] = "relative-locks";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown configuration test scenario.");
        }
    }
}
