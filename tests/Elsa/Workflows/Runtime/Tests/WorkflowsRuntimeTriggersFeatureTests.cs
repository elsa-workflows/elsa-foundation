using CShells.Features;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
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
        AssertRegistered<IStimulusStartDeduplicator>(services);
        AssertRegistered<IStimulusRouter>(services);

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

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IStimulusRouter>());
        // The bridged index must resolve to the same store the runtime owns.
        Assert.Same(
            provider.GetRequiredService<IBookmarkStateStore>(),
            provider.GetRequiredService<IBookmarkStimulusIndex>());
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
