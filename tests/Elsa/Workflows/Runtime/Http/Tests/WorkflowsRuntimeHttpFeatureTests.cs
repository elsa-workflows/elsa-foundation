using Elsa.Http.Core.Contracts;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Http.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

/// <summary>
/// Feature-composition coverage for <see cref="WorkflowsRuntimeHttpFeature"/>. The regression under test (C1): the
/// three handler-type settings defaulted to unqualified <c>typeof(X).FullName</c> and were resolved via
/// <c>Type.GetType</c>, which only searches Elsa.Primitives + corelib — so <c>ConfigureServices</c> threw with the
/// DEFAULT settings in any real composition. The fix uses assembly-qualified defaults (and a loaded-assembly
/// fallback in <c>GetLoadedType</c>), so the defaults compose. This test locks that in by building a provider from
/// the default settings and resolving all three reflectively-registered services.
/// </summary>
public sealed class WorkflowsRuntimeHttpFeatureTests
{
    [Fact]
    public void ConfigureServices_WithDefaultSettings_ComposesAllReflectiveHandlers()
    {
        var services = new ServiceCollection();

        // DEFAULT settings — no type-name overrides. This is the path that used to throw.
        new WorkflowsRuntimeHttpFeature().ConfigureServices(services);

        // Minimal dependencies the reflectively-registered handlers need at resolve time. The route resolver also
        // needs the cross-execution bookmark lookup (spec 089 D union) — normally contributed by the
        // WorkflowsRuntimeTriggers dependency; here it is added explicitly since the feature is composed in isolation.
        services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        services.AddSingleton<IBookmarkStimulusIndex, InMemoryBookmarkStateStore>();
        services.AddSingleton<IGlobalBookmarkStimulusLookup, GlobalBookmarkStimulusLookup>();
        services.AddLogging();
        services.AddAuthorizationCore();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IHttpEndpointFaultHandler>());
        Assert.NotNull(sp.GetRequiredService<IHttpEndpointAuthorizationHandler>());
        Assert.NotNull(sp.GetRequiredService<IHttpEndpointRoutesResolver>());
    }

    [Fact]
    public void ConfigureServices_RegistersTheRouteTablePopulators()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeHttpFeature().ConfigureServices(services);

        // The startup task + trigger-index observer are what fill the route table from the durable index; the
        // route resolver is what they read through. ActivitiesHttp depends on this feature precisely so these
        // populators always compose with the endpoint activity (B2). The index validator is the publish-time
        // (template, method) uniqueness gate (issue #592 item 2).
        Assert.Contains(services, d => d.ServiceType == typeof(IStartupTask));
        Assert.Contains(services, d => d.ServiceType == typeof(IWorkflowTriggerIndexObserver));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IWorkflowTriggerIndexValidator) &&
            d.Lifetime == ServiceLifetime.Scoped);
        // The bookmark lifecycle observer keeps the table fresh as mid-flow endpoints suspend/resume (spec 089 D).
        Assert.Contains(services, d => d.ServiceType == typeof(IBookmarkLifecycleObserver));
        Assert.Contains(services, d => d.ServiceType == typeof(IHttpEndpointRoutesResolver));
        // The single serialization point every refresh routes through — registered once as a singleton (review fix).
        var synchronizer = Assert.Single(services, d => d.ServiceType == typeof(IHttpEndpointRouteTableSynchronizer));
        Assert.Equal(ServiceLifetime.Singleton, synchronizer.Lifetime);
    }
}
