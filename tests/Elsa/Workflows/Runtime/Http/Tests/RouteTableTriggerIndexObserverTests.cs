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
    }

    private RouteTableTriggerIndexObserver Observer() =>
        new(_services.GetRequiredService<IServiceScopeFactory>());

    private async Task NotifyAsync(string artifactId)
    {
        var bindings = await _store.ListByArtifactAsync(artifactId);
        await Observer().OnTriggersIndexedAsync(new WorkflowTriggerIndexSnapshot(artifactId, bindings));
    }

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
