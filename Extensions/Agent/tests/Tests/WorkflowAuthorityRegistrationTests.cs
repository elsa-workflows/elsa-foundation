using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>Startup inspects the current shell's metadata, even when shell registrations were copied.</summary>
public sealed class WorkflowAuthorityRegistrationTests
{
    private const string ValidatorName = "WorkflowDefinitionAuthorizationStartupTask";

    [Fact]
    public async Task A_duplicate_in_the_original_collection_is_rejected_before_startup()
    {
        var services = Configure();
        services.AddSingleton<IWorkflowDefinitionAuthorizer>(new Owner());
        using var provider = services.BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Validator(services, provider).ExecuteAsync(CancellationToken.None));

        Assert.Contains(nameof(IWorkflowDefinitionAuthorizer), error.Message);
        Assert.Contains("multiple registrations", error.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_copied_child_checks_its_own_duplicates_without_mutating_parent_metadata(int depth)
    {
        var parent = Configure();
        var originalParent = parent.ToArray();
        IServiceCollection child = parent;
        for (var i = 0; i < depth; i++) child = CopyAndConfigure(child);
        child.AddSingleton<IWorkflowDefinitionAuthorizer>(new Owner());
        using var parentProvider = parent.BuildServiceProvider();
        using var childProvider = child.BuildServiceProvider();
        // An inherited application-wide metadata registration must not determine this validator.
        Assert.Same(parent, childProvider.GetRequiredService<IServiceCollection>());
        await Validator(parent, parentProvider).ExecuteAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Validator(child, childProvider).ExecuteAsync(CancellationToken.None));

        Assert.Contains(nameof(IWorkflowDefinitionAuthorizer), error.Message);
        Assert.Equal(originalParent, parent.ToArray());
        Assert.Single(parent, item => item.ServiceType == typeof(IWorkflowDefinitionAuthorizer));
    }

    [Fact]
    public async Task A_child_can_select_one_authority_without_inheriting_its_parents_duplicate_failure()
    {
        var parent = Configure();
        parent.AddSingleton<IWorkflowDefinitionAuthorizer>(new Owner());
        var child = CopyAndConfigure(parent);
        child.RemoveAll<IWorkflowDefinitionAuthorizer>();
        child.AddSingleton<IWorkflowDefinitionAuthorizer>(new Owner());
        using var parentProvider = parent.BuildServiceProvider();
        using var childProvider = child.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Validator(parent, parentProvider).ExecuteAsync(CancellationToken.None));

        await Validator(child, childProvider).ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, parent.Count(item => item.ServiceType == typeof(IWorkflowDefinitionAuthorizer)));
        Assert.Single(child, item => item.ServiceType == typeof(IWorkflowDefinitionAuthorizer));
    }

    [Fact]
    public async Task Repeated_child_configuration_keeps_one_current_startup_validator()
    {
        var parent = Configure();
        var originalParent = parent.ToArray();
        var child = CopyAndConfigure(parent);
        new WorkflowsDesignApiFeature().ConfigureServices(child);
        new WorkflowsDesignApiFeature().ConfigureServices(child);
        using var provider = child.BuildServiceProvider();

        await Validator(child, provider).ExecuteAsync(CancellationToken.None);

        Assert.Single(child, IsValidator);
        Assert.Single(child, item => item.ServiceType == typeof(IWorkflowDefinitionAuthorizer));
        Assert.Equal(originalParent, parent.ToArray());
        child.AddSingleton<IWorkflowDefinitionAuthorizer>(new Owner());
        using var duplicateProvider = child.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Validator(child, duplicateProvider).ExecuteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reconfiguration_removes_a_copied_legacy_type_registration_for_the_same_validator()
    {
        var services = Configure();
        var current = Assert.Single(services, IsValidator);
        var validatorType = current.ImplementationInstance?.GetType() ?? current.ImplementationType!;
        services.Add(ServiceDescriptor.Scoped(typeof(IStartupTask), validatorType));

        new WorkflowsDesignApiFeature().ConfigureServices(services);

        Assert.Single(services, IsValidator);
        using var provider = services.BuildServiceProvider();
        await Validator(services, provider).ExecuteAsync(CancellationToken.None);
    }

    private static IServiceCollection Configure()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        new WorkflowsDesignApiFeature().ConfigureServices(services);
        return services;
    }

    private static IServiceCollection CopyAndConfigure(IServiceCollection parent)
    {
        IServiceCollection child = new ServiceCollection();
        foreach (var descriptor in parent) child.Add(descriptor);
        new WorkflowsDesignApiFeature().ConfigureServices(child);
        return child;
    }

    private static bool IsValidator(ServiceDescriptor descriptor) => descriptor.ServiceType == typeof(IStartupTask)
        && (descriptor.ImplementationInstance?.GetType().Name == ValidatorName || descriptor.ImplementationType?.Name == ValidatorName);

    private static IStartupTask Validator(IServiceCollection registrations, IServiceProvider provider)
    {
        var descriptor = Assert.Single(registrations, IsValidator);
        return descriptor.ImplementationInstance is IStartupTask instance ? instance
            : Assert.IsAssignableFrom<IStartupTask>(ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
    }

    private sealed class Owner : IWorkflowDefinitionAuthorizer
    {
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }
}
