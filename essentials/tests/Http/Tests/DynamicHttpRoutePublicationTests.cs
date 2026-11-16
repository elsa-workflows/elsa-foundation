using CShells;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Http;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Exceptions;
using Elsa.Http.Core.Models;
using Elsa.Http.Core.Options;
using Elsa.Http.Options;
using Elsa.Http.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Xunit;

namespace Elsa.Http.Tests;

public sealed class DynamicHttpRoutePublicationTests : IDisposable
{
    private readonly MemoryCache _compatibilityCache = new(new MemoryCacheOptions());

    [Fact]
    public async Task Refresh_EnrichesLegacyRouteWithDynamicOwnerAndPublicDisposition()
    {
        var table = CreateTable("orders-shell");

        await table.Refresh([new HttpRouteData("orders/{id}") { Methods = ["GET"] }]);

        var route = Assert.Single(table);
        var ownership = Assert.IsType<HttpRouteOwnershipMetadata>(route.Metadata.Single(value => value is HttpRouteOwnershipMetadata));
        var security = Assert.IsType<HttpRouteSecurityDispositionMetadata>(route.Metadata.Single(value => value is HttpRouteSecurityDispositionMetadata));
        Assert.Equal(HttpRouteOwnerKind.DynamicShell, ownership.OwnerKind);
        Assert.Equal("Elsa.Http", ownership.OwnerId);
        Assert.Equal("orders-shell", ownership.ShellId);
        Assert.Equal(1, ownership.Generation);
        Assert.Equal(HttpRouteSecurityDispositionKind.Public, security.Kind);
        Assert.Equal("compatibility", security.Category);
    }

    [Fact]
    public async Task Refresh_RejectsEquivalentTemplateAndLeavesPreviousGeneration()
    {
        var table = CreateTable("orders-shell");
        await table.Refresh([new HttpRouteData("orders/{id}") { Methods = ["GET"] }]);

        var exception = await Assert.ThrowsAsync<HttpRouteConflictException>(() =>
            table.Refresh([
                new HttpRouteData("orders/{name}") { Methods = ["GET"] },
                new HttpRouteData("orders/{orderId}") { Methods = ["GET"] }
            ]).AsTask());

        Assert.Contains("orders/{name}", exception.Message, StringComparison.Ordinal);
        Assert.Contains("orders/{orderId}", exception.Message, StringComparison.Ordinal);
        Assert.Equal("orders/{id}", Assert.Single(table).Route);
        Assert.Equal(1, Assert.IsType<HttpRouteOwnershipMetadata>(Assert.Single(table).Metadata.Single(value => value is HttpRouteOwnershipMetadata)).Generation);
    }

