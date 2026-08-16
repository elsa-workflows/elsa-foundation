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
public sealed class DynamicHttpRouteCompositionTests
{
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
        Assert.Equal("/module/orders/{name}", staticManifestRoute.Route);
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
    public async Task Repeated_http_shell_reload_releases_collectible_route_generation_roots()
    {
        var evidence = new List<WeakReference>();

        for (var cycle = 0; cycle < 4; cycle++)
        {
            var cycleEvidence = await RunCollectibleShellCycleAsync(cycle);
            evidence.AddRange(cycleEvidence);
            ForceCollection();
        }

        for (var attempt = 0; attempt < 20 && evidence.Any(reference => reference.IsAlive); attempt++)
        {
            ForceCollection();
            await Task.Delay(10);
        }

        Assert.NotEmpty(evidence);
        Assert.All(evidence, reference => Assert.False(reference.IsAlive));
    }

    private static async Task<WeakReference[]> RunCollectibleShellCycleAsync(int cycle)
    {
        var app = await StartHttpShellHostAsync();
        try
        {
            var registry = app.Services.GetRequiredService<IShellRegistry>();
            var shell = registry.GetActive("default")
                ?? throw new InvalidOperationException("The test shell was not activated.");
            var evidence = PublishCollectibleGeneration(shell, cycle);
            var reload = await registry.ReloadAsync("default");
            if (reload.Drain is not null)
                await reload.Drain.WaitAsync();

            // A code-first blueprint is intentionally read-only, so CShells retains disposed shell history for
            // diagnostics during reload. Disposing this real host is the supported unload boundary that releases
            // the registry and all of its historical shell/provider roots.
            await app.StopAsync();
            await app.DisposeAsync();
            return evidence;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static async Task<WebApplication> StartHttpShellHostAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(typeof(HttpFeature).Assembly)
            .AddShell("default", shell => shell.WithFeatures("Http"))
            .WithWebRouting(options => options.EnablePathRouting = true));

        var app = builder.Build();
        app.MapPost("/module/orders/{name}", () => Results.Ok())
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
    private static WeakReference[] PublishCollectibleGeneration(
        IShell shell,
        int cycle)
    {
        using var scope = shell.ServiceProvider.CreateScope();
        var routeTable = scope.ServiceProvider.GetRequiredService<IRouteTable>();
        var provider = scope.ServiceProvider.GetRequiredService<IHttpRouteManifestProvider>();
        var serializerType = Type.GetType("Elsa.Serialization.Core.IPayloadSerializer, Elsa.Serialization.Core");

        // Route publication stores immutable route metadata and does not serialize or retain workflow payloads.
        // HttpFeature-only shells therefore intentionally have no IPayloadSerializer root; this is the exact
        // disposition for this custom publication path, rather than an omitted assertion.
        Assert.Null(serializerType is null ? null : scope.ServiceProvider.GetService(serializerType));

        var shellReference = new WeakReference(shell);
        var shellServicesReference = new WeakReference(shell.ServiceProvider);
        var scopeServicesReference = new WeakReference(scope.ServiceProvider);
        var routeTableReference = new WeakReference(routeTable);
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
            new WeakReference(loadContext),
            new WeakReference(assembly),
            new WeakReference(routeType),
            new WeakReference(routeMetadata),
            new WeakReference(routeDelegate),
            new WeakReference(providerRoot),
            routeTableReference,
            scopeServicesReference
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
            shellReference,
            shellServicesReference,
            .. evidence,
            new WeakReference(publishedRoute),
            new WeakReference(owner)
        ];
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ForceCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
