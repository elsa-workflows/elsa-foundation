using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>The kernel's shell feature: what it replaces in a shell, what it leaves out, and what it requires of the host.</summary>
public sealed class KernelFeatureTests
{
    private sealed class LibraryObserver : IWorkflowTriggerIndexObserver
    {
        public ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    /// <summary>A shell's own coordinator, which the feature must not replace without a word.</summary>
    private sealed class CustomCoordinator : IWorkflowActivationCoordinator
    {
        public ValueTask<WorkflowActivationResult> ActivateAsync(WorkflowActivationCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowActivationResult> DeactivateAsync(WorkflowDeactivationCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowActivationResult> EnsureServingAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A shell as the old core composes it, beside the host's kernel serving state.</summary>
    private static ServiceCollection Shell(bool hostComposesKernel = true)
    {
        var services = new ServiceCollection();
        var executables = new InMemoryWorkflowExecutableStore();
        var references = new InMemoryWorkflowExecutableSourceReferenceStore(executables, TimeProvider.System);
        var authority = new InMemoryWorkflowActivationAuthority();
        var bindings = new InMemoryWorkflowTriggerBindingStore();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IWorkflowExecutableStore>(executables);
        services.AddSingleton<IWorkflowExecutableSourceReferenceStore>(references);
        services.AddSingleton<IWorkflowActivationAuthority>(authority);
        services.AddSingleton<IWorkflowTriggerBindingStore>(bindings);
        services.AddSingleton<IWorkflowActivationSwitch>(new InMemoryWorkflowActivationSwitch(authority, references, TimeProvider.System, bindings));
        services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(new WorkflowExecutableRootWriteLeaseManager(executables,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()), TimeProvider.System));
        // The old defaults and an independent publication observer.
        services.TryAddScoped<IWorkflowActivationCoordinator, WorkflowActivationCoordinator>();
        services.TryAddSingleton<IWorkflowTriggerBindingExtractor, WorkflowTriggerBindingExtractor>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowTriggerIndexObserver, LibraryObserver>());
        if (hostComposesKernel)
        {
            services.AddSingleton(new KernelServing());
            services.AddSingleton<IScheduleActors>(new UnavailableScheduleActors("no schedules here"));
        }
        return services;
    }

    [Fact]
    public async Task The_feature_replaces_the_coordinator_and_the_extractor_and_keeps_publication_observers()
    {
        var services = Shell();

        new WorkflowsRuntimeKernelFeature().PostConfigureServices(services);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowActivationCoordinator));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowTriggerBindingExtractor));
        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(LibraryObserver));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        Assert.IsType<KernelActivationCoordinator>(scope.ServiceProvider.GetRequiredService<IWorkflowActivationCoordinator>());
        Assert.IsType<KernelTriggerBindingExtractor>(scope.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingExtractor>());
        Assert.IsType<LibraryObserver>(Assert.Single(scope.ServiceProvider.GetServices<IWorkflowTriggerIndexObserver>()));
    }

    [Fact]
    public void Without_the_feature_the_shell_keeps_the_old_core()
    {
        var services = Shell();

        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(WorkflowActivationCoordinator));
        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(WorkflowTriggerBindingExtractor));
    }

    [Fact]
    public void A_shell_on_a_host_that_composes_no_kernel_is_refused()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeKernelFeature().PostConfigureServices(Shell(hostComposesKernel: false)));

        Assert.Contains("KernelServing", refused.Message, StringComparison.Ordinal);
        Assert.Contains("WorkflowsRuntimeKernelHosting", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shell_with_its_own_coordinator_is_refused_with_it_named()
    {
        var services = Shell();
        services.RemoveAll<IWorkflowActivationCoordinator>();
        services.AddScoped<IWorkflowActivationCoordinator, CustomCoordinator>();

        var refused = Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeKernelFeature().PostConfigureServices(services));

        Assert.Contains(typeof(CustomCoordinator).FullName!, refused.Message, StringComparison.Ordinal);
        Assert.Contains(WorkflowsRuntimeKernelFeature.FeatureName, refused.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IWorkflowActivationCoordinator), refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shell_with_a_coordinator_from_a_factory_is_refused()
    {
        var services = Shell();
        services.AddScoped<IWorkflowActivationCoordinator>(_ => new CustomCoordinator());

        var refused = Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeKernelFeature().PostConfigureServices(services));

        Assert.Contains("a factory", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shell_with_its_own_extractor_is_refused_with_it_named()
    {
        var services = Shell();
        services.RemoveAll<IWorkflowTriggerBindingExtractor>();
        services.AddSingleton<IWorkflowTriggerBindingExtractor>(new KernelTriggerBindingExtractor());

        var refused = Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeKernelFeature().PostConfigureServices(services));

        Assert.Contains(typeof(KernelTriggerBindingExtractor).FullName!, refused.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IWorkflowTriggerBindingExtractor), refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_feature_is_found_in_the_shell_that_composes_it()
    {
        var kernel = Shell();
        new WorkflowsRuntimeKernelFeature().PostConfigureServices(kernel);
        await using var kernelShell = kernel.BuildServiceProvider();
        await using var oldCore = Shell().BuildServiceProvider();

        Assert.True(WorkflowsRuntimeKernelFeature.IsComposedIn(kernelShell));
        Assert.False(WorkflowsRuntimeKernelFeature.IsComposedIn(oldCore));
    }

    [Fact]
    public void The_old_core_types_are_named_by_their_types()
    {
        Assert.Equal(typeof(WorkflowActivationCoordinator).FullName, WorkflowsRuntimeKernelFeature.DefaultCoordinatorTypeName);
        Assert.Equal(typeof(WorkflowTriggerBindingExtractor).FullName, WorkflowsRuntimeKernelFeature.DefaultExtractorTypeName);
    }
}
