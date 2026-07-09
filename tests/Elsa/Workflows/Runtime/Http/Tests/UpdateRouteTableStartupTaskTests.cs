using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Http.Services;
using Elsa.Workflows.Runtime.Http.Tasks;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

public sealed class UpdateRouteTableStartupTaskTests
{
    private readonly InMemoryWorkflowTriggerBindingStore _store = new();
    private readonly FakeRouteTable _routeTable = new();

    private UpdateRouteTableStartupTask Task() =>
        new(new HttpEndpointRoutesResolver(_store), _routeTable);

    [Fact]
    public async Task PopulatesRouteTable_FromBindings()
    {
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));
        await _store.SaveAsync(Bindings.HttpEndpoint("a2", "n2", "products", "POST"));

        await Task().ExecuteAsync(CancellationToken.None);

        Assert.Equal(
            new[] { "orders/{id}", "products" }.OrderBy(x => x),
            _routeTable.RouteTemplates.OrderBy(x => x));
    }

    [Fact]
    public async Task Refresh_ReplacesAnyPreexistingRoutes()
    {
        await _routeTable.Add("stale/route");
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders", "GET"));

        await Task().ExecuteAsync(CancellationToken.None);

        var route = Assert.Single(_routeTable.RouteTemplates);
        Assert.Equal("orders", route);
    }
}
