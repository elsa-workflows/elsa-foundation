using CShells;
using Elsa.Activities.Bpmn.Interchange;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Modularity.Api;
using Elsa.Workflows.ExecutionEvidence;
using Elsa3.Activities.Design.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Exercises every Wave 2 owner through real route, DI, serializer, and disposal cycles.</summary>
public sealed class Wave2MinimalApiUnloadabilityTests
{
    private static readonly (Type FeatureType, string Owner, int Routes)[] Owners =
    [
        (typeof(ActivitiesBpmnInterchangeFeature), "Elsa.Activities.Bpmn.Interchange", 3),
        (typeof(ModularityApiFeature), "Elsa.Modularity.Api", 2),
        (typeof(WorkflowsExecutionEvidenceFeature), "Elsa.Workflows.ExecutionEvidence", 3),
        (typeof(Elsa3ImportActivitiesFeature), "Elsa3.Activities.Design.Import", 5)
    ];

    [Fact]
    public void Repeated_collectible_owner_cycles_release_routes_di_and_serializer_state()
    {
        for (var cycle = 0; cycle < 5; cycle++)
        {
            var evidence = RunCycle();
            ForceCollection();

            Assert.Equal(Owners.Length, evidence.Count);
            foreach (var owner in evidence)
            {
                Assert.True(owner.RouteCount > 0, owner.Diagnostic);
                Assert.False(owner.LoadContext.IsAlive, owner.Diagnostic);
                Assert.False(owner.Assembly.IsAlive, owner.Diagnostic);
                Assert.False(owner.FeatureType.IsAlive, owner.Diagnostic);
                Assert.False(owner.Endpoint.IsAlive, owner.Diagnostic);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<UnloadEvidence> RunCycle()
    {
        var evidence = new List<UnloadEvidence>(Owners.Length);
        foreach (var (featureType, owner, expectedRoutes) in Owners)
            evidence.Add(MapAndRelease(featureType, owner, expectedRoutes));

        return evidence;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static UnloadEvidence MapAndRelease(Type featureType, string owner, int expectedRoutes)
    {
        var assemblyPath = featureType.Assembly.Location;
        var loadContext = new ProductionApiLoadContext($"Elsa.Wave2.{owner}.{Guid.NewGuid():N}");
        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
        var collectibleFeatureType = assembly.GetType(featureType.FullName!, throwOnError: true)!;
        var feature = Activator.CreateInstance(collectibleFeatureType)
            ?? throw new InvalidOperationException($"Could not construct '{collectibleFeatureType.FullName}'.");
        var services = new ServiceCollection();
        var configureServices = collectibleFeatureType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"'{collectibleFeatureType.FullName}' has no ConfigureServices method.");
        configureServices.Invoke(feature, [services]);
        using var provider = services.BuildServiceProvider();
        var routeBuilder = new CollectibleRouteBuilder(provider);
        var mapEndpoints = collectibleFeatureType.GetMethod("MapEndpoints", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"'{collectibleFeatureType.FullName}' has no MapEndpoints method.");
        mapEndpoints.Invoke(feature, [routeBuilder, null]);

        var routes = routeBuilder.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId == owner)
            .ToArray();
        Assert.Equal(expectedRoutes, routes.Length);
        var representative = routes[0];

        var serializerEvidence = JsonSerializer.Serialize(new
        {
            owner,
            routes = routes.Select(route => route.RoutePattern.RawText).Order(StringComparer.Ordinal),
            responseTypes = routes.SelectMany(route => route.Metadata.OfType<IProducesResponseTypeMetadata>())
                .Where(metadata => metadata.Type is not null)
                .Select(metadata => metadata.Type!.FullName)
                .Order(StringComparer.Ordinal)
        });
        Assert.Contains(owner, serializerEvidence, StringComparison.Ordinal);

        var loadContextReference = new WeakReference(loadContext);
        var assemblyReference = new WeakReference(assembly);
        var featureTypeReference = new WeakReference(collectibleFeatureType);
        var endpointReference = new WeakReference(representative);
        routeBuilder.DataSources.Clear();
        loadContext.Unload();

        return new UnloadEvidence(loadContextReference, assemblyReference, featureTypeReference, endpointReference, routes.Length,
            $"Owner '{owner}' retained collectible route/DI/serializer state after disposal.");
    }

    private static void ForceCollection()
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed record UnloadEvidence(
        WeakReference LoadContext,
        WeakReference Assembly,
        WeakReference FeatureType,
        WeakReference Endpoint,
        int RouteCount,
        string Diagnostic);

    private sealed class ProductionApiLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CollectibleRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
