using CShells.Features;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowsRuntimeCheckpointPersistenceFeatureTests
{
    [Fact]
    public void DeclaresExpectedMetadataAndOperatorSettings()
    {
        var featureType = typeof(WorkflowsRuntimeCheckpointPersistenceFeature);
        var featureAttribute = Assert.Single(
            featureType.GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsRuntimeCheckpointPersistence", featureAttribute.Name);
        Assert.Contains("WorkflowsRuntimeApi", featureAttribute.DependsOn.Select(dependency => dependency?.ToString()));
        Assert.Contains(featureType.GetProperty(nameof(WorkflowsRuntimeCheckpointPersistenceFeature.Mode))!.CustomAttributes,
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
        Assert.Contains(featureType.GetProperty(nameof(WorkflowsRuntimeCheckpointPersistenceFeature.MaxSegmentCheckpoints))!.CustomAttributes,
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
    }

    [Fact]
    public void DefaultsToImmediateModeAndCapFifty()
    {
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature();

        Assert.Equal(CheckpointPersistenceMode.Immediate, feature.Mode);
        Assert.Equal(50, feature.MaxSegmentCheckpoints);
    }

    [Fact]
    public void ImmediateModeLeavesSelectedProviderUntouched()
    {
        var services = CreateRuntimeServices();
        var selectedProvider = ReplaceCheckpointProvider(services);
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature();

        feature.PostConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ImmediateRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.Same(selectedProvider, provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
    }

    [Fact]
    public void CoalescedModeCapturesPostConfiguredProviderAndAppliesConfiguredCap()
    {
        var services = CreateRuntimeServices();
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature
        {
            Mode = CheckpointPersistenceMode.Coalesced,
            MaxSegmentCheckpoints = 7
        };
        feature.ConfigureServices(services);
        var selectedProvider = ReplaceCheckpointProvider(services);

        feature.PostConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CoalescingRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.Equal(7, provider.GetRequiredService<CoalescingRuntimeCheckpointPersistenceOptions>().MaxSegmentCheckpoints);
        Assert.Same(selectedProvider, provider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value);
    }

    [Fact]
    public void Coalesced_mode_fails_closed_when_the_selected_durable_provider_lacks_the_prepared_ledger_capability()
    {
        var services = CreateRuntimeServices();
        ReplaceCheckpointProvider(services, new NonPreparedLedgerCheckpointStore());
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature
        {
            Mode = CheckpointPersistenceMode.Coalesced
        };

        var exception = Assert.Throws<InvalidOperationException>(() => feature.PostConfigureServices(services));

        Assert.Contains(nameof(IRuntimeCheckpointPreparedLedgerStore), exception.Message);
        Assert.Contains(nameof(CheckpointPersistenceMode.Coalesced), exception.Message);
    }

    [Fact]
    public void UndefinedModeFailsDuringPostConfigurationWithActionableMessage()
    {
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature
        {
            Mode = (CheckpointPersistenceMode)999
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            feature.PostConfigureServices(CreateRuntimeServices());
        });

        Assert.Contains("WorkflowsRuntimeCheckpointPersistence", exception.Message);
        Assert.Contains("999", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveCoalescedCapFailsDuringPostConfiguration(int cap)
    {
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature
        {
            Mode = CheckpointPersistenceMode.Coalesced,
            MaxSegmentCheckpoints = cap
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            feature.PostConfigureServices(CreateRuntimeServices());
        });

        Assert.Contains(nameof(WorkflowsRuntimeCheckpointPersistenceFeature.MaxSegmentCheckpoints), exception.Message);
        Assert.Contains("greater than zero", exception.Message);
    }

    [Fact]
    public void RepeatedPostConfigurationDoesNotNestOrDuplicateDecorators()
    {
        var services = CreateRuntimeServices();
        var selectedProvider = ReplaceCheckpointProvider(services);
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature
        {
            Mode = CheckpointPersistenceMode.Coalesced
        };

        feature.PostConfigureServices(services);
        feature.PostConfigureServices(services);

        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(CoalescingInner<IRuntimeCheckpointCommitStore>)));

        using var provider = services.BuildServiceProvider();
        Assert.Same(selectedProvider, provider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value);
        Assert.IsType<CoalescingRuntimeCheckpointCommitStore>(provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
    }

    [Fact]
    public async Task Recovery_services_are_explicit_and_the_resolver_observes_durable_work_hidden_by_the_overlay()
    {
        var resolverContract = RecoveryAuthorityTestContract.RequiredType(
            "Elsa.Workflows.Runtime.Core.Contracts.IRuntimeSchedulerWorkItemResolver");
        var accessorContract = RecoveryAuthorityTestContract.AccessorContractType;
        var routerType = typeof(RuntimeCheckpointCommitter).Assembly.GetType(
            "Elsa.Workflows.Runtime.Core.Services.RuntimeCheckpointRecoveryRouter",
            throwOnError: false) ?? throw new InvalidOperationException(
            "Required Path A router type 'Elsa.Workflows.Runtime.Core.Services.RuntimeCheckpointRecoveryRouter' is missing.");
        var services = CreateRuntimeServices();
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature { Mode = CheckpointPersistenceMode.Coalesced };
        feature.ConfigureServices(services);
        feature.PostConfigureServices(services);

        Assert.Single(services, descriptor => descriptor.ServiceType == accessorContract);
        Assert.Single(services, descriptor => descriptor.ServiceType == routerType);
        Assert.Single(services, descriptor => descriptor.ServiceType == resolverContract);

        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService(resolverContract);
        Assert.True(resolverContract.IsInstanceOfType(resolver));

        var durableQueue = provider.GetRequiredService<CoalescingInner<IWorkflowSchedulerWorkQueue>>().Value;
        var overlayQueue = provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var accessor = provider.GetRequiredService<IRuntimeCoalescingSessionAccessor>();
        var item = AuthorityWorkItem.Sample().ToRuntimeWorkItem();
        var session = new RuntimeCoalescingSession(
            item.WorkflowExecutionId,
            durableQueue,
            provider.GetRequiredService<CoalescingRuntimeCheckpointPersistenceOptions>());

        using (accessor.Push(session))
        {
            Assert.Empty((await overlayQueue.ListAsync(new RuntimeSchedulerWorkQuery(item.WorkflowExecutionId))).Items);
            await durableQueue.EnqueueAsync(item);
            Assert.Empty((await overlayQueue.ListAsync(new RuntimeSchedulerWorkQuery(item.WorkflowExecutionId))).Items);

            var resolution = await InvokeResolverAsync(resolverContract, resolver, RecoveryAuthorityTestContract.Encode(item));
            Assert.Equal("Exact", resolution.GetType().GetProperty("Status")?.GetValue(resolution)?.ToString());
            Assert.Equal(item, resolution.GetType().GetProperty("WorkItem")?.GetValue(resolution));
        }
    }

    private static async Task<object> InvokeResolverAsync(Type contract, object resolver, object authority)
    {
        var method = Assert.Single(contract.GetMethods());
        var arguments = method.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == RecoveryAuthorityTestContract.AuthorityType)
                return authority;
            if (parameter.ParameterType == typeof(CancellationToken))
                return CancellationToken.None;
            Assert.Fail($"Unexpected required resolver argument '{parameter.Name}:{parameter.ParameterType.FullName}'.");
            return null;
        }).ToArray();
        var awaitable = method.Invoke(resolver, arguments)!;
        var task = (Task)awaitable.GetType().GetMethod("AsTask")!.Invoke(awaitable, null)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static ServiceCollection CreateRuntimeServices()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        return services;
    }

    private static InMemoryRuntimeCheckpointCommitStore ReplaceCheckpointProvider(IServiceCollection services)
    {
        var selectedProvider = new InMemoryRuntimeCheckpointCommitStore();
        ReplaceCheckpointProvider(services, selectedProvider);
        return selectedProvider;
    }

    private static void ReplaceCheckpointProvider(IServiceCollection services, IRuntimeCheckpointCommitStore selectedProvider)
    {
        services.RemoveAll<IRuntimeCheckpointCommitStore>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(selectedProvider);
    }

    private sealed class NonPreparedLedgerCheckpointStore : IRuntimeCheckpointCommitStore
    {
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
