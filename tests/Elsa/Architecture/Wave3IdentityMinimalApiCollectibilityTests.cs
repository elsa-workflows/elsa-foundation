using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Architecture.Tests;

/// <summary>Repeatedly proves both Wave 3 identity owners release route, DI, auth, and source-generated JSON state.</summary>
public sealed class Wave3IdentityMinimalApiCollectibilityTests
{
    private static readonly (string Owner, string AssemblyPath, string MapperType, string MapperMethod, string FeatureType, string ContextType, string TypeInfo)[] Owners =
    [
        ("Elsa.Foundation.Identity.Api", typeof(FoundationIdentityApiFeature).Assembly.Location,
            typeof(FoundationIdentityApi).FullName!, nameof(FoundationIdentityApi.MapFoundationIdentityApi),
            typeof(FoundationIdentityApiFeature).FullName!, "Elsa.Foundation.Identity.Api.FoundationIdentityApiJsonContext", "IdentityBootstrapResponse"),
        ("Elsa.Foundation.Identity.AspNetCoreIdentity", typeof(AspNetCoreIdentityFeature).Assembly.Location,
            typeof(AspNetCoreIdentityApi).FullName!, nameof(AspNetCoreIdentityApi.MapAspNetCoreIdentityApi),
            typeof(AspNetCoreIdentityFeature).FullName!, "Elsa.Foundation.Identity.AspNetCoreIdentity.AspNetCoreIdentityJsonContext", "AuthSession")
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
        (string Owner, string AssemblyPath, string MapperType, string MapperMethod, string FeatureType, string ContextType, string TypeInfo) owner,
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
        featureType.GetMethod("ConfigureServices")!.Invoke(feature, [services]);
        using var serviceProvider = services.BuildServiceProvider();
        var routes = new CollectibleRouteBuilder(serviceProvider);
        var mapperType = assembly.GetType(owner.MapperType, throwOnError: true)!;
        var mapper = mapperType.GetMethod(owner.MapperMethod, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        mapper.Invoke(null, [routes]);
        var routeCount = routes.DataSources.Sum(source => source.Endpoints.Count);
        Assert.True(routeCount > 0, $"{owner.Owner} published no routes.");

        var contextType = assembly.GetType(owner.ContextType, throwOnError: true)!;
        var context = (System.Text.Json.JsonSerializerContext)contextType.GetProperty("Default", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var typeInfo = (JsonTypeInfo)contextType.GetProperty(owner.TypeInfo, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(context)!;
        _ = JsonSerializer.Serialize((object?)null, typeInfo);
        var result = Results.Json(null, typeInfo);
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Response.Body = new MemoryStream();
        result.ExecuteAsync(httpContext).GetAwaiter().GetResult();

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
        result = null;
        httpContext = null;
        return new Evidence(cycleId, loadContextReference, assemblyReference, mapperReference);
    }

    private sealed record Evidence(Guid CycleId, WeakReference LoadContext, WeakReference Assembly, WeakReference MapperType);
}