    [Fact]
    public async Task Refresh_RejectsConflictAgainstStaticManifestAndPreservesLiveGeneration()
    {
        var staticRoute = new HttpRouteData("/workflows/http/orders/{name}")
        {
            Methods = ["POST"],
            Metadata =
            [
                HttpRouteOwnershipMetadata.Module("Elsa.Orders"),
                HttpRouteSecurityDispositionMetadata.Permission("orders.manage", "Elsa.Orders")
            ]
        };
        var table = CreateTable("orders-shell", new StaticManifestProvider(staticRoute));
        await table.Refresh([new HttpRouteData("orders/{id}") { Methods = ["GET"] }]);

        var exception = await Assert.ThrowsAsync<HttpRouteConflictException>(() =>
            table.Refresh([new HttpRouteData("orders/{id}") { Methods = ["POST"] }]).AsTask());

        Assert.Contains("Elsa.Orders", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Elsa.Http", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, Assert.Single(table).Metadata.OfType<HttpRouteOwnershipMetadata>().Single().Generation);
        Assert.Equal("orders/{id}", Assert.Single(table).Route);
    }

    [Fact]
    public async Task Refresh_DoesNotConflictWithUnrelatedAbsoluteStaticRoute()
    {
        var staticRoute = StaticRoute("/orders/{name}", "POST", "Elsa.Orders");
        var table = CreateTable("orders-shell", new StaticManifestProvider(staticRoute));

        await table.Refresh([new HttpRouteData("orders/{id}") { Methods = ["POST"] }]);

        Assert.Equal("orders/{id}", Assert.Single(table).Route);
    }

    [Fact]
    public async Task Refresh_ConflictsWithStaticRouteAtWorkflowPublicationAddress()
    {
        var staticRoute = StaticRoute("/workflows/http/orders/{name}", "POST", "Elsa.Orders");
        var table = CreateTable("orders-shell", new StaticManifestProvider(staticRoute));

        var exception = await Assert.ThrowsAsync<HttpRouteConflictException>(() =>
            table.Refresh([new HttpRouteData("orders/{id}") { Methods = ["POST"] }]).AsTask());

        Assert.Equal("/workflows/http/orders/{id}", exception.SecondRoute);
        Assert.Equal("POST", exception.OverlappingMethod);
    }

    [Fact]
    public async Task Refresh_UsesConfiguredWorkflowPublicationBasePathForCollisionValidation()
    {
        var staticRoute = StaticRoute("/hooks/orders/{name}", "POST", "Elsa.Orders");
        var table = CreateTable("orders-shell", new StaticManifestProvider(staticRoute), "/hooks");

        var exception = await Assert.ThrowsAsync<HttpRouteConflictException>(() =>
            table.Refresh([new HttpRouteData("orders/{id}") { Methods = ["POST"] }]).AsTask());

        Assert.Equal("/hooks/orders/{id}", exception.SecondRoute);
    }

    [Fact]
    public async Task Refresh_RejectsWorkflowStaticOwnerSpoofAndPreservesLiveGeneration()
    {
        var table = CreateTable("orders-shell");
        await table.Refresh([new HttpRouteData("safe/{id}") { Methods = ["GET"] }]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            table.Refresh([
                new HttpRouteData("orders/{id}")
                {
                    Methods = ["POST"],
                    Metadata = [HttpRouteOwnershipMetadata.Host("Foundation.Host")]
                }
            ]).AsTask());

        Assert.Contains("cannot claim static owner", exception.Message, StringComparison.Ordinal);
        Assert.Equal("safe/{id}", Assert.Single(table).Route);
        Assert.Equal(1, Assert.Single(table).Metadata.OfType<HttpRouteOwnershipMetadata>().Single().Generation);
    }

    [Fact]
    public async Task AddRange_ValidatesAndPublishesOneCandidateWithoutPartialState()
    {
        var staticRoute = new HttpRouteData("/workflows/http/orders/{name}")
        {
            Methods = ["POST"],
            Metadata = [HttpRouteOwnershipMetadata.Module("Elsa.Orders")]
        };
        var table = CreateTable("orders-shell", new StaticManifestProvider(staticRoute));
        await table.Refresh([new HttpRouteData("safe/{id}") { Methods = ["GET"] }]);
        var provider = (IRouteTableSnapshotProvider)table;
        using var lease = provider.AcquireSnapshot();

        var exception = await Assert.ThrowsAsync<HttpRouteConflictException>(() =>
            table.AddRange(["first/{id}", "orders/{id}"]).AsTask());

        Assert.Contains("Elsa.Orders", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Elsa.Http", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, lease.Snapshot.Generation);
        Assert.Equal("safe/{id}", Assert.Single(lease.Snapshot.Routes).Route);
        Assert.Equal("safe/{id}", Assert.Single(table).Route);
        Assert.Equal(1, Assert.Single(table).Metadata.OfType<HttpRouteOwnershipMetadata>().Single().Generation);
    }

    [Fact]
    public async Task AddRange_PublishesAllRoutesInOneGeneration()
    {
        var table = CreateTable("orders-shell");
        await table.Refresh([new HttpRouteData("existing") { Methods = ["GET"] }]);

        await table.AddRange(["orders/{id}", "payments/{id}"]);

        var routes = table.ToArray();
        Assert.Equal(2, routes.Single(route => route.Route == "orders/{id}").Metadata.OfType<HttpRouteOwnershipMetadata>().Single().Generation);
        Assert.Equal(2, routes.Single(route => route.Route == "payments/{id}").Metadata.OfType<HttpRouteOwnershipMetadata>().Single().Generation);
        Assert.Equal(3, routes.Length);
    }

    [Fact]
    public async Task Add_AllowsMethodDisjointEntriesForTheSameTemplate()
    {
        var table = CreateTable("orders-shell");

        await table.Add(new HttpRouteData("orders/{id}") { Methods = ["GET"] });
        await table.Add(new HttpRouteData("orders/{id}") { Methods = ["POST"] });

        var routes = table.ToArray();
        Assert.Equal(2, routes.Length);
        Assert.Equal(["GET", "POST"], routes.Select(route => Assert.Single(route.Methods)).OrderBy(method => method, StringComparer.Ordinal));
        Assert.All(routes, route => Assert.Equal(2, route.Metadata.OfType<HttpRouteOwnershipMetadata>().Single().Generation));
    }

    [Fact]
    public async Task Add_RejectsOverlappingMethodThroughOwnerAwareValidationAndPreservesGeneration()
    {
        var table = CreateTable("orders-shell");
        await table.Add(new HttpRouteData("orders/{id}") { Methods = ["GET"] });

        var exception = await Assert.ThrowsAsync<HttpRouteConflictException>(() =>
            table.Add(new HttpRouteData("orders/{name}") { Methods = ["GET"] }).AsTask());

        Assert.Equal("GET", exception.OverlappingMethod);
        Assert.Contains("Elsa.Http", exception.Message, StringComparison.Ordinal);
        var route = Assert.Single(table);
        Assert.Equal("orders/{id}", route.Route);
        Assert.Equal(1, route.Metadata.OfType<HttpRouteOwnershipMetadata>().Single().Generation);
    }

    [Fact]
    public async Task Add_PreservesLegacyMethodlessDuplicateExceptionAndGeneration()
    {
        var table = CreateTable("orders-shell");
        await table.Add("orders/{id}");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => table.Add("orders/{id}").AsTask());

        Assert.Equal("Route 'orders/{id}' is already added", exception.Message);
        var route = Assert.Single(table);
        Assert.Equal(1, route.Metadata.OfType<HttpRouteOwnershipMetadata>().Single().Generation);
    }

    [Fact]
    public async Task AddRange_PreservesLegacyMethodlessDuplicateExceptionWithoutPartialPublication()
    {
        var table = CreateTable("orders-shell");
        await table.Add("safe");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            table.AddRange(["new", "orders/{id}", "orders/{id}"]).AsTask());

        Assert.Equal("Route 'orders/{id}' is already added", exception.Message);
        Assert.Equal("safe", Assert.Single(table).Route);
        Assert.Equal(1, Assert.Single(table).Metadata.OfType<HttpRouteOwnershipMetadata>().Single().Generation);
    }

