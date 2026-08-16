using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json.Serialization;
using Elsa.Agent.Api;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave4AgentFastEndpointsCollection.Name)]
public sealed class Wave4AgentCollectibilityTests
{
    [Fact]
    public void Three_real_route_publication_cycles_release_agent_assembly_routes_di_and_serializer_metadata()
    {
        var failures = new List<string>();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var evidence = CreateAndUnload(cycle);
            var unload = UnloadEvidence.Verify(evidence.Id, evidence.LoadContext, evidence.Assembly, evidence.MapperType, 32);
            if (!unload.Collected || !WaitForCollection(evidence.References))
                failures.Add($"cycle {cycle}: {unload.Diagnostic ?? "retained"}; alive={string.Join(",", evidence.References.Where(pair => pair.Value.IsAlive).Select(pair => pair.Key))}");
        }

        Assert.Empty(failures);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Evidence CreateAndUnload(int cycle)
    {
        var path = typeof(AgentApi).Assembly.Location;
        var loadContext = new Wave4LoadContext($"Elsa.Wave4.Agent.{cycle}.{Guid.NewGuid():N}");
        var assembly = loadContext.LoadFromAssemblyPath(path);
        var mapperType = assembly.GetType("Elsa.Agent.Api.AgentApi", true)!;
        var mapper = mapperType.GetMethod("MapAgentApi", BindingFlags.Public | BindingFlags.Static)!;
        var featureType = assembly.GetType("Elsa.Agent.Api.FoundationAgentApiFeature", true)!;
        var feature = Activator.CreateInstance(featureType)!;
        var descriptors = new ServiceCollection().AddLogging().AddRouting();
        featureType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.Instance)!.Invoke(feature, [descriptors]);
        var services = descriptors.BuildServiceProvider();
        var routes = new Wave4RouteBuilder(services);
        mapper.Invoke(null, [routes]);
        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).ToArray();
        Assert.Equal(11, endpoints.Length);
        var contextType = assembly.GetType("Elsa.Agent.Api.AgentJsonContext", true)!;
        var context = contextType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!.GetValue(null) as JsonSerializerContext;
        Assert.NotNull(context);
        Assert.NotNull(contextType.GetProperty("AgentApiResponseAgentBootstrapResponse", BindingFlags.Public | BindingFlags.Instance)!.GetValue(context));

        var id = Guid.NewGuid();
        var references = new Dictionary<string, WeakReference>(StringComparer.Ordinal)
        {
            ["load-context"] = new(loadContext),
            ["assembly"] = new(assembly),
            ["mapper-type"] = new(mapperType),
            ["feature"] = new(feature),
            ["services"] = new(services),
            ["context"] = new(context)
        };
        foreach (var endpoint in endpoints.Select((value, index) => (value, index)))
            references[$"endpoint-{endpoint.index}"] = new(endpoint.value);

        routes.DataSources.Clear();
        services.Dispose();
        loadContext.Unload();
        return new(id, references["load-context"], references["assembly"], references["mapper-type"], references);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool WaitForCollection(IReadOnlyDictionary<string, WeakReference> references)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            if (references.All(reference => !reference.Value.IsAlive))
                return true;
        }

        return false;
    }

    private sealed record Evidence(Guid Id, WeakReference LoadContext, WeakReference Assembly, WeakReference MapperType, IReadOnlyDictionary<string, WeakReference> References);

    private sealed class Wave4RouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class Wave4LoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }
}
