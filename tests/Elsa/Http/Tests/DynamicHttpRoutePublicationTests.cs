using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CShells;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Http;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Exceptions;
using Elsa.Http.Core.Models;
using Elsa.Http.Options;
using Elsa.Http.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Elsa.Http.Tests;

public sealed class DynamicHttpRoutePublicationTests
{
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
        var staticRoute = new HttpRouteData("orders/{name}")
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
        var staticRoute = new HttpRouteData("orders/{name}")
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
    public async Task Refresh_AllowsStaticRouteWhenMethodsDoNotOverlap()
    {
        var staticRoute = new HttpRouteData("orders/{name}")
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
            RoutePatternFactory.Parse("orders/{name}"),
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

    private static RouteTable CreateTable(string shellId) =>
        new(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<RouteTable>.Instance,
            Microsoft.Extensions.Options.Options.Create(new RouteTableOptions { ShellDiscriminator = shellId }));

    private static RouteTable CreateTable(string shellId, IHttpRouteManifestProvider staticManifestProvider) =>
        new(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<RouteTable>.Instance,
            Microsoft.Extensions.Options.Options.Create(new RouteTableOptions { ShellDiscriminator = shellId }),
            staticManifestProvider);

    private sealed class StaticManifestProvider(params HttpRouteData[] routes) : IHttpRouteManifestProvider
    {
        public IEnumerable<HttpRouteData> GetRoutes() => routes;
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