    [Fact]
    public async Task Refresh_AllowsStaticRouteWhenMethodsDoNotOverlap()
    {
        var staticRoute = new HttpRouteData("/workflows/http/orders/{name}")
        {
            Methods = ["POST"],
            Metadata = [HttpRouteOwnershipMetadata.Host("Foundation.Host")]
        };
        var table = CreateTable("orders-shell", new StaticManifestProvider(staticRoute));

        await table.Refresh([new HttpRouteData("orders/{id}") { Methods = ["GET"] }]);

        Assert.Equal("orders/{id}", Assert.Single(table).Route);
    }

    [Fact]
    public async Task HttpFeature_ComposesEndpointManifestProviderFromShellServices()
    {
        var endpointBuilder = new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/workflows/http/orders/{name}"),
            order: 0);
        endpointBuilder.Metadata.Add(EndpointOwnershipMetadata.Module("Elsa.Orders"));
        endpointBuilder.Metadata.Add(EndpointSecurityDispositionMetadata.NamedPolicy("orders.read", "Elsa.Orders"));
        endpointBuilder.Metadata.Add(new HttpMethodMetadata(["POST"]));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton(new ShellSettings { Id = new ShellId("orders-shell") });
        services.AddSingleton<EndpointDataSource>(new StaticEndpointDataSource(endpointBuilder.Build()));
        new HttpFeature(new ShellFeatureContext(new ShellSettings { Id = new ShellId("orders-shell") }, [])).ConfigureServices(services);

