using Elsa.Activities.Design.Core.Models;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class ExpressionToolingCompositionTests
{
    [Fact]
    public void Duplicate_operations_fail_configuration_without_constructing_request_services()
    {
        var services = new ServiceCollection();
        AddOperations(services);
        AddOperations(services);
        var error = Assert.Throws<InvalidOperationException>(() => new WorkflowsDesignApiFeature().ConfigureServices(services));
        Assert.Contains("IExpressionToolingOperations has multiple registrations", error.Message);
    }

    [Fact]
    public async Task Late_duplicate_operations_fail_startup_but_optional_absence_is_valid()
    {
        var services = new ServiceCollection();
        new WorkflowsDesignApiFeature().ConfigureServices(services);
        AddOperations(services);
        var task = OperationsValidator(services);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task.ExecuteAsync(CancellationToken.None));
        Assert.Contains("IExpressionToolingOperations has multiple registrations", error.Message);
        services.RemoveAll<IExpressionToolingOperations>();
        await task.ExecuteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Copied_child_shell_rebinds_operations_validation_to_its_own_collection()
    {
        var parent = new ServiceCollection();
        new WorkflowsDesignApiFeature().ConfigureServices(parent);
        var child = new ServiceCollection();
        foreach (var descriptor in parent) ((IServiceCollection)child).Add(descriptor);
        new WorkflowsDesignApiFeature().ConfigureServices(child);
        new WorkflowsDesignApiFeature().ConfigureServices(child);
        AddOperations(parent);
        await OperationsValidator(child).ExecuteAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => OperationsValidator(parent).ExecuteAsync(CancellationToken.None));
        AddOperations(child);
        await Assert.ThrowsAsync<InvalidOperationException>(() => OperationsValidator(child).ExecuteAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("duplicate")]
    public async Task Invalid_strategy_keys_fail_scoped_startup_without_projecting_payload(string key)
    {
        var services = new ServiceCollection();
        new WorkflowsDesignApiFeature().ConfigureServices(services);
        services.AddScoped<IActivityDraftExpressionGraphProjection>(_ => new Projection(key));
        if (key == "duplicate") services.AddScoped<IActivityDraftExpressionGraphProjection>(_ => new Projection(key));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var task = ProjectionValidator(scope.ServiceProvider);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task.ExecuteAsync(CancellationToken.None));
        Assert.Contains(key == "duplicate" ? "duplicate provider key" : "blank provider key", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_distinct_ordinal_strategies_are_valid_without_projecting_payload(bool registerStrategies)
    {
        var services = new ServiceCollection();
        new WorkflowsDesignApiFeature().ConfigureServices(services);
        if (registerStrategies)
        {
            services.AddScoped<IActivityDraftExpressionGraphProjection>(_ => new Projection("graph"));
            services.AddScoped<IActivityDraftExpressionGraphProjection>(_ => new Projection("Graph"));
        }
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        await ProjectionValidator(scope.ServiceProvider).ExecuteAsync(CancellationToken.None);
    }

    private static void AddOperations(IServiceCollection services) => services.AddScoped<IExpressionToolingOperations>(_ =>
        throw new Xunit.Sdk.XunitException("Startup validation must not construct optional request services."));

    private static IStartupTask OperationsValidator(IServiceCollection services) => services
        .Where(descriptor => descriptor.ServiceType == typeof(IStartupTask)).Select(descriptor => descriptor.ImplementationInstance)
        .OfType<IStartupTask>().Single(task => task.GetType().Name == "ExpressionToolingStartupTask");

    private static IStartupTask ProjectionValidator(IServiceProvider services)
    {
        // Resolve only this task: unrelated module startup tasks require their own feature dependencies.
        var descriptor = services.GetRequiredService<IServiceCollection>()
            .Single(item => item.ServiceType == typeof(IStartupTask) && item.ImplementationType?.Name == "ValidateActivityDraftExpressionGraphProjectionsStartupTask");
        return (IStartupTask)ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!);
    }

    private sealed class Projection(string key) : IActivityDraftExpressionGraphProjection
    {
        public string ProviderKey => key;
        public WorkflowDefinitionState? TryProject(ActivityDefinitionDraftState state) =>
            throw new Xunit.Sdk.XunitException("Key validation must never project an authored payload.");
    }
}
