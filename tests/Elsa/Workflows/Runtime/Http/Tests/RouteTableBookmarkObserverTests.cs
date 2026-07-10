using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

public sealed class RouteTableBookmarkObserverTests
{
    private readonly InMemoryWorkflowTriggerBindingStore _bindings = new();
    private readonly InMemoryBookmarkStateStore _bookmarks = new();
    private readonly FakeRouteTable _routeTable = new();
    private readonly IServiceProvider _services;

    public RouteTableBookmarkObserverTests()
    {
        // Mirror the trigger-index observer test wiring: a real resolver over real stores, resolved through a fresh
        // scope. The route table's state is shared, so any scope mutates the same instance (production semantics).
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowTriggerBindingStore>(_bindings);
        services.AddSingleton<IBookmarkStimulusIndex>(_bookmarks);
        services.AddSingleton<IGlobalBookmarkStimulusLookup, GlobalBookmarkStimulusLookup>();
        services.AddSingleton<IRouteTable>(_routeTable);
        services.AddScoped<IHttpEndpointRoutesResolver, HttpEndpointRoutesResolver>();
        _services = services.BuildServiceProvider();
    }

    private RouteTableBookmarkObserver Observer() =>
        new(_services.GetRequiredService<IServiceScopeFactory>());

    [Fact]
    public async Task Created_HttpBookmark_AddsRouteToTable()
    {
        // A mid-flow suspension persists the bookmark, then the notifier fires with the committed state.
        var bookmark = Bookmarks.HttpEndpoint("wf1", "callbacks/{id}", "POST");
        await _bookmarks.SaveAsync(bookmark);

        await Observer().OnBookmarkCreatedAsync(bookmark);

        var route = Assert.Single(_routeTable.RouteTemplates);
        Assert.Equal("callbacks/{id}", route);
    }

    [Fact]
    public async Task Consumed_HttpBookmark_RemovesRouteFromTable()
    {
        // The route is live while the bookmark waits; the resume deletes it, then the notifier fires post-commit.
        var bookmark = Bookmarks.HttpEndpoint("wf1", "callbacks/{id}", "POST");
        await _bookmarks.SaveAsync(bookmark);
        await Observer().OnBookmarkCreatedAsync(bookmark);
        Assert.Single(_routeTable.RouteTemplates);

        await _bookmarks.DeleteAsync(bookmark.WorkflowExecutionId, bookmark.BookmarkId);
        await Observer().OnBookmarkConsumedAsync(bookmark);

        Assert.Empty(_routeTable.RouteTemplates);
    }

    [Fact]
    public async Task Consumed_HttpBookmark_KeepsRoute_WhenAnotherInstanceStillWaits()
    {
        // Full re-projection, not incremental: a consumed template survives if another instance still awaits it.
        var wf1 = Bookmarks.HttpEndpoint("wf1", "callbacks/{id}", "POST");
        var wf2 = Bookmarks.HttpEndpoint("wf2", "callbacks/{id}", "POST");
        await _bookmarks.SaveAsync(wf1);
        await _bookmarks.SaveAsync(wf2);
        await Observer().OnBookmarkCreatedAsync(wf1);

        await _bookmarks.DeleteAsync(wf1.WorkflowExecutionId, wf1.BookmarkId);
        await Observer().OnBookmarkConsumedAsync(wf1);

        var route = Assert.Single(_routeTable.RouteTemplates);
        Assert.Equal("callbacks/{id}", route);
    }

    [Fact]
    public async Task NonHttpBookmark_IsIgnored_AndDoesNotTouchTheTable()
    {
        // A pre-existing route proves the observer neither adds nor clears the table for a non-HTTP stimulus type
        // (it returns before opening a scope, so no refresh runs).
        await _routeTable.Add("preexisting/route");
        var other = Bookmarks.Other("wf1", "Event");

        await Observer().OnBookmarkCreatedAsync(other);
        await Observer().OnBookmarkConsumedAsync(other);

        var route = Assert.Single(_routeTable.RouteTemplates);
        Assert.Equal("preexisting/route", route);
    }

    [Fact]
    public async Task Created_UnionsBookmarkRoute_WithExistingTriggerRoutes()
    {
        // The refresh re-projects the whole union: an existing start trigger's route coexists with the new
        // mid-flow bookmark route.
        await _bindings.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));
        var bookmark = Bookmarks.HttpEndpoint("wf1", "callbacks", "POST");
        await _bookmarks.SaveAsync(bookmark);

        await Observer().OnBookmarkCreatedAsync(bookmark);

        Assert.Equal(
            new[] { "callbacks", "orders/{id}" }.OrderBy(x => x),
            _routeTable.RouteTemplates.OrderBy(x => x));
    }

    [Fact]
    public async Task ResolvesScopedServicesFreshly_PerNotification()
    {
        // Each notification opens its own scope: a bookmark saved between two notifications is reflected by the
        // second refresh, proving the resolver is re-resolved and re-run rather than cached from the first scope.
        var first = Bookmarks.HttpEndpoint("wf1", "first", "GET");
        await _bookmarks.SaveAsync(first);
        await Observer().OnBookmarkCreatedAsync(first);
        Assert.Single(_routeTable.RouteTemplates);

        var second = Bookmarks.HttpEndpoint("wf2", "second", "GET");
        await _bookmarks.SaveAsync(second);
        await Observer().OnBookmarkCreatedAsync(second);

        Assert.Equal(
            new[] { "first", "second" }.OrderBy(x => x),
            _routeTable.RouteTemplates.OrderBy(x => x));
    }
}
