using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Repeatedly proves both Wave 3 identity owners release route, DI, auth, and source-generated JSON state.</summary>
[Collection(Wave3IdentityMinimalApiCollectibilityCollection.Name)]
public sealed class Wave3IdentityMinimalApiCollectibilityTests
{
    private static readonly (
        string Owner,
        string AssemblyPath,
        string MapperType,
        string MapperMethod,
        string FeatureType,
        string ContextType,
        string TypeInfo,
        string ResponseJson,
        string BinderRoute)[] Owners =
    [
        ("Elsa.Foundation.Identity.Api", typeof(FoundationIdentityApiFeature).Assembly.Location,
            typeof(FoundationIdentityApi).FullName!, nameof(FoundationIdentityApi.MapFoundationIdentityApi),
            typeof(FoundationIdentityApiFeature).FullName!, "Elsa.Foundation.Identity.Api.FoundationIdentityApiJsonContext", "IdentityBootstrapResponse",
            "{\"ownershipMode\":0,\"providers\":[]}", "_elsa/identity/refresh"),
        ("Elsa.Foundation.Identity.AspNetCoreIdentity", typeof(AspNetCoreIdentityFeature).Assembly.Location,
            typeof(AspNetCoreIdentityApi).FullName!, nameof(AspNetCoreIdentityApi.MapAspNetCoreIdentityApi),
            typeof(AspNetCoreIdentityFeature).FullName!, "Elsa.Foundation.Identity.AspNetCoreIdentity.AspNetCoreIdentityJsonContext", "AuthSession",
            "{\"status\":\"anonymous\",\"subject\":null,\"displayName\":null,\"tenantId\":null,\"roles\":[],\"permissions\":[],\"tokenFreshness\":\"none\",\"provider\":null}",
            "_elsa/identity/login")
    ];

    [Fact]
    public void Both_identity_owners_release_repeatedly_after_route_and_json_publication()
    {
        var failures = new List<string>();
        foreach (var owner in Owners)
        {
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var evidence = CreateAndUnload(owner, cycle);
                var unload = UnloadEvidence.Verify(evidence.CycleId, evidence.LoadContext, evidence.Assembly, evidence.MapperType, 32);
                if (!unload.Collected)
                    failures.Add($"{owner.Owner} cycle {cycle}: {unload.Diagnostic}");
            }
        }

