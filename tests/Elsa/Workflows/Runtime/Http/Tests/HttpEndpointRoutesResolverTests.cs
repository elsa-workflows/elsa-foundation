using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Http.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

public sealed class HttpEndpointRoutesResolverTests
{
    private readonly InMemoryWorkflowTriggerBindingStore _store = new();
    private readonly InMemoryBookmarkStateStore _bookmarks = new();

    // The resolver projects endpoint-relative templates only — the endpoints base path is a middleware concern
    // (HttpEndpointMiddleware strips it before consulting the route table), so it takes no base-path option. The
    // waiting-bookmark union goes through the REAL expiry-aware lookup over the in-memory store (via Resolvers.Build),
    // exercising the production expiry-filtering path rather than a fake; a null logger stands in for the conflict
    // warning.
    private HttpEndpointRoutesResolver Resolver() => Resolvers.Build(_store, _bookmarks);

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
        Assert.Equal(new[] { "DELETE", "GET" }, route.Methods);
    }

    [Fact]
    public async Task ProjectsPublicSecurityDisposition_ForUnauthenticatedWorkflowRoute()
    {
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "public", "GET"));

        var route = Assert.Single(await Resolver().ResolveRoutesAsync());
        var disposition = Assert.IsType<HttpRouteSecurityDispositionMetadata>(route.Metadata.Single(value => value is HttpRouteSecurityDispositionMetadata));
        Assert.Equal(HttpRouteSecurityDispositionKind.Public, disposition.Kind);
        Assert.Equal("workflow-authored", disposition.Category);
        Assert.NotNull(disposition.Reason);
    }

    [Fact]
    public async Task ProjectsNamedPolicyDisposition_ForAuthorizedWorkflowRoute()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Elsa.Http.Core.HttpEndpointRouting.TemplateMetadataKey] = "orders",
            [Elsa.Http.Core.HttpEndpointRouting.MethodMetadataKey] = "GET",
            [Elsa.Http.Core.HttpEndpointRouting.AuthorizeMetadataKey] = "true",
            [Elsa.Http.Core.HttpEndpointRouting.PolicyMetadataKey] = "orders.read"
        };
        await _store.SaveAsync(Bindings.Build("a1", "n1", Elsa.Http.Core.HttpEndpointRouting.StimulusType, "sha256:authorized", metadata));

        var route = Assert.Single(await Resolver().ResolveRoutesAsync());
        var disposition = Assert.IsType<HttpRouteSecurityDispositionMetadata>(route.Metadata.Single(value => value is HttpRouteSecurityDispositionMetadata));
        Assert.Equal(HttpRouteSecurityDispositionKind.NamedPolicy, disposition.Kind);
        Assert.Equal(["orders.read"], disposition.Values);
        Assert.Equal("Elsa.Http", disposition.OwnerId);
    }

    [Fact]
    public async Task ProjectsAuthenticatedPrincipalDisposition_ForAuthorizeOnlyWorkflowRoute()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Elsa.Http.Core.HttpEndpointRouting.TemplateMetadataKey] = "orders",
            [Elsa.Http.Core.HttpEndpointRouting.MethodMetadataKey] = "GET",
            [Elsa.Http.Core.HttpEndpointRouting.AuthorizeMetadataKey] = "true"
        };
        await _store.SaveAsync(Bindings.Build("a1", "n1", Elsa.Http.Core.HttpEndpointRouting.StimulusType, "sha256:authenticated", metadata));

        var route = Assert.Single(await Resolver().ResolveRoutesAsync());
        var disposition = Assert.IsType<HttpRouteSecurityDispositionMetadata>(route.Metadata.Single(value => value is HttpRouteSecurityDispositionMetadata));
        Assert.Equal(HttpRouteSecurityDispositionKind.AuthenticatedPrincipal, disposition.Kind);
        Assert.Empty(disposition.Values);
        Assert.Equal("Elsa.Http", disposition.OwnerId);
    }

    [Fact]
    public async Task MixedAuthorizeOnlyAndNamedPolicyBindingsRemainFailClosedWithActualPolicies()
    {
        var authorizeOnlyMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Elsa.Http.Core.HttpEndpointRouting.TemplateMetadataKey] = "orders",
            [Elsa.Http.Core.HttpEndpointRouting.MethodMetadataKey] = "GET",
            [Elsa.Http.Core.HttpEndpointRouting.AuthorizeMetadataKey] = "true"
        };
        var namedPolicyMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Elsa.Http.Core.HttpEndpointRouting.TemplateMetadataKey] = "orders",
            [Elsa.Http.Core.HttpEndpointRouting.MethodMetadataKey] = "GET",
            [Elsa.Http.Core.HttpEndpointRouting.AuthorizeMetadataKey] = "true",
            [Elsa.Http.Core.HttpEndpointRouting.PolicyMetadataKey] = "orders.read"
        };
        await _store.SaveAsync(Bindings.Build("a1", "n1", Elsa.Http.Core.HttpEndpointRouting.StimulusType, "sha256:auth-only", authorizeOnlyMetadata, "def-a"));
        await _store.SaveAsync(Bindings.Build("a1", "n2", Elsa.Http.Core.HttpEndpointRouting.StimulusType, "sha256:named", namedPolicyMetadata, "def-a"));

        var route = Assert.Single(await Resolver().ResolveRoutesAsync());
        var disposition = Assert.IsType<HttpRouteSecurityDispositionMetadata>(route.Metadata.Single(value => value is HttpRouteSecurityDispositionMetadata));
        Assert.Equal(HttpRouteSecurityDispositionKind.NamedPolicy, disposition.Kind);
        Assert.Equal(["orders.read"], disposition.Values);
        Assert.DoesNotContain("default", disposition.Values, StringComparer.Ordinal);
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

    // ---- Mid-flow bookmark union (spec 089 D, T009 / D-D4) ----

    [Fact]
    public async Task UnionsWaitingBookmarkTemplates_WithTriggerBindingTemplates()
    {
        // A start trigger publishes 'products'; a suspended instance waits on a mid-flow 'callbacks/{id}'.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "products", "POST"));
        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf1", "callbacks/{id}", "POST"));

        var routes = await Resolver().ResolveRoutesAsync();

        Assert.Equal(
            new[] { "callbacks/{id}", "products" }.OrderBy(x => x),
            routes.Select(r => r.Route).OrderBy(x => x));
    }

    [Fact]
    public async Task BookmarkOnlyStore_ProjectsBookmarkTemplates()
    {
        // No start triggers at all — a directly-started workflow suspended mid-flow still routes its endpoint.
        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf1", "callbacks/{id}", "POST"));

        var route = Assert.Single(await Resolver().ResolveRoutesAsync());
        Assert.Equal("callbacks/{id}", route.Route);
    }

    [Fact]
    public async Task DedupesTemplate_SharedByTriggerAndBookmark()
    {
        // A start trigger and a suspended instance await the SAME template — the union collapses to one route.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));
        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf1", "orders/{id}", "GET"));

        var route = Assert.Single(await Resolver().ResolveRoutesAsync());
        Assert.Equal("orders/{id}", route.Route);
    }

    [Fact]
    public async Task DedupesTemplate_SharedByMultipleMethodBookmarks()
    {
        // One suspended instance holds one bookmark per method (D-D2), all sharing a template → one route.
        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf1", "callbacks/{id}", "GET"));
        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf1", "callbacks/{id}", "POST"));

        var route = Assert.Single(await Resolver().ResolveRoutesAsync());
        Assert.Equal("callbacks/{id}", route.Route);
    }

    [Fact]
    public async Task ExcludesExpiredBookmarkTemplates()
    {
        // The expiry-aware lookup drops a bookmark whose ExpiresAt is in the past; its template does not route.
        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf1", "live", "GET"));
        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf2", "expired", "GET", expiresAt: DateTimeOffset.UnixEpoch));

        var route = Assert.Single(await Resolver().ResolveRoutesAsync());
        Assert.Equal("live", route.Route);
    }

    [Fact]
    public async Task IgnoresNonHttpBookmarks()
    {
        await _bookmarks.SaveAsync(Bookmarks.Other("wf1", "Event"));
        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf2", "callbacks", "POST"));

        var route = Assert.Single(await Resolver().ResolveRoutesAsync());
        Assert.Equal("callbacks", route.Route);
    }

    [Fact]
    public async Task IgnoresHttpBookmarks_MissingTemplateKey()
    {
        await _bookmarks.SaveAsync(Bookmarks.Build(
            "wf1", Elsa.Http.Core.HttpEndpointRouting.StimulusType, "sha256:x",
            new Dictionary<string, string>(StringComparer.Ordinal)));
        await _bookmarks.SaveAsync(Bookmarks.HttpEndpoint("wf2", "callbacks", "POST"));

        var route = Assert.Single(await Resolver().ResolveRoutesAsync());
        Assert.Equal("callbacks", route.Route);
    }

    [Fact]
    public async Task NoBookmarks_IdenticalToBindingOnlyProjection()
    {
        // Regression pin: with no waiting bookmarks the union is byte-identical to the pre-D binding-only result.
        await _store.SaveAsync(Bindings.HttpEndpoint("a1", "n1", "orders/{id}", "GET"));
        await _store.SaveAsync(Bindings.HttpEndpoint("a2", "n2", "products", "POST"));

        var routes = await Resolver().ResolveRoutesAsync();

        Assert.Equal(
            new[] { "orders/{id}", "products" }.OrderBy(x => x),
            routes.Select(r => r.Route).OrderBy(x => x));
    }
}
