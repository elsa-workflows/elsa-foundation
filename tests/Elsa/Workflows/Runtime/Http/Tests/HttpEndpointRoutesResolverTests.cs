using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Http.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

public sealed class HttpEndpointRoutesResolverTests
{
    private readonly InMemoryWorkflowTriggerBindingStore _store = new();

    // The resolver projects endpoint-relative templates only — the endpoints base path is a middleware concern
    // (HttpEndpointMiddleware strips it before consulting the route table), so it takes no base-path option.
    private HttpEndpointRoutesResolver Resolver() => new(_store, NullLogger<HttpEndpointRoutesResolver>.Instance);

    [Fact]
    public async Task ProjectsDistinctTemplates_FromHttpBindingMetadata()
    {
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));
        await _store.SaveAsync(Bindings.HttpEndpoint("a2", "n2", "products", "POST"));

        var routes = await Resolver().ResolveRoutesAsync();

        Assert.Equal(
            new[] { "orders/{id}", "products" }.OrderBy(x => x),
            routes.Select(r => r.Route).OrderBy(x => x));
    }

    [Fact]
    public async Task DedupesTemplate_SharedByMultipleMethodBindings()
    {
        // One endpoint, two methods → two bindings, same template. The route table wants one route.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "DELETE"));

        var routes = await Resolver().ResolveRoutesAsync();

        var route = Assert.Single(routes);
        Assert.Equal("orders/{id}", route.Route);
    }

    [Fact]
    public async Task IgnoresNonHttpStimulusTypes()
    {
        await _store.SaveAsync(Bindings.Other("a1", "n1", "Event"));
        await _store.SaveAsync(Bindings.Other("a2", "n2", "Timer"));
        await _store.SaveAsync(Bindings.HttpEndpoint("a3", "n3", "orders", "GET"));

        var routes = await Resolver().ResolveRoutesAsync();

        var route = Assert.Single(routes);
        Assert.Equal("orders", route.Route);
    }

    [Fact]
    public async Task IgnoresHttpBindings_MissingTemplateKey()
    {
        // An HTTP-typed binding with no http:template metadata contributes no route.
        await _store.SaveAsync(Bindings.Build(
            "a1", "n1", Elsa.Http.Core.HttpEndpointRouting.StimulusType, "sha256:x",
            new Dictionary<string, string>(StringComparer.Ordinal)));
        await _store.SaveAsync(Bindings.HttpEndpoint("a2", "n2", "orders", "GET"));

        var routes = await Resolver().ResolveRoutesAsync();

        var route = Assert.Single(routes);
        Assert.Equal("orders", route.Route);
    }

    [Fact]
    public async Task StoresTemplatesEndpointRelative_NeverBasePathPrefixed()
    {
        // The resolver never prefixes the endpoints base path (that is the middleware's job) — the template is
        // stored exactly as authored/normalized.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));

        var routes = await Resolver().ResolveRoutesAsync();

        var route = Assert.Single(routes);
        Assert.Equal("orders/{id}", route.Route);
    }

    [Fact]
    public async Task EmptyStore_YieldsNoRoutes()
    {
        var routes = await Resolver().ResolveRoutesAsync();
        Assert.Empty(routes);
    }

    // ---- Issue #592 item 2: conflicts degrade at the resolver (uniqueness is enforced pre-write by the
    // validator; a poisoned store must not brick startup or unrelated publishes) ----

    [Fact]
    public async Task CrossDefinitionConflictInTheStore_WarnsButStillResolvesRoutes()
    {
        // Two DISTINCT definitions claim GET orders/{id} — a state the pre-write validator normally prevents,
        // reachable only out-of-band. The resolver must NOT throw (it also runs at shell startup and on every
        // observed publish); it resolves the routes and leaves the conflict to the request-time 409 backstop.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET", definitionId: "def-a"));
        await _store.SaveAsync(Bindings.HttpEndpoint("a2", "n2", "orders/{id}", "GET", definitionId: "def-b"));

        var routes = await Resolver().ResolveRoutesAsync();

        var route = Assert.Single(routes);
        Assert.Equal("orders/{id}", route.Route);
    }

    [Fact]
    public async Task SameDefinitionOwningBothBindings_ResolvesToOneRoute()
    {
        // Same definition, same (template, method) on two nodes (republish remnants / a duplicate node) — not an
        // authoring conflict, resolves to one route.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET", definitionId: "def-a"));
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n2", "orders/{id}", "GET", definitionId: "def-a"));

        var routes = await Resolver().ResolveRoutesAsync();

        var route = Assert.Single(routes);
        Assert.Equal("orders/{id}", route.Route);
    }
}
