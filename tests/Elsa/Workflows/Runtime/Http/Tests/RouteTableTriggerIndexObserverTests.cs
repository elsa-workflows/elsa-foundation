using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Options;
using Elsa.Workflows.Runtime.Http.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

public sealed class RouteTableTriggerIndexObserverTests
{
    private readonly InMemoryWorkflowTriggerBindingStore _store = new();
    private readonly FakeRouteTable _routeTable = new();
    private readonly IServiceProvider _services;

    public RouteTableTriggerIndexObserverTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowTriggerBindingStore>(_store);
        services.AddSingleton<IRouteTable>(_routeTable);
        services.Configure<WorkflowsRuntimeHttpFeatureOptions>(_ => { });
        services.AddLogging();
        services.AddScoped<IHttpEndpointRoutesResolver, HttpEndpointRoutesResolver>();
        _services = services.BuildServiceProvider();
        _observer = Observer();
    }

    private RouteTableTriggerIndexObserver Observer() =>
        new(_services.GetRequiredService<IServiceScopeFactory>());

    // One observer instance across a test's notifications so its per-artifact HTTP-contributor memory persists.
    private readonly RouteTableTriggerIndexObserver _observer;

    private async Task NotifyAsync(string artifactId)
    {
        var bindings = await _store.ListByArtifactAsync(artifactId);
        await _observer.OnTriggersIndexedAsync(new WorkflowTriggerIndexSnapshot(artifactId, bindings));
    }

    private ValueTask NotifyWithAsync(string artifactId, params WorkflowTriggerBinding[] bindings) =>
        _observer.OnTriggersIndexedAsync(new WorkflowTriggerIndexSnapshot(artifactId, bindings));

    [Fact]
    public async Task RefreshesRouteTable_FromTheDurableIndex()
    {
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));

        await NotifyAsync("a1");

        var route = Assert.Single(_routeTable.RouteTemplates);
        Assert.Equal("orders/{id}", route);
    }

    [Fact]
    public async Task Republish_RemovesSupersededRoutes()
    {
        // Two artifacts publish routes; the route table reflects both.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));
        await _store.SaveAsync(Bindings.HttpEndpoint("a2", "n2", "products", "GET"));
        await NotifyAsync("a2");
        Assert.Equal(2, _routeTable.RouteTemplates.Count);

        // Republish a1 through the indexer's delete-and-resave: its route changes orders/{id} -> customers.
        var indexer = new WorkflowTriggerIndexer(
            new StaticExtractor(Bindings.HttpEndpoint("a1", "n1", "customers", "GET")),
            _store,
            [Observer()]);

        await indexer.IndexAsync(FakeExecutable("a1"));

        // The observer's full refresh drops the superseded orders/{id} and keeps products + the new customers.
        Assert.Equal(
            new[] { "customers", "products" }.OrderBy(x => x),
            _routeTable.RouteTemplates.OrderBy(x => x));
    }

    [Fact]
    public async Task NonHttpPublish_NeverSeenAsHttp_SkipsTheRefresh()
    {
        // Spec 089 efficiency #8: a workflow that neither declares nor previously declared an HTTP endpoint cannot
        // change the route set, so the observer must not pay for a full re-projection on its publish.
        await NotifyWithAsync("a1", Bindings.Other("a1", "n1", stimulusType: "Event"));

        Assert.Equal(0, _routeTable.RefreshCount);
        Assert.Empty(_routeTable.RouteTemplates);
    }

    [Fact]
    public async Task HttpPublish_RefreshesTheRouteTable()
    {
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));

        await NotifyAsync("a1");

        Assert.Equal(1, _routeTable.RefreshCount);
    }

    [Fact]
    public async Task ArtifactDropsItsLastHttpRoute_StillRefreshes_ToReconcileTheVanishedRoute()
    {
        // The artifact first publishes an HTTP route (table gets it), then republishes with only a non-HTTP
        // trigger. The new snapshot has no HTTP binding, but the observer remembers the artifact contributed one,
        // so it refreshes to drop the now-superseded route — otherwise the table would serve a stale route.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));
        await NotifyAsync("a1");
        Assert.Single(_routeTable.RouteTemplates);

        // Republish a1 as non-HTTP: its HTTP binding is gone from the durable index.
        await _store.DeleteByArtifactAsync("a1");
        await _store.SaveAsync(Bindings.Other("a1", "n1", stimulusType: "Event"));
        await NotifyAsync("a1");

        Assert.Equal(2, _routeTable.RefreshCount);
        Assert.Empty(_routeTable.RouteTemplates);
    }

    private static WorkflowExecutable FakeExecutable(string artifactId) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, $"def-{artifactId}", "version-1", "1.0.0", $"sha256:{artifactId}"),
            rootActivity: TestNodes.Root(),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            publishedAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>());

    /// <summary>An extractor that ignores the executable and returns a fixed binding set — isolates the observer path.</summary>
    private sealed class StaticExtractor(params WorkflowTriggerBinding[] bindings) : IWorkflowTriggerBindingExtractor
    {
        public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable) => bindings;
    }
}
