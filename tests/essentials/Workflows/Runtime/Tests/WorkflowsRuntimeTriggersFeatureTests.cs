using CShells.Features;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowsRuntimeTriggersFeatureTests
{
    [Fact]
    public void RegistersTriggerIndexAndStimulusRoutingServices()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);

        AssertRegistered<IWorkflowTriggerBindingStore>(services);
        AssertRegistered<IWorkflowTriggerBindingExtractor>(services);
        AssertRegistered<IWorkflowTriggerIndexer>(services);
        AssertRegistered<IGlobalBookmarkStimulusLookup>(services);
        AssertRegistered<IStimulusRouter>(services);

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(IWorkflowTriggerIndexer)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(IBookmarkStimulusIndex)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(IGlobalBookmarkStimulusLookup)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, x => x.ServiceType == typeof(IStimulusRouter)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, x => x.ServiceType == typeof(IWorkflowTriggerBindingExtractor)).Lifetime);

        // The cross-execution index is bridged onto the bookmark state store via a factory, so assert by service type.
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBookmarkStimulusIndex));
    }

    [Fact]
    public void ResolvesStimulusRouter_WhenComposedWithRuntimeApi()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);
        services.RemoveAll<IBookmarkStateStore>();
        services.AddScoped<IBookmarkStateStore, InMemoryBookmarkStateStore>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstRouter = firstScope.ServiceProvider.GetRequiredService<IStimulusRouter>();
        Assert.NotSame(firstRouter, secondScope.ServiceProvider.GetRequiredService<IStimulusRouter>());
        // The bridged index must resolve to the same store the runtime owns.
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<IBookmarkStateStore>(),
            firstScope.ServiceProvider.GetRequiredService<IBookmarkStimulusIndex>());
        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<IBookmarkStimulusIndex>(),
            secondScope.ServiceProvider.GetRequiredService<IBookmarkStimulusIndex>());
        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<IGlobalBookmarkStimulusLookup>(),
            secondScope.ServiceProvider.GetRequiredService<IGlobalBookmarkStimulusLookup>());
    }

    [Fact]
    public async Task KeyedStartsConverge_OnTheInMemoryDefaultComposition()
    {
        // #2195: a keyed start refuses to run without an IWorkflowExecutionStateStore. The router's feature depends on the
        // runtime API, whose AddWorkflowRuntime always composes the in-memory state store, so the dispatcher it resolves
        // recognizes a redelivery as a duplicate instead of throwing.
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var keyed = KeyedWorkflowStartIdentity.For("delivery-1", "artifact-1");
        var startedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(new WorkflowExecutionState(
            keyed.WorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:artifact"),
            WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: startedAt,
            StartedAt: startedAt,
            UpdatedAt: startedAt,
            CompletedAt: null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>()));

        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                "artifact-1",
                "runtime-test",
                workflowExecutionId: keyed.WorkflowExecutionId,
                idempotencyKey: keyed.StartKey));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, result.CommandDispatch.Status);
    }

    [Fact]
    public void DeclaresRuntimeApiDependencyAndServerRuntime()
    {
        var attribute = Assert.Single(
            typeof(WorkflowsRuntimeTriggersFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsRuntimeTriggers", attribute.Name);
        Assert.Contains("WorkflowsRuntimeApi", attribute.DependsOn.Select(dependency => dependency?.ToString()));
    }

    // TS-1 (§2.23.1): assert single-implementation services by registration presence, not implementation-type or
    // lifetime pinning, so swapping an equivalent implementation no longer trips this test.
    private static void AssertRegistered<TService>(IServiceCollection services) =>
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TService));
}
