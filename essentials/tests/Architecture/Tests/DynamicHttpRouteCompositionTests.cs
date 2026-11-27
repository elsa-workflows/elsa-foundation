using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.FastEndpoints.Features;
using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Http;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Exceptions;
using Elsa.Http.Core.Models;
using Elsa.Http.Core.Options;
using Elsa.Http.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Operational regressions for the root-to-shell HTTP route-manifest composition seam. These tests intentionally
/// activate a real CShells shell instead of constructing a flat service collection: HttpFeature must register its
/// adapter in the child shell after root endpoint sources have been composed.
/// </summary>
/// <remarks>
/// Collectibility of an <see cref="System.Runtime.Loader.AssemblyLoadContext"/> is not safe to prove while sibling
/// architecture tests keep allocating. This class shares the OpenAPI lifetime pattern: its own collection with
/// <c>DisableParallelization</c>, NoInlining unload boundaries, and a blocking GC loop that names any surviving root.
/// </remarks>
[Collection(Name)]
public sealed class DynamicHttpRouteCompositionTests
{
    public const string Name = nameof(DynamicHttpRouteCompositionTests);

    [Fact]
    public async Task Root_module_endpoint_manifest_is_promoted_into_http_shell_and_rejects_workflow_collision()
    {
        await using var app = await StartHttpShellHostAsync();

        var shell = app.Services.GetRequiredService<IShellRegistry>().GetActive("default")
            ?? throw new InvalidOperationException("The test shell was not activated.");
        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();
        var provider = scope.ServiceProvider.GetRequiredService<IHttpRouteManifestProvider>();

        var staticManifestRoute = Assert.Single(provider.GetRoutes());
        Assert.Equal("/workflows/http/module/orders/{name}", staticManifestRoute.Route);
        Assert.Equal(["POST"], staticManifestRoute.Methods);
        Assert.Equal("Elsa.Orders", staticManifestRoute.Metadata.OfType<HttpRouteOwnershipMetadata>().Single().OwnerId);

        await routeTable.Refresh([new HttpRouteData("safe/{id}") { Methods = ["GET"] }]);
        var previousGeneration = Assert.Single(routeTable).Metadata.OfType<HttpRouteOwnershipMetadata>().Single().Generation;

        var exception = await Assert.ThrowsAsync<HttpRouteConflictException>(() =>
            routeTable.Refresh([new HttpRouteData("module/orders/{id}") { Methods = ["POST"] }]).AsTask());

        Assert.Contains("Elsa.Orders", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Elsa.Http", exception.Message, StringComparison.Ordinal);
        Assert.Equal("POST", exception.OverlappingMethod);
        Assert.Equal("safe/{id}", Assert.Single(routeTable).Route);
        Assert.Equal(previousGeneration, Assert.Single(routeTable).Metadata.OfType<HttpRouteOwnershipMetadata>().Single().Generation);
    }

    [Fact]
    public async Task Root_route_outside_workflow_base_does_not_conflict_with_endpoint_relative_workflow_route()
    {
        await using var app = await StartHttpShellHostAsync("/module/orders/{name}");
        var shell = app.Services.GetRequiredService<IShellRegistry>().GetActive("default")
            ?? throw new InvalidOperationException("The test shell was not activated.");
        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();

        await routeTable.Refresh([new HttpRouteData("module/orders/{id}") { Methods = ["POST"] }]);

        Assert.Equal("module/orders/{id}", Assert.Single(routeTable).Route);
    }

    [Fact]
    public async Task Custom_workflow_base_path_is_composed_into_real_shell_collision_validation()
    {
        await using var app = await StartHttpShellHostAsync("/hooks/module/orders/{name}", "/hooks");
        var shell = app.Services.GetRequiredService<IShellRegistry>().GetActive("default")
            ?? throw new InvalidOperationException("The test shell was not activated.");
        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();

        var exception = await Assert.ThrowsAsync<HttpRouteConflictException>(() =>
            routeTable.Refresh([new HttpRouteData("module/orders/{id}") { Methods = ["POST"] }]).AsTask());

        Assert.Equal("/hooks/module/orders/{id}", exception.SecondRoute);
        Assert.Contains("Elsa.Orders", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Elsa.Http", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Repeated_http_shell_reload_releases_collectible_route_generation_roots()
    {
        // An async test method keeps the last cycle's WebApplication on its state machine until the
        // assertion finishes, so the last shell/DI/state-owner roots never collect. The OpenAPI
        // collectibility fixture is synchronous for the same reason.
        var evidence = RunCollectibleShellCycles();

        for (var attempt = 0; attempt < 32 && evidence.Any(item => item.Reference.IsAlive); attempt++)
            ForceCollection();

        Assert.NotEmpty(evidence);
        var live = evidence
            .Where(item => item.Reference.IsAlive)
            .Select(item => item.Label)
            .ToArray();
        Assert.True(live.Length == 0, $"Collectible route-generation roots still alive: {string.Join(", ", live)}.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static List<LabeledReference> RunCollectibleShellCycles()
    {
        var evidence = new List<LabeledReference>();
        for (var cycle = 0; cycle < 4; cycle++)
        {
            evidence.AddRange(RunCollectibleShellCycle(cycle));
            ForceCollection();
        }

        return evidence;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static LabeledReference[] RunCollectibleShellCycle(int cycle)
    {
        var app = StartHttpShellHostAsync().GetAwaiter().GetResult();
        try
        {
            var registry = app.Services.GetRequiredService<IShellRegistry>();
            var shell = registry.GetActive("default")
                ?? throw new InvalidOperationException("The test shell was not activated.");
            var evidence = PublishCollectibleGeneration(shell, cycle);
            var reload = registry.ReloadAsync("default").GetAwaiter().GetResult();
            reload.Drain?.WaitAsync().GetAwaiter().GetResult();

            // A code-first blueprint is intentionally read-only, so CShells retains disposed shell history for
            // diagnostics during reload. Disposing this real host is the supported unload boundary that releases
            // the registry and all of its historical shell/provider roots.
            app.StopAsync().GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return evidence;
        }
        catch
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    private static async Task<WebApplication> StartHttpShellHostAsync(
        string moduleRoute = "/workflows/http/module/orders/{name}",
        string workflowBasePath = "/workflows/http")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.Configure<HttpRoutePublicationOptions>(options => options.BasePath = workflowBasePath);

        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(typeof(HttpFeature).Assembly)
            .AddShell("default", shell => shell.WithFeatures("Http"))
            .WithWebRouting(options => options.EnablePathRouting = true));

        var app = builder.Build();
        app.MapPost(moduleRoute, () => Results.Ok())
            .WithOwner("Elsa.Orders")
            .WithSecurityDisposition(EndpointSecurityDispositionMetadata.NamedPolicy("orders.read", "Elsa.Orders"));
        app.MapShells();
        await app.StartAsync();

        var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync("default");
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();
        Assert.Contains("Http", settings.EnabledFeatures);
        return app;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static LabeledReference[] PublishCollectibleGeneration(
        IShell shell,
        int cycle)
    {
        using var scope = shell.ServiceProvider.CreateScope();
        var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();
        var provider = scope.ServiceProvider.GetRequiredService<IHttpRouteManifestProvider>();
        var stateType = typeof(HttpFeature).Assembly.GetType("Elsa.Http.Services.RouteTableState", throwOnError: true)!;
        var stateOwner = scope.ServiceProvider.GetRequiredService(stateType);
        var serializerType = Type.GetType("Elsa.Serialization.Core.IPayloadSerializer, Elsa.Serialization.Core");

        // Route publication stores immutable route metadata and does not serialize or retain workflow payloads.
        // HttpFeature-only shells therefore intentionally have no IPayloadSerializer root; this is the exact
        // disposition for this custom publication path, rather than an omitted assertion.
        Assert.Null(serializerType is null ? null : scope.ServiceProvider.GetService(serializerType));

        var snapshotProvider = (IRouteTableSnapshotProvider)routeTable;
        using var currentLease = snapshotProvider.AcquireSnapshot();
        var expectedGeneration = currentLease.Snapshot.Generation + 1;
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext($"Elsa.Http.Shell.{cycle}.{Guid.NewGuid():N}", isCollectible: true);
        using var assemblyStream = File.OpenRead(typeof(HttpRouteData).Assembly.Location);
        var assembly = loadContext.LoadFromStream(assemblyStream);
        var routeType = assembly.GetType(typeof(HttpRouteData).FullName!)!;
        var routeMetadata = Activator.CreateInstance(routeType, $"collectible-generation-{cycle}")!;
        var routeDelegate = (Func<string>)routeMetadata.ToString!;
        var providerRoot = provider;
        var evidence = new[]
        {
            Label($"cycle-{cycle}/load-context", loadContext),
            Label($"cycle-{cycle}/assembly", assembly),
            Label($"cycle-{cycle}/route-type", routeType),
            Label($"cycle-{cycle}/route-metadata", routeMetadata),
            Label($"cycle-{cycle}/route-delegate", routeDelegate),
            Label($"cycle-{cycle}/provider", providerRoot),
            Label($"cycle-{cycle}/state-owner", stateOwner),
            Label($"cycle-{cycle}/route-table", routeTable),
            Label($"cycle-{cycle}/scope-services", scope.ServiceProvider)
        };

        routeTable.Refresh([
            new HttpRouteData($"collectible/{cycle}")
            {
                Metadata = [routeMetadata, routeDelegate]
            }
        ]).GetAwaiter().GetResult();
        var publishedRoute = routeTable.Single();
        var owner = publishedRoute.Metadata.OfType<HttpRouteOwnershipMetadata>().Single();
        Assert.Equal(HttpRouteOwnerKind.DynamicShell, owner.OwnerKind);
        Assert.Equal("Elsa.Http", owner.OwnerId);
        Assert.Equal("default", owner.ShellId);
        Assert.Equal(expectedGeneration, owner.Generation);
        routeTable.Refresh([new HttpRouteData($"replacement/{cycle}")]).GetAwaiter().GetResult();
        loadContext.Unload();
        return [
            Label($"cycle-{cycle}/shell", shell),
            Label($"cycle-{cycle}/shell-services", shell.ServiceProvider),
            .. evidence,
            Label($"cycle-{cycle}/published-route", publishedRoute),
            Label($"cycle-{cycle}/owner", owner)
        ];
    }

    private static LabeledReference Label(string label, object target) => new(label, new WeakReference(target));

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ForceCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private readonly record struct LabeledReference(string Label, WeakReference Reference);
}

[CollectionDefinition(DynamicHttpRouteCompositionTests.Name, DisableParallelization = true)]
public sealed class DynamicHttpRouteCompositionTestCollection;
