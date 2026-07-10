using CShells.Features;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Testing;
using Elsa.Http;
using Elsa.Http.Core.Contracts;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Feature-registration coverage for <see cref="ActivitiesHttpFeature"/> (constitution §2.23.1). The HTTP
/// activities are constructed by the runtime's CLR activity constructor, so the feature registers no per-type
/// activity services; it does own the outbound transport (the named client), contributes the
/// <see cref="HttpEndpoint"/> start-trigger's stimulus provider on the W7 seam, and registers the inbound
/// request middleware.
/// </summary>
public sealed class ActivitiesHttpFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersHttpClientFactory_AndNoActivityConstructor()
    {
        var services = new ServiceCollection();

        new ActivitiesHttpFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IHttpClientFactory>());
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IActivityConstructor));
    }

    [Fact]
    public void ConfiguredClient_UsesTheWellKnownClientName()
    {
        var services = new ServiceCollection();
        new ActivitiesHttpFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpActivityConstants.HttpClientName);

        // The activity disables the ambient client timeout and enforces its own via a linked token source.
        Assert.Equal(System.Threading.Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void ContributesHttpEndpointTriggerStimulusProvider_OnTheW7Seam()
    {
        var services = new ServiceCollection();
        new ActivitiesHttpFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var providers = provider.GetServices<IActivityTriggerStimulusProvider>();

        Assert.Contains(providers, p => p is HttpEndpointTriggerStimulusProvider);
    }

    [Fact]
    public void RegistersTheInboundRequestMiddleware()
    {
        var services = new ServiceCollection();
        new ActivitiesHttpFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(HttpEndpointMiddleware));
    }

    [Fact]
    public async Task MountsTheEndpointMiddleware_ThroughTheCShellsMiddlewareSeam()
    {
        // Spec 089 FR-003: enabling the feature is all a host needs — the feature itself is the
        // IMiddlewareShellFeature that mounts HttpEndpointMiddleware into the shell pipeline. Proven
        // behaviorally: a request under the endpoints base path is answered by the mounted middleware
        // (404: no matching trigger) instead of reaching the terminal sentinel.
        var feature = new ActivitiesHttpFeature();
        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        services.AddScoped<IStimulusRouter, RecordingStimulusRouter>();
        // The middleware's route-table/matcher/binding-store dependencies are contributed in production by the
        // features ActivitiesHttpFeature.DependsOn ("Http" provides IRouteTable/IRouteMatcher; "WorkflowsRuntime-
        // Triggers" provides IWorkflowTriggerBindingStore). This bare container stands in for that platform
        // guarantee: the endpoint template is seeded so a request under the base path resolves and reaches
        // dispatch, where the no-start router yields 404 (proving the mounted middleware, not the sentinel,
        // answered).
        services.AddSingleton<IRouteTable>(new FakeRouteTable("orders/webhook"));
        services.AddSingleton<IRouteMatcher, TestRouteMatcher>();
        services.AddSingleton<IWorkflowTriggerBindingStore, Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowTriggerBindingStore>();
        // The middleware also resolves waiting-bookmark options for resume-only matches (spec 089 D) via the
        // cross-execution lookup — contributed in production by WorkflowsRuntimeTriggers (in ActivitiesHttp's
        // DependsOn closure); the bare container stands in for that guarantee with an empty bookmark store.
        services.AddSingleton<IBookmarkStimulusIndex, Elsa.Workflows.Runtime.Core.Services.InMemoryBookmarkStateStore>();
        services.AddSingleton<IGlobalBookmarkStimulusLookup, Elsa.Workflows.Runtime.Core.Services.GlobalBookmarkStimulusLookup>();
        // CShells guarantees an IMiddlewareFactory in every shell container; this bare container stands in for one.
        services.AddSingleton<Microsoft.AspNetCore.Http.IMiddlewareFactory, Microsoft.AspNetCore.Http.MiddlewareFactory>();
        var provider = services.BuildServiceProvider();
        await using var _ = provider;

        var app = new Microsoft.AspNetCore.Builder.ApplicationBuilder(provider);
        ((CShells.AspNetCore.Features.IMiddlewareShellFeature)feature).UseMiddleware(app, environment: null);
        var sentinelReached = false;
        app.Run(_ =>
        {
            sentinelReached = true;
            return Task.CompletedTask;
        });
        var pipeline = app.Build();

        await using var scope = provider.CreateAsyncScope();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Path = "/workflows/http/orders/webhook";
        context.Request.Method = "GET";
        await pipeline(context);

        Assert.False(sentinelReached);
        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public void DeclaresExpectedShellFeatureMetadata()
    {
        var attribute = Assert.Single(
            typeof(ActivitiesHttpFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("ActivitiesHttp", attribute.Name);
    }

    [Fact]
    public void DependsOn_IncludesWorkflowsRuntimeHttp_SoTheRouteTablePopulatorsAlwaysCompose()
    {
        // Contract pin for B2: enabling ActivitiesHttp alone (DependsOn only "Http") left the route table
        // unpopulated — WorkflowsRuntimeHttp contributes the populators (UpdateRouteTableStartupTask,
        // RouteTableTriggerIndexObserver, IHttpEndpointRoutesResolver). The DependsOn closure must include it.
        var attribute = Assert.Single(
            typeof(ActivitiesHttpFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Contains("WorkflowsRuntimeHttp", attribute.DependsOn.Cast<object>().Select(d => d.ToString()));
    }

    [Fact]
    public void DependsOnClosure_ComposesTheRouteTableAndItsPopulators()
    {
        // B2 wiring proof: run the real ConfigureServices of ActivitiesHttp and its DependsOn closure
        // (WorkflowsRuntimeHttp -> Http) plus the minimal runtime registrations those features expect from the
        // rest of the platform, then build the provider and resolve the route table together with the populators
        // that fill it. Before the fix, WorkflowsRuntimeHttp.ConfigureServices threw (C1) and ActivitiesHttp did
        // not depend on it, so this composition — the one a host actually gets — was never exercised.
        var services = new ServiceCollection();

        new ActivitiesHttpFeature().ConfigureServices(services);
        new WorkflowsRuntimeHttpFeature().ConfigureServices(services); // default settings: the C1 path
        new HttpFeature().ConfigureServices(services);

        // Platform-provided dependencies the closure reads at resolve time.
        services.AddSingleton<IWorkflowTriggerBindingStore, Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowTriggerBindingStore>();
        // The route resolver now unions waiting-bookmark templates (spec 089 D) via the cross-execution lookup —
        // contributed in production by WorkflowsRuntimeTriggers; supplied here as the platform stand-in.
        services.AddSingleton<IBookmarkStimulusIndex, Elsa.Workflows.Runtime.Core.Services.InMemoryBookmarkStateStore>();
        services.AddSingleton<IGlobalBookmarkStimulusLookup, Elsa.Workflows.Runtime.Core.Services.GlobalBookmarkStimulusLookup>();
        services.AddMemoryCache();
        services.AddLogging();
        services.AddAuthorizationCore();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IRouteTable>());
        Assert.NotNull(sp.GetRequiredService<IHttpEndpointRoutesResolver>());
        Assert.Contains(sp.GetServices<IStartupTask>(), t => t is UpdateRouteTableStartupTask);
    }
}