        await using var serviceProvider = services.BuildServiceProvider();
        var routeTable = serviceProvider.GetRequiredService<IRouteTable>();

        var exception = await Assert.ThrowsAsync<HttpRouteConflictException>(() =>
            routeTable.Refresh([new HttpRouteData("orders/{id}") { Methods = ["POST"] }]).AsTask());

        Assert.Contains("Elsa.Orders", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Elsa.Http", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpManifestProvider_ExcludesRoutesOwnedByAnotherShellGeneration()
    {
        var endpointBuilder = new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("ignored/{name}"),
            order: 0);
        endpointBuilder.Metadata.Add(EndpointOwnershipMetadata.Module("Elsa.OtherShell"));
        endpointBuilder.Metadata.Add(new HttpMethodMetadata(["POST"]));
        endpointBuilder.Metadata.Add(new ShellEndpointMetadata("other-shell"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton(new ShellSettings { Id = new ShellId("orders-shell") });
        services.AddSingleton<EndpointDataSource>(new StaticEndpointDataSource(endpointBuilder.Build()));
        new HttpFeature(new ShellFeatureContext(new ShellSettings { Id = new ShellId("orders-shell") }, [])).ConfigureServices(services);

        await using var serviceProvider = services.BuildServiceProvider();
        var routeTable = serviceProvider.GetRequiredService<IRouteTable>();
        await routeTable.Refresh([new HttpRouteData("ignored/{id}") { Methods = ["POST"] }]);

        Assert.Equal("ignored/{id}", Assert.Single(routeTable).Route);
    }

    [Fact]
    public async Task HttpManifestProvider_RejectsAmbiguousStaticEndpointMetadata()
    {
        var endpointBuilder = new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("ambiguous"),
            order: 0);
        endpointBuilder.Metadata.Add(EndpointOwnershipMetadata.Module("Elsa.First"));
        endpointBuilder.Metadata.Add(EndpointOwnershipMetadata.Module("Elsa.Second"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton(new ShellSettings { Id = new ShellId("orders-shell") });
        services.AddSingleton<EndpointDataSource>(new StaticEndpointDataSource(endpointBuilder.Build()));
        new HttpFeature(new ShellFeatureContext(new ShellSettings { Id = new ShellId("orders-shell") }, [])).ConfigureServices(services);

        await using var serviceProvider = services.BuildServiceProvider();
        var routeTable = serviceProvider.GetRequiredService<IRouteTable>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            routeTable.Refresh([new HttpRouteData("safe")]).AsTask());

        Assert.Contains("2 ownership metadata records", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ambiguous", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpManifestProvider_RejectsAmbiguousStaticSecurityMetadata()
    {
        var endpointBuilder = new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("ambiguous-security"),
            order: 0);
        endpointBuilder.Metadata.Add(EndpointOwnershipMetadata.Module("Elsa.Module"));
        endpointBuilder.Metadata.Add(EndpointSecurityDispositionMetadata.NamedPolicy("orders.read", "Elsa.Module"));
        endpointBuilder.Metadata.Add(EndpointSecurityDispositionMetadata.NamedPolicy("orders.manage", "Elsa.Module"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton(new ShellSettings { Id = new ShellId("orders-shell") });
        services.AddSingleton<EndpointDataSource>(new StaticEndpointDataSource(endpointBuilder.Build()));
        new HttpFeature(new ShellFeatureContext(new ShellSettings { Id = new ShellId("orders-shell") }, [])).ConfigureServices(services);

        await using var serviceProvider = services.BuildServiceProvider();
        var routeTable = serviceProvider.GetRequiredService<IRouteTable>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            routeTable.Refresh([new HttpRouteData("safe")]).AsTask());

        Assert.Contains("2 security-disposition metadata records", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ambiguous-security", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HttpManifestProvider_ProjectsStandardAuthorizationMetadataWithoutCallingItPublic()
    {
        var anonymous = Endpoint("/anonymous", EndpointOwnershipMetadata.Module("Elsa.Module"), new AllowAnonymousAttribute());
        var authenticated = Endpoint("/authenticated", EndpointOwnershipMetadata.Module("Elsa.Module"), new AuthorizeAttribute());
        var namedPolicy = Endpoint("/policy", EndpointOwnershipMetadata.Module("Elsa.Module"), new AuthorizeAttribute("orders.read"));
        using var fixture = ManifestProvider(anonymous, authenticated, namedPolicy);
        var provider = fixture.Provider;

        var routes = provider.GetRoutes().ToDictionary(route => route.Route, StringComparer.Ordinal);

        Assert.Equal(HttpRouteSecurityDispositionKind.Public, Security(routes["/anonymous"]).Kind);
        Assert.Equal(HttpRouteSecurityDispositionKind.HostPolicy, Security(routes["/authenticated"]).Kind);
        Assert.Empty(Security(routes["/authenticated"]).Values);
        Assert.Equal(HttpRouteSecurityDispositionKind.HostPolicy, Security(routes["/policy"]).Kind);
        Assert.Equal(["orders.read"], Security(routes["/policy"]).Values);
    }

    [Theory]
    [InlineData("/catalog/v{version:int}/{page=1}/{slug?}/{*file}")]
    [InlineData("/catalog/v{version:int}/{page=1}/{slug?}/{**path}")]
    public void HttpManifestProvider_ReconstructsRawTextNullRoutePatternsWithoutLosingParameterSemantics(string template)
    {
        var parsed = RoutePatternFactory.Parse(template);
        var rawTextNullPattern = RoutePatternFactory.Pattern(parsed.PathSegments);
        var endpoint = Endpoint(rawTextNullPattern, EndpointOwnershipMetadata.Module("Elsa.Catalog"));
        using var fixture = ManifestProvider(endpoint);
        var provider = fixture.Provider;

        var route = Assert.Single(provider.GetRoutes());

        Assert.Null(rawTextNullPattern.RawText);
        Assert.Equal(template, route.Route);
    }

    [Fact]
    public void ManifestValidator_ReportsBothOwnersForHostModuleAndDynamicConflict()
    {
        var host = new HttpRouteData("health/{id}")
        {
            Methods = ["GET", "POST"],
            Metadata =
            [
                HttpRouteOwnershipMetadata.Host("Foundation.Host"),
                HttpRouteSecurityDispositionMetadata.Public("health", "Health probe.")
            ]
        };
        var module = new HttpRouteData("health/{name}")
        {
            Methods = ["POST"],
            Metadata =
            [
                HttpRouteOwnershipMetadata.Module("Elsa.Health"),
                HttpRouteSecurityDispositionMetadata.Public("health", "Module health probe.")
            ]
        };

        var exception = Assert.Throws<HttpRouteConflictException>(() => HttpRouteManifestValidator.Validate([host, module]));

        Assert.Contains("Foundation.Host", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Elsa.Health", exception.Message, StringComparison.Ordinal);
        Assert.Equal("POST", exception.OverlappingMethod);
    }

    [Fact]
    public async Task Replacement_IsAtomicAndOldGenerationDrainsAfterLeaseRelease()
    {
        var table = CreateTable("orders-shell");
        await table.Refresh([new HttpRouteData("orders/{id}") { Methods = ["GET"] }]);
        var provider = (IRouteTableSnapshotProvider)table;
        using var oldLease = provider.AcquireSnapshot();

        await table.Refresh([new HttpRouteData("payments/{id}") { Methods = ["GET"] }]);

        using (var newLease = provider.AcquireSnapshot())
        {
            Assert.Equal(2, newLease.Snapshot.Generation);
            Assert.Equal("payments/{id}", Assert.Single(newLease.Snapshot.Routes).Route);
        }

        Assert.False(oldLease.Drained.IsCompleted);
        Assert.Equal("orders/{id}", Assert.Single(oldLease.Snapshot.Routes).Route);
        oldLease.Dispose();
        await oldLease.Drained.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CacheEvictionCannotResetTheAuthoritativeSnapshotOrGeneration()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var table = new RouteTable(cache, NullLogger<RouteTable>.Instance);
        await table.Refresh([new HttpRouteData("orders") { Methods = ["GET"] }]);

        cache.Compact(1.0);

        using var beforeReplacement = ((IRouteTableSnapshotProvider)table).AcquireSnapshot();
        Assert.Equal(1, beforeReplacement.Snapshot.Generation);
        Assert.Equal("orders", Assert.Single(beforeReplacement.Snapshot.Routes).Route);
        await table.Refresh([new HttpRouteData("payments") { Methods = ["GET"] }]);
        using var afterReplacement = ((IRouteTableSnapshotProvider)table).AcquireSnapshot();
        Assert.Equal(2, afterReplacement.Snapshot.Generation);
    }

    [Fact]
    public async Task HttpFeatureSharesStateWithinOneShellProviderAndIsolatesAnotherProvider()
    {
        await using var firstProvider = BuildHttpFeatureProvider("same-shell");
        await using var secondProvider = BuildHttpFeatureProvider("same-shell");
        await using var firstScope = firstProvider.CreateAsyncScope();
        await using var siblingScope = firstProvider.CreateAsyncScope();
        await using var isolatedScope = secondProvider.CreateAsyncScope();

        await firstScope.ServiceProvider.GetRequiredService<IRouteTable>().Refresh([new HttpRouteData("orders")]);

        Assert.Equal("orders", Assert.Single(siblingScope.ServiceProvider.GetRequiredService<IRouteTable>()).Route);
        Assert.Empty(isolatedScope.ServiceProvider.GetRequiredService<IRouteTable>());
    }

    [Fact]
    public async Task PublishedSnapshotCollectionsAreDefensiveAndReadOnly()
    {
        var sourceRoutes = new List<HttpRouteData> { new("source") };
        var snapshot = new HttpRouteTableSnapshot(1, sourceRoutes);
        sourceRoutes.Clear();
        Assert.Single(snapshot.Routes);
        Assert.Throws<NotSupportedException>(() => ((IList<HttpRouteData>)snapshot.Routes).Add(new HttpRouteData("mutated")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HttpRouteTableSnapshot(-1, []));

        var methods = new[] { "GET" };
        var metadata = new object[] { HttpRouteSecurityDispositionMetadata.Public("test", "Mutation regression.") };
        var table = CreateTable("immutable-shell");
        await table.Refresh([new HttpRouteData("orders") { Methods = methods, Metadata = metadata }]);
        methods[0] = "POST";
        metadata[0] = new object();

        using var lease = ((IRouteTableSnapshotProvider)table).AcquireSnapshot();
        var route = Assert.Single(lease.Snapshot.Routes);
        Assert.Equal(["GET"], route.Methods);
        Assert.IsType<HttpRouteSecurityDispositionMetadata>(Assert.Single(route.Metadata, value => value is HttpRouteSecurityDispositionMetadata));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)route.Methods).Add("DELETE"));
        Assert.Throws<NotSupportedException>(() => ((IList<object>)route.Metadata).Add(new object()));
    }

    [Fact]
    public async Task SnapshotElementMutationCannotChangePublishedInspectionOrRoutingState()
    {
        await using var provider = BuildHttpFeatureProvider("immutable-shell");
        await using var scope = provider.CreateAsyncScope();
        var table = scope.ServiceProvider.GetRequiredService<IRouteTable>();
        await table.Refresh([
            new HttpRouteData(
                "orders/{id}",
                new RouteValueDictionary { ["token"] = "original" },
                new RouteValueDictionary { ["default"] = "original" })
            {
                Methods = ["GET"],
                Metadata = [HttpRouteSecurityDispositionMetadata.Public("test", "Mutation isolation.")]
            }
        ]);

        using var lease = ((IRouteTableSnapshotProvider)table).AcquireSnapshot();
        var exposed = Assert.Single(lease.Snapshot.Routes);
        exposed.Route = "tampered/{value}";
        exposed.DataTokens["token"] = "tampered";
        exposed.RouteValues["default"] = "tampered";
        exposed.Methods = ["POST"];
        exposed.Metadata = [new object()];
        exposed.CompiledMatcher = new object();

        var inspectedAgain = Assert.Single(lease.Snapshot.Routes);
        Assert.Equal("orders/{id}", inspectedAgain.Route);
        Assert.Equal("original", inspectedAgain.DataTokens["token"]);
        Assert.Equal("original", inspectedAgain.RouteValues["default"]);
        Assert.Equal(["GET"], inspectedAgain.Methods);
        Assert.IsType<TemplateMatcher>(inspectedAgain.CompiledMatcher);

        var match = lease.ResolveRoute("orders/42", "GET", scope.ServiceProvider.GetRequiredService<IRouteMatcher>());
        Assert.NotNull(match);
        Assert.Equal("orders/{id}", match.Template);
        Assert.Equal("42", match.RouteValues["id"]);
        Assert.Null(lease.ResolveRoute("tampered/42", "POST", scope.ServiceProvider.GetRequiredService<IRouteMatcher>()));
    }

    [Fact]
    public async Task RepeatedReplacementNeverPublishesAnEmptySnapshot()
    {
        var table = CreateTable("orders-shell");
        await table.Refresh([new HttpRouteData("orders/{id}") { Methods = ["GET"] }]);
        var provider = (IRouteTableSnapshotProvider)table;
        var observedEmpty = false;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var reader = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                using var lease = provider.AcquireSnapshot();
                if (lease.Snapshot.Routes.Count == 0)
                {
                    observedEmpty = true;
                    return;
                }

                await Task.Yield();
            }
        });

        for (var i = 0; i < 300 && !observedEmpty; i++)
            await table.Refresh([new HttpRouteData(i % 2 == 0 ? "orders/{id}" : "payments/{id}") { Methods = ["GET"] }]);

        cancellation.Cancel();
        await reader;
        Assert.False(observedEmpty);
    }

    [Fact]
    public void RetiredGenerationReleasesCollectibleRouteMetadata()
    {
        var table = CreateTable("collectible-shell");
        var evidence = PublishCollectibleRouteAndReplace(table);

        for (var attempt = 0; attempt < 12 && evidence.Any(reference => reference.IsAlive); attempt++)
            ForceCollection();

        Assert.All(evidence, reference => Assert.False(reference.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] PublishCollectibleRouteAndReplace(RouteTable table)
    {
        var loadContext = new AssemblyLoadContext($"Elsa.Dynamic.Route.{Guid.NewGuid():N}", isCollectible: true);
        using var assemblyStream = File.OpenRead(typeof(HttpRouteData).Assembly.Location);
        var assembly = loadContext.LoadFromStream(assemblyStream);
        var type = assembly.GetType(typeof(HttpRouteData).FullName!)!;
        var metadata = Activator.CreateInstance(type, "collectible")!;
        var evidence = new[]
        {
            new WeakReference(loadContext),
            new WeakReference(assembly),
            new WeakReference(type),
            new WeakReference(metadata)
        };

        table.Refresh([
            new HttpRouteData("collectible")
            {
                Metadata = [metadata]
            }
        ]).GetAwaiter().GetResult();
        table.Refresh([new HttpRouteData("replacement")]).GetAwaiter().GetResult();
        loadContext.Unload();
        return evidence;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    public void Dispose() => _compatibilityCache.Dispose();

    private RouteTable CreateTable(string shellId) =>
        new(
            _compatibilityCache,
            NullLogger<RouteTable>.Instance,
            Microsoft.Extensions.Options.Options.Create(new RouteTableOptions { ShellDiscriminator = shellId }));

    private RouteTable CreateTable(string shellId, IHttpRouteManifestProvider staticManifestProvider, string publicationBasePath = "/workflows/http") =>
        new(
            _compatibilityCache,
            NullLogger<RouteTable>.Instance,
            Microsoft.Extensions.Options.Options.Create(new RouteTableOptions { ShellDiscriminator = shellId }),
            staticManifestProvider,
            Microsoft.Extensions.Options.Options.Create(new HttpRoutePublicationOptions { BasePath = publicationBasePath }));

    private static HttpRouteData StaticRoute(string route, string method, string ownerId) => new(route)
    {
        Methods = [method],
        Metadata = [HttpRouteOwnershipMetadata.Module(ownerId)]
    };

    private static RouteEndpoint Endpoint(string route, params object[] metadata) =>
        Endpoint(RoutePatternFactory.Parse(route), metadata);

    private static RouteEndpoint Endpoint(RoutePattern routePattern, params object[] metadata)
    {
        var builder = new RouteEndpointBuilder(_ => Task.CompletedTask, routePattern, 0);
        foreach (var value in metadata)
            builder.Metadata.Add(value);
        builder.Metadata.Add(new HttpMethodMetadata(["GET"]));
        return (RouteEndpoint)builder.Build();
    }

    private static ManifestProviderFixture ManifestProvider(params Endpoint[] endpoints)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        var settings = new ShellSettings { Id = new ShellId("orders-shell") };
        services.AddSingleton(settings);
        services.AddSingleton<EndpointDataSource>(new StaticEndpointDataSource(endpoints));
        new HttpFeature(new ShellFeatureContext(settings, [])).ConfigureServices(services);
        return new ManifestProviderFixture(services.BuildServiceProvider());
    }

    private static HttpRouteSecurityDispositionMetadata Security(HttpRouteData route) =>
        route.Metadata.OfType<HttpRouteSecurityDispositionMetadata>().Single();

    private static ServiceProvider BuildHttpFeatureProvider(string shellId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        var settings = new ShellSettings { Id = new ShellId(shellId) };
        services.AddSingleton(settings);
        new HttpFeature(new ShellFeatureContext(settings, [])).ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private sealed class StaticManifestProvider(params HttpRouteData[] routes) : IHttpRouteManifestProvider
    {
        public IEnumerable<HttpRouteData> GetRoutes() => routes;
    }

    private sealed class ManifestProviderFixture(ServiceProvider serviceProvider) : IDisposable
    {
        public IHttpRouteManifestProvider Provider { get; } = serviceProvider.GetRequiredService<IHttpRouteManifestProvider>();

        public void Dispose() => serviceProvider.Dispose();
    }

    private sealed class StaticEndpointDataSource(params Endpoint[] endpoints) : EndpointDataSource
    {
        public override IReadOnlyList<Endpoint> Endpoints { get; } = endpoints;

        public override IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }

    private sealed class ShellEndpointMetadata(string shellId)
    {
        public string ShellId { get; } = shellId;
    }

}
