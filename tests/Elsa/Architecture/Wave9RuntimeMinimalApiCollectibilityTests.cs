using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Executes the Runtime mapper, generated serializer, policy evaluator, and a real route delegate in three collectible cycles.</summary>
[Collection(Wave9RuntimeCollectibilityCollection.Name)]
public sealed class Wave9RuntimeMinimalApiCollectibilityTests
{
    [Fact]
    public void Runtime_owner_is_collectible_after_real_mapping_delegate_and_serializer_use()
    {
        var failures = new List<string>();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var evidence = CreateAndRelease(cycle);
            var collected = WaitForCollection(evidence.References);
            var unload = UnloadEvidence.Verify(evidence.CycleId, evidence.LoadContext, evidence.Assembly, evidence.MapperType, 32);
            if (!unload.Collected || !collected)
                failures.Add($"cycle {cycle}: {unload.Diagnostic ?? "owner retained"}; alive={string.Join(",", evidence.References.Where(x => x.Value.IsAlive).Select(x => x.Key))}");
        }

        Assert.Empty(failures);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Evidence CreateAndRelease(int cycle)
    {
        var sourcePath = typeof(WorkflowsRuntimeApiFeature).Assembly.Location;
        var bytes = File.ReadAllBytes(sourcePath);
        var loadContext = new RuntimeLoadContext($"Elsa.Wave9.Runtime.{cycle}.{Guid.NewGuid():N}");
        Assembly assembly;
        using (var stream = new MemoryStream(bytes, writable: false))
            assembly = loadContext.LoadFromStream(stream);

        var featureType = assembly.GetType(typeof(WorkflowsRuntimeApiFeature).FullName!, true)!;
        var mapperType = assembly.GetType(typeof(WorkflowsRuntimeApi).FullName!, true)!;
        var mapper = mapperType.GetMethod(nameof(WorkflowsRuntimeApi.MapWorkflowsRuntimeApi), BindingFlags.Public | BindingFlags.Static)!;
        var feature = Activator.CreateInstance(featureType)!;
        var descriptors = new ServiceCollection().AddLogging().AddRouting();
        descriptors.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(["Wave9Collectibility"], StringComparer.Ordinal));
        descriptors.AddSingleton<IRequestSender, CollectibleRequestSender>();
        descriptors.AddSingleton<ICommandSender, CollectibleCommandSender>();
        featureType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.Instance)!.Invoke(feature, [descriptors]);
        var services = descriptors.BuildServiceProvider();
        var routes = new RouteBuilder(services);
        mapper.Invoke(null, [routes]);
        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Equal(24, endpoints.Length);

        var representative = endpoints.SingleOrDefault(endpoint => endpoint.RoutePattern.RawText == "runtime/workflows/instances/{workflowExecutionId}")
            ?? throw new InvalidOperationException($"Runtime instance route was not published. Routes: {string.Join(",", endpoints.Select(endpoint => endpoint.RoutePattern.RawText))}");
        var responseBody = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.RouteValues["workflowExecutionId"] = "collectible-instance";
        http.Response.Body = responseBody;
        representative.RequestDelegate!(http).GetAwaiter().GetResult();
        Assert.Equal(StatusCodes.Status404NotFound, http.Response.StatusCode);

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard)], "Wave9Collectibility"));
        var evaluator = services.GetRequiredService<IPermissionEvaluator>();
        Assert.True(evaluator.EvaluateAsync(new PermissionEvaluationContext(principal, "workflow-runtime.read")).GetAwaiter().GetResult().Succeeded);

        var contextType = assembly.GetType("Elsa.Workflows.Runtime.Api.WorkflowsRuntimeJsonContext", true)!;
        var serializerContext = (JsonSerializerContext)contextType.GetProperty("Default", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var typeInfo = (JsonTypeInfo)contextType.GetProperty("WorkflowInstanceListView", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(serializerContext)!;
        var value = JsonSerializer.Deserialize("{\"items\":[],\"nextCursor\":null,\"hasNext\":false,\"count\":0,\"totalCount\":0}", typeInfo)!;
        using var output = new MemoryStream();
        JsonSerializer.Serialize(output, value, typeInfo);
        Assert.NotEmpty(output.ToArray());

        var references = new Dictionary<string, WeakReference>(StringComparer.Ordinal)
        {
            ["load-context"] = new(loadContext),
            ["assembly"] = new(assembly),
            ["mapper-type"] = new(mapperType),
            ["feature-type"] = new(featureType),
            ["feature"] = new(feature),
            ["services"] = new(services),
            ["serializer-context"] = new(serializerContext),
            ["serializer-type-info"] = new(typeInfo),
            ["http-context"] = new(http),
            ["response-body"] = new(responseBody)
        };
        foreach (var endpoint in endpoints.Select((endpoint, index) => (endpoint, index)))
            references[$"endpoint-{endpoint.index}"] = new(endpoint.endpoint);

        routes.DataSources.Clear();
        services.Dispose();
        responseBody.Dispose();
        representative = null!;
        endpoints = null!;
        routes = null!;
        descriptors = null!;
        feature = null!;
        featureType = null!;
        mapper = null!;
        mapperType = null!;
        serializerContext = null!;
        typeInfo = null!;
        value = null!;
        http = null!;
        output.Dispose();
        services = null!;
        loadContext.Unload();
        loadContext = null!;
        assembly = null!;
        return new(Guid.NewGuid(), references["load-context"], references["assembly"], references["mapper-type"], references);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool WaitForCollection(IReadOnlyDictionary<string, WeakReference> references)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            if (references.All(x => !x.Value.IsAlive))
                return true;
        }
        return false;
    }

    private sealed record Evidence(Guid CycleId, WeakReference LoadContext, WeakReference Assembly, WeakReference MapperType, IReadOnlyDictionary<string, WeakReference> References);

    private sealed class RouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = services;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class RuntimeLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CollectibleRequestSender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            Task.FromResult((T)RuntimeHelpers.GetUninitializedObject(typeof(T)));
    }

    private sealed class CollectibleCommandSender : ICommandSender
    {
        public Task<T> Send<T>(ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull =>
            Task.FromResult((T)RuntimeHelpers.GetUninitializedObject(typeof(T)));

        public Task Send(ICommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wave9RuntimeCollectibilityCollection
{
    public const string Name = "Wave 9 Runtime Minimal API collectibility";
}
