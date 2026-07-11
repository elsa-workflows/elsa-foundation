using CShells.Features;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Contracts;
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

    private static ServiceCollection CreateRuntimeServices()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        return services;
    }

    private static InMemoryRuntimeCheckpointCommitStore ReplaceCheckpointProvider(IServiceCollection services)
    {
        var selectedProvider = new InMemoryRuntimeCheckpointCommitStore();
        services.RemoveAll<IRuntimeCheckpointCommitStore>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(selectedProvider);
        return selectedProvider;
    }
}
