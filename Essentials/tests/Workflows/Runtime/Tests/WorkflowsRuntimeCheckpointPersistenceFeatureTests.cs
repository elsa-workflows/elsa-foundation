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
        var modeSetting = featureType.GetProperty(nameof(WorkflowsRuntimeCheckpointPersistenceFeature.Mode))!.CustomAttributes.Single(
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
        Assert.Contains(modeSetting.NamedArguments,
            argument => argument.MemberName == "DefaultValue" && (string?)argument.TypedValue.Value == "Balanced");
        Assert.Contains(featureType.GetProperty(nameof(WorkflowsRuntimeCheckpointPersistenceFeature.MaxActivitiesPerCommit))!.CustomAttributes,
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
        var durableValueReadsSetting = featureType.GetProperty(nameof(WorkflowsRuntimeCheckpointPersistenceFeature.CoalesceDurableValueReads))!.CustomAttributes.Single(
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
        Assert.Contains(durableValueReadsSetting.NamedArguments,
            argument => argument.MemberName == "DefaultValue" && (string?)argument.TypedValue.Value == "true");
    }

    [Fact]
    public void DefaultsToBalancedModeCapFiftyAndDurableReadReuse()
    {
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature();
        var options = new PersistenceModeOptions();

        Assert.Equal(PersistenceMode.Balanced, feature.Mode);
        Assert.Equal(50, feature.MaxActivitiesPerCommit);
        Assert.True(feature.CoalesceDurableValueReads);
        Assert.Equal(PersistenceMode.Balanced, options.Mode);
        Assert.Equal(50, options.MaxActivitiesPerCommit);
        Assert.True(options.CoalesceDurableValueReads);
    }

    [Fact]
    public void TheRuntimeApiAddsThePersistenceModesWithoutTheSettingsFeature()
    {
        // A shell that lists only the runtime API still runs Balanced: CShells calls the API feature's post-configure
        // step after every feature registered its stores, and that step wraps them.
        Assert.True(typeof(IPostConfigureShellServices).IsAssignableFrom(typeof(WorkflowsRuntimeApiFeature)));
        var services = CreateRuntimeServices();
        var selectedProvider = ReplaceCheckpointProvider(services);

        PostConfigureRuntimeApi(services);

        using var provider = services.BuildServiceProvider();
        Assert.Equal(PersistenceMode.Balanced, provider.GetRequiredService<PersistenceModeOptions>().Mode);
        Assert.IsType<CoalescingRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.IsType<CoalescingRuntimeCheckpointCommitStore>(provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.Same(selectedProvider, provider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value);
        Assert.NotNull(provider.GetService<IRuntimeCoalescingDrainScopeFactory>());
        using var scope = provider.CreateScope();
        Assert.Equal(PersistenceMode.Balanced, scope.ServiceProvider.GetRequiredService<IRuntimePersistenceModeResolver>().Resolve(executable: null));
    }

    [Fact]
    public void DurableDefaultStillAddsTheServicesSoAWorkflowCanChooseBalanced()
    {
        var services = CreateRuntimeServices();
        new WorkflowsRuntimeCheckpointPersistenceFeature { Mode = PersistenceMode.Durable }.ConfigureServices(services);
        var selectedProvider = ReplaceCheckpointProvider(services);

        PostConfigureRuntimeApi(services);

        using var provider = services.BuildServiceProvider();
        Assert.Equal(PersistenceMode.Durable, provider.GetRequiredService<PersistenceModeOptions>().Mode);
        Assert.Same(selectedProvider, provider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value);
        Assert.NotNull(provider.GetService<IRuntimeCoalescingDrainScopeFactory>());
        using var scope = provider.CreateScope();
        Assert.Equal(PersistenceMode.Durable, scope.ServiceProvider.GetRequiredService<IRuntimePersistenceModeResolver>().Resolve(executable: null));
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

        PostConfigureRuntimeApi(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CoalescingRuntimeCheckpointPersistencePolicy>(provider.GetRequiredService<IRuntimeCheckpointPersistencePolicy>());
        Assert.Equal(7, provider.GetRequiredService<PersistenceModeOptions>().MaxActivitiesPerCommit);
        Assert.Equal(coalesceDurableValueReads, provider.GetRequiredService<PersistenceModeOptions>().CoalesceDurableValueReads);
        Assert.Same(selectedProvider, provider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value);
    }

    [Fact]
    public void TheSettingsFeatureOptionsWinWhicheverStepRunsFirst()
    {
        // The settings feature registers its options while the shell is composed and the API feature adds the services
        // afterwards; a direct composition may call them the other way round. Either way the configured options stand.
        var settingsFirst = CreateRuntimeServices();
        new WorkflowsRuntimeCheckpointPersistenceFeature { Mode = PersistenceMode.Durable, MaxActivitiesPerCommit = 3 }.ConfigureServices(settingsFirst);
        settingsFirst.AddPersistenceModes();

        var servicesFirst = CreateRuntimeServices();
        servicesFirst.AddPersistenceModes();
        new WorkflowsRuntimeCheckpointPersistenceFeature { Mode = PersistenceMode.Durable, MaxActivitiesPerCommit = 3 }.ConfigureServices(servicesFirst);

        foreach (var services in new[] { settingsFirst, servicesFirst })
        {
            using var provider = services.BuildServiceProvider();
            var options = Assert.Single(provider.GetServices<PersistenceModeOptions>());
            Assert.Equal(PersistenceMode.Durable, options.Mode);
            Assert.Equal(3, options.MaxActivitiesPerCommit);
        }
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
    public void UndefinedModeFailsWhileTheShellIsComposedWithActionableMessage()
    {
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature
        {
            Mode = (PersistenceMode)999
        };

        var exception = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(CreateRuntimeServices()));

        Assert.Contains("WorkflowsRuntimeCheckpointPersistence", exception.Message);
        Assert.Contains("999", exception.Message);
    }

    [Theory]
    [InlineData(PersistenceMode.Balanced, 0)]
    [InlineData(PersistenceMode.Balanced, -1)]
    [InlineData(PersistenceMode.Durable, 0)]
    public void NonPositiveCapFailsWhileTheShellIsComposedInEveryMode(PersistenceMode mode, int cap)
    {
        // A Durable host still runs the workflows that choose Balanced, with this cap, so a bad cap is refused there too.
        var feature = new WorkflowsRuntimeCheckpointPersistenceFeature
        {
            Mode = mode,
            MaxActivitiesPerCommit = cap
        };

        var exception = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(CreateRuntimeServices()));

        Assert.Contains(nameof(WorkflowsRuntimeCheckpointPersistenceFeature.MaxActivitiesPerCommit), exception.Message);
        Assert.Contains("greater than zero", exception.Message);
    }

    [Fact]
    public void RepeatedPostConfigurationDoesNotNestOrDuplicateDecorators()
    {
        var services = CreateRuntimeServices();
        var selectedProvider = ReplaceCheckpointProvider(services);

        PostConfigureRuntimeApi(services);
        PostConfigureRuntimeApi(services);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CoalescingInner<IRuntimeCheckpointCommitStore>));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(PersistenceModeOptions));

        using var provider = services.BuildServiceProvider();
        Assert.Same(selectedProvider, provider.GetRequiredService<CoalescingInner<IRuntimeCheckpointCommitStore>>().Value);
        Assert.IsType<CoalescingRuntimeCheckpointCommitStore>(provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
    }

    private static void PostConfigureRuntimeApi(IServiceCollection services) =>
        new WorkflowsRuntimeApiFeature().PostConfigureServices(services);

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
