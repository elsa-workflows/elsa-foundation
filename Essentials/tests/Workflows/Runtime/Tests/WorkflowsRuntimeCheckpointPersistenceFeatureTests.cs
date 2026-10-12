using CShells.Features;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
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
        Assert.Contains(featureType.GetProperty(nameof(WorkflowsRuntimeCheckpointPersistenceFeature.MaxActivitiesPerCommit))!.CustomAttributes,
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
        var durableValueReadsSetting = featureType.GetProperty(nameof(WorkflowsRuntimeCheckpointPersistenceFeature.CoalesceDurableValueReads))!.CustomAttributes.Single(
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
        Assert.Contains(durableValueReadsSetting.NamedArguments,
            argument => argument.MemberName == "DefaultValue" && (string?)argument.TypedValue.Value == "true");
    }

    [Fact]
    public void DefaultsToDurableModeCapFiftyAndDurableReadReuse()
    {
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature();

        Assert.Equal(PersistenceMode.Durable, feature.Mode);
        Assert.Equal(50, feature.MaxActivitiesPerCommit);
        Assert.True(feature.CoalesceDurableValueReads);
        Assert.True(new PersistenceModeOptions().CoalesceDurableValueReads);
    }

    [Fact]
    public void DurableModeLeavesSelectedProviderUntouched()
    {
        var services = CreateRuntimeServices();
        var selectedProvider = ReplaceCheckpointProvider(services);
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature { CoalesceDurableValueReads = false };

        feature.PostConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ImmediateRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.Same(selectedProvider, provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.Null(provider.GetService<PersistenceModeOptions>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BalancedModeCapturesPostConfiguredProviderAndReadSetting(bool coalesceDurableValueReads)
    {
        var services = CreateRuntimeServices();
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature
        {
            Mode = PersistenceMode.Balanced,
            MaxActivitiesPerCommit = 7,
            CoalesceDurableValueReads = coalesceDurableValueReads
        };
        feature.ConfigureServices(services);
        var selectedProvider = ReplaceCheckpointProvider(services);

        feature.PostConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CoalescingRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.Equal(7, provider.GetRequiredService<PersistenceModeOptions>().MaxActivitiesPerCommit);
        Assert.Equal(coalesceDurableValueReads, provider.GetRequiredService<PersistenceModeOptions>().CoalesceDurableValueReads);
        Assert.Same(selectedProvider, provider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SessionUsesTheHostCapAndBothReadSettings(
        bool coalesceDurableValueReads,
        bool coalesceInspectionReads)
    {
        var services = CreateRuntimeServices();
        services.AddPersistenceModes(options =>
        {
            options.CoalesceInspectionReads = coalesceInspectionReads;
            options.CoalesceDurableValueReads = coalesceDurableValueReads;
        });

        using var provider = services.BuildServiceProvider();
        var hostOptions = provider.GetRequiredService<PersistenceModeOptions>();
        var factory = provider.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>();
        await using var scope = factory.Begin("wf-host-settings");

        Assert.Equal(hostOptions.MaxActivitiesPerCommit, scope.Session.MaxActivitiesPerCommit);
        Assert.Equal(coalesceDurableValueReads, scope.Session.CoalesceDurableValueReads);
        Assert.Equal(coalesceInspectionReads, scope.Session.CoalesceInspectionReads);
        Assert.Equal(coalesceDurableValueReads, hostOptions.CoalesceDurableValueReads);
        Assert.Equal(coalesceInspectionReads, hostOptions.CoalesceInspectionReads);
    }

    [Fact]
    public void UndefinedModeFailsDuringPostConfigurationWithActionableMessage()
    {
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature
        {
            Mode = (PersistenceMode)999
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
    public void NonPositiveBalancedCapFailsDuringPostConfiguration(int cap)
    {
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature
        {
            Mode = PersistenceMode.Balanced,
            MaxActivitiesPerCommit = cap
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            feature.PostConfigureServices(CreateRuntimeServices());
        });

        Assert.Contains(nameof(WorkflowsRuntimeCheckpointPersistenceFeature.MaxActivitiesPerCommit), exception.Message);
        Assert.Contains("greater than zero", exception.Message);
    }

    [Fact]
    public void RepeatedPostConfigurationDoesNotNestOrDuplicateDecorators()
    {
        var services = CreateRuntimeServices();
        var selectedProvider = ReplaceCheckpointProvider(services);
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature
        {
            Mode = PersistenceMode.Balanced
        };

        feature.PostConfigureServices(services);
        feature.PostConfigureServices(services);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CoalescingInner<IRuntimeCheckpointCommitStore>));

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