        Assert.Empty(failures);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Evidence CreateAndUnload(
        (
            string Owner,
            string AssemblyPath,
            string MapperType,
            string MapperMethod,
            string FeatureType,
            string ContextType,
            string TypeInfo,
            string ResponseJson,
            string BinderRoute) owner,
        int cycle)
    {
        var cycleId = Guid.NewGuid();
        var loadContext = new ProductionApiLoadContext($"Elsa.Wave3.Identity.{owner.Owner}.{cycle}.{Guid.NewGuid():N}");
        var assemblyBytes = File.ReadAllBytes(owner.AssemblyPath);
        var assembly = (Assembly?)null;
        using (var stream = new MemoryStream(assemblyBytes, writable: false))
            assembly = loadContext.LoadFromStream(stream);

        var featureType = assembly.GetType(owner.FeatureType, throwOnError: true)!;
        var feature = Activator.CreateInstance(featureType)!;
        var services = new ServiceCollection().AddLogging().AddRouting();
        // The owner assembly is deliberately loaded collectibly here, so its contract types ARE
        // collectible and the fail-closed lifetime boundary would reject the mapping outright.
        // This probe is the regime the explicit suppression exists for: no host-lifetime OpenAPI
        // document survives the cycle, and the weak-reference assertions below prove the release.
        services.SuppressOpenApiLifetimeEnforcement();
        featureType.GetMethod("ConfigureServices")!.Invoke(feature, [services]);
        using var serviceProvider = services.BuildServiceProvider();
        var routes = new CollectibleRouteBuilder(serviceProvider);
        var mapperType = assembly.GetType(owner.MapperType, throwOnError: true)!;
        var mapper = mapperType.GetMethod(owner.MapperMethod, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        mapper.Invoke(null, [routes]);
        var publishedRoutes = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        var routeCount = publishedRoutes.Length;
        Assert.True(routeCount > 0, $"{owner.Owner} published no routes.");

        ExerciseConfiguredServicesAsync(owner.Owner, assembly, serviceProvider).GetAwaiter().GetResult();
        ExerciseMappedRequestBinderAsync(owner.BinderRoute, publishedRoutes, serviceProvider).GetAwaiter().GetResult();

        var contextType = assembly.GetType(owner.ContextType, throwOnError: true)!;
        var context = (JsonSerializerContext)contextType.GetProperty("Default", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var typeInfo = (JsonTypeInfo)contextType.GetProperty(owner.TypeInfo, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(context)!;
        var response = CreateResponse(owner.Owner, owner.ResponseJson, typeInfo);
        Assert.NotNull(response);
        var result = Results.Json(response, typeInfo);
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Response.Body = new MemoryStream();
        result.ExecuteAsync(httpContext).GetAwaiter().GetResult();
        Assert.True(httpContext.Response.Body.Length > 0, $"{owner.Owner} produced no typed JSON response.");

        routes.DataSources.Clear();
        serviceProvider.Dispose();
        var loadContextReference = new WeakReference(loadContext);
        var assemblyReference = new WeakReference(assembly);
        var mapperReference = new WeakReference(mapperType);
        loadContext.Unload();
        assembly = null;
        feature = null;
        mapper = null;
        mapperType = null;
        context = null;
        typeInfo = null;
        response = null;
        result = null;
        httpContext = null;
        return new Evidence(cycleId, loadContextReference, assemblyReference, mapperReference);
    }

    private static object? CreateResponse(string owner, string responseJson, JsonTypeInfo typeInfo) =>
        owner == "Elsa.Foundation.Identity.AspNetCoreIdentity"
            ? Activator.CreateInstance(
                typeInfo.Type,
                "anonymous",
                null,
                null,
                null,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                "none",
                null)
            : JsonSerializer.Deserialize(responseJson, typeInfo);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ExerciseConfiguredServicesAsync(string owner, Assembly assembly, IServiceProvider serviceProvider)
    {
        if (owner == "Elsa.Foundation.Identity.Api")
        {
            var optionsType = assembly.GetType("Elsa.Foundation.Identity.Api.FoundationIdentityApiOptions", throwOnError: true)!;
            var options = serviceProvider.GetRequiredService(typeof(IOptions<>).MakeGenericType(optionsType));
            var value = options.GetType().GetProperty(nameof(IOptions<object>.Value))!.GetValue(options)!;
            var schemes = (IEnumerable<string>)optionsType.GetProperty("InteractiveAuthSchemes")!.GetValue(value)!;
            Assert.NotEmpty(schemes);
            return;
        }

        var modules = serviceProvider.GetServices<IAuthenticationProviderModule>().ToArray();
        var module = Assert.Single(modules, candidate => candidate.GetType().Assembly == assembly);
        var descriptor = await module.DescribeAsync();
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Id));
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Challenge?.Scheme));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ExerciseMappedRequestBinderAsync(
        string binderRoute,
        IReadOnlyCollection<RouteEndpoint> routes,
        IServiceProvider serviceProvider)
    {
        var route = routes.Single(candidate =>
            candidate.RoutePattern.RawText?.TrimStart('/') == binderRoute
            && candidate.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("POST", StringComparer.OrdinalIgnoreCase) == true);
        var body = Encoding.UTF8.GetBytes("{");
        var context = new DefaultHttpContext { RequestServices = serviceProvider };
        context.Request.Method = "POST";
        context.Request.Path = "/" + binderRoute;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();

        await (route.RequestDelegate ?? throw new InvalidOperationException($"Route '{binderRoute}' has no request delegate."))(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    private sealed record Evidence(Guid CycleId, WeakReference LoadContext, WeakReference Assembly, WeakReference MapperType);

    private sealed class CollectibleRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class ProductionApiLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wave3IdentityMinimalApiCollectibilityCollection
{
    public const string Name = "Wave 3 Identity Minimal API collectibility";
}
