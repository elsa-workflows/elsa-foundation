using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Http.Options;
using Elsa.Workflows.Runtime.Http.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

public sealed class HttpEndpointRoutesResolverTests
{
    private readonly InMemoryWorkflowTriggerBindingStore _store = new();

    private HttpEndpointRoutesResolver Resolver(string basePath = "") =>
        new(_store, Microsoft.Extensions.Options.Options.Create(new WorkflowsRuntimeHttpFeatureOptions { BasePath = basePath }));

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
    public async Task PrefixesBasePath_WhenConfigured()
    {
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));

        var routes = await Resolver(basePath: "workflows/http").ResolveRoutesAsync();

        var route = Assert.Single(routes);
        Assert.Equal("workflows/http/orders/{id}", route.Route);
    }

    [Fact]
    public async Task EmptyStore_YieldsNoRoutes()
    {
        var routes = await Resolver().ResolveRoutesAsync();
        Assert.Empty(routes);
    }
}
