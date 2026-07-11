using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Http.Contracts;
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
        // The resolver unions waiting-bookmark templates via the expiry-aware lookup (spec 089 D); an empty
        // in-memory bookmark store keeps these trigger-index observer tests focused on the trigger path.
        services.AddSingleton<IBookmarkStimulusIndex>(new InMemoryBookmarkStateStore());
        services.AddSingleton<IGlobalBookmarkStimulusLookup, GlobalBookmarkStimulusLookup>();
        services.AddLogging();
        services.AddScoped<IHttpEndpointRoutesResolver, HttpEndpointRoutesResolver>();
        // The observer now delegates the read-then-swap to the shared synchronizer (spec 089 D review fix); wire the
        // real one so the refresh path (and the FailNextRefresh propagation) still runs through it.
        services.AddSingleton<IHttpEndpointRouteTableSynchronizer, HttpEndpointRouteTableSynchronizer>();
        _services = services.BuildServiceProvider();
        _observer = Observer();
    }

    private RouteTableTriggerIndexObserver Observer() =>
        new(_services.GetRequiredService<IHttpEndpointRouteTableSynchronizer>());

    // One observer instance across a test's notifications so its known-non-HTTP artifact memory persists.
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
    public async Task RepeatNonHttpPublish_SkipsTheRefresh_AfterOneRefreshEstablishedItContributesNothing()
    {
        // Spec 089 efficiency #8: the FIRST no-HTTP publish of an unknown artifact refreshes (the observer cannot
        // know the artifact did not just drop its last HTTP route); once that refresh succeeded, the artifact is
        // positively known non-HTTP and repeat no-HTTP publishes — the common case — skip the re-projection.
        await NotifyWithAsync("a1", Bindings.Other("a1", "n1", stimulusType: "Event"));
        Assert.Equal(1, _routeTable.RefreshCount);

        await NotifyWithAsync("a1", Bindings.Other("a1", "n1", stimulusType: "Event"));
        await NotifyWithAsync("a1", Bindings.Other("a1", "n1", stimulusType: "Event"));

        Assert.Equal(1, _routeTable.RefreshCount);
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
    public async Task HttpPublish_OfAKnownNonHttpArtifact_RefreshesAgain()
    {
        // A known-non-HTTP artifact that later declares an HTTP endpoint must refresh (and be forgotten as
        // non-HTTP, so a subsequent removal refreshes too).
        await NotifyWithAsync("a1", Bindings.Other("a1", "n1", stimulusType: "Event"));
        Assert.Equal(1, _routeTable.RefreshCount);

        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));
        await NotifyAsync("a1");

        Assert.Equal(2, _routeTable.RefreshCount);
        Assert.Single(_routeTable.RouteTemplates);
    }

    [Fact]
    public async Task ArtifactDropsItsLastHttpRoute_StillRefreshes_ToReconcileTheVanishedRoute()
    {
        // The artifact first publishes an HTTP route (table gets it), then republishes with only a non-HTTP
        // trigger. The new snapshot has no HTTP binding and the artifact is not known non-HTTP, so the observer
        // refreshes and drops the now-superseded route — otherwise the table would serve a stale route.
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

    [Fact]
    public async Task RestartThenDropLastHttpRoute_FreshObserverStillRefreshes_NoStaleRouteSurvives()
    {
        // Restart-removal sequence (control-room review of #599): before the restart, a1's HTTP route was
        // published and the (restarted) process rebuilt the table from the durable index via the startup task —
        // so the route is live but the observer instance is FRESH and has no memory of a1. a1 then republishes
        // without its HTTP trigger. A fresh observer must refresh (unknown artifact), reconciling the vanished
        // route out; skipping here would serve the stale route forever.
        await _routeTable.Refresh(new[] { "orders/{id}" }); // The startup task's rebuild, pre-republish.
        var refreshesBeforeRepublish = _routeTable.RefreshCount;
        await _store.SaveAsync(Bindings.Other("a1", "n1", stimulusType: "Event")); // The post-republish durable index.

        var freshObserver = Observer();
        var bindings = await _store.ListByArtifactAsync("a1");
        await freshObserver.OnTriggersIndexedAsync(new WorkflowTriggerIndexSnapshot("a1", bindings));

        Assert.Equal(refreshesBeforeRepublish + 1, _routeTable.RefreshCount);
        Assert.Empty(_routeTable.RouteTemplates);
    }

    [Fact]
    public async Task FailedRefresh_LeavesNoMemory_SoTheRetriedPublishRefreshesAgain()
    {
        // The observer failure fails the publish. The known-non-HTTP set must be written only AFTER a successful
        // refresh — otherwise the retried publish would skip and any stale route would persist.
        _routeTable.FailNextRefresh = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NotifyWithAsync("a1", Bindings.Other("a1", "n1", stimulusType: "Event")));
        Assert.Equal(0, _routeTable.RefreshCount);

        // The publish retry: same snapshot, must attempt the refresh again (and only now record the artifact).
        await NotifyWithAsync("a1", Bindings.Other("a1", "n1", stimulusType: "Event"));
        Assert.Equal(1, _routeTable.RefreshCount);

        // Subsequent repeats skip as usual.
        await NotifyWithAsync("a1", Bindings.Other("a1", "n1", stimulusType: "Event"));
        Assert.Equal(1, _routeTable.RefreshCount);
    }

    private static WorkflowExecutable FakeExecutable(string artifactId) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, $"def-{artifactId}", "version-1", "1.0.0", $"sha256:{artifactId}"),
            rootActivity: TestNodes.Root(),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UnixEpoch,
            compatibilityMetadata: new Dictionary<string, string>());

    /// <summary>An extractor that ignores the executable and returns a fixed binding set — isolates the observer path.</summary>
    private sealed class StaticExtractor(params WorkflowTriggerBinding[] bindings) : IWorkflowTriggerBindingExtractor
    {
        public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable) => bindings;
    }
}
