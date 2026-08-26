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
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Exercises every Wave 2 owner through real route, DI, serializer, and disposal cycles.</summary>
[Collection(Wave2MinimalApiUnloadabilityCollection.Name)]
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
        services.AddLogging();
        // The owner assembly is deliberately loaded collectibly here, so its contract types ARE
        // collectible and the fail-closed lifetime boundary would reject the mapping outright.
        // This probe is the regime the explicit suppression exists for: no host-lifetime OpenAPI
        // document survives the cycle, and the weak-reference assertions below prove the release.
        services.SuppressOpenApiLifetimeEnforcement();
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

        var serializerEvidence = ExerciseProductionSerializationAsync(owner, routes, provider).GetAwaiter().GetResult();
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> ExerciseProductionSerializationAsync(
        string owner,
        IReadOnlyCollection<RouteEndpoint> routes,
        IServiceProvider provider)
    {
        var route = owner switch
        {
            "Elsa.Activities.Bpmn.Interchange" => routes.Single(route => route.RoutePattern.RawText?.TrimStart('/') == "interchange/bpmn/analyze"),
            "Elsa.Modularity.Api" => routes.Single(route => route.RoutePattern.RawText?.TrimStart('/') == "modularity/features/apply"),
            "Elsa.Workflows.ExecutionEvidence" => routes.Single(route => route.RoutePattern.RawText?.TrimStart('/') == "_elsa/execution-evidence"
                && route.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("GET", StringComparer.OrdinalIgnoreCase) == true),
            "Elsa3.Activities.Design.Import" => routes.Single(route => route.RoutePattern.RawText?.TrimStart('/') == "migration/elsa3/reusable-activities/collections/{collectionHandle}/selection"),
            _ => throw new InvalidOperationException($"No serializer canary route is defined for '{owner}'.")
        };

        var method = route.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Single()
                     ?? throw new InvalidOperationException($"Route '{route.RoutePattern.RawText}' has no HTTP method metadata.");
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = method;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = "/" + route.RoutePattern.RawText!.TrimStart('/');
        context.Response.Body = new MemoryStream();

        switch (owner)
        {
            case "Elsa.Activities.Bpmn.Interchange":
                SetJsonBody(context, "{\"xml\":\"<definitions xmlns=\\\"http://www.omg.org/spec/BPMN/20100524/MODEL\\\"><process id=\\\"p\\\" /></definitions>\"}");
                break;
            case "Elsa.Modularity.Api":
                // Invalid JSON still takes the production typed request binder and the module's JSON error writer.
                SetJsonBody(context, "{");
                break;
            case "Elsa.Workflows.ExecutionEvidence":
                context.Request.QueryString = new QueryString("?correlationId=wave2-unload");
                break;
            case "Elsa3.Activities.Design.Import":
                context.Request.RouteValues["collectionHandle"] = "wave2-unload";
                context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "wave2-unload")],
                    "wave2-unload"));
                SetJsonBody(context, "{\"planId\":\"wave2-unload\",\"selectedSourceVersionIds\":[]}");
                break;
        }

        await (route.RequestDelegate ?? throw new InvalidOperationException($"Route '{route.RoutePattern.RawText}' has no request delegate."))(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true).ReadToEndAsync();
        Assert.NotEmpty(body);
        Assert.Contains("json", context.Response.ContentType ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        return JsonSerializer.Serialize(new
        {
            owner,
            route = route.RoutePattern.RawText,
            status = context.Response.StatusCode,
            contentType = context.Response.ContentType,
            bodyLength = body.Length
        });
    }

    private static void SetJsonBody(HttpContext context, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bytes.Length;
        context.Request.Body = new MemoryStream(bytes);
    }

    private static void ForceCollection()
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
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

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wave2MinimalApiUnloadabilityCollection
{
    public const string Name = "Wave 2 Minimal API unloadability";
}
