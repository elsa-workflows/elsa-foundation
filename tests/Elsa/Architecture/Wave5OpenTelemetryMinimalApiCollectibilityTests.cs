using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class Wave5OpenTelemetryMinimalApiCollectibilityTests
{
    [Fact]
    public void Repeated_real_query_stream_otlp_and_provider_cycles_collect_owner()
    {
        var failures = new List<string>();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var evidence = CreateAndUnload(cycle);
            var result = UnloadEvidence.Verify(evidence.CycleId, evidence.LoadContext, evidence.Assembly, evidence.MapperType, 32);
            if (!result.Collected)
                failures.Add($"cycle {cycle}: {result.Diagnostic}; load-context={result.LoadContext.IsAlive}, assembly={result.Assembly.IsAlive}, mapper={result.EndpointType.IsAlive}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Evidence CreateAndUnload(int cycle)
    {
        var cycleId = Guid.NewGuid();
        var loadContext = new OpenTelemetryLoadContext($"Elsa.Wave5.OpenTelemetry.{cycle}.{Guid.NewGuid():N}");
        var assembly = loadContext.LoadFromAssemblyPath(typeof(OpenTelemetryFeature).Assembly.Location);
        Assert.NotSame(assembly, typeof(OpenTelemetryFeature).Assembly);
        Assert.Same(loadContext, AssemblyLoadContext.GetLoadContext(assembly));
        var featureType = assembly.GetType(typeof(OpenTelemetryFeature).FullName!, true)!;
        var feature = Activator.CreateInstance(featureType)!;
        var services = new ServiceCollection().AddLogging().AddRouting();
        services.AddAuthentication("wave5");
        services.AddAuthorization();
        services.AddFoundationIdentityAbstractions();
        featureType.GetMethod(nameof(OpenTelemetryFeature.ConfigureServices))!.Invoke(feature, [services]);
        services.AddSingleton<IOpenTelemetryLiveFeed, CollectibilityLiveFeed>();
        var serviceProvider = services.AddElsaEndpoints().BuildServiceProvider();
        var routes = new CollectibleRouteBuilder(serviceProvider);
        featureType.GetMethod(nameof(OpenTelemetryFeature.MapEndpoints))!.Invoke(feature, [routes, null]);
        var allRoutes = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Equal(11, allRoutes.Length);
        Assert.Equal(8, allRoutes.Count(route => route.RoutePattern.RawText?.StartsWith("/diagnostics/opentelemetry", StringComparison.Ordinal) == true || route.RoutePattern.RawText?.StartsWith("/_elsa/studio/diagnostics/opentelemetry", StringComparison.Ordinal) == true));
        Assert.Equal(3, allRoutes.Count(route => route.RoutePattern.RawText?.StartsWith("/elsa/otlp/v1/", StringComparison.Ordinal) == true));

        var serviceTypes = new[]
        {
            typeof(Elsa.Diagnostics.OpenTelemetry.Core.Contracts.IOpenTelemetryProvider),
            typeof(Elsa.Diagnostics.OpenTelemetry.Core.Contracts.IOpenTelemetryLiveFeed),
            assembly.GetType("Elsa.Diagnostics.OpenTelemetry.Ingestion.IOtlpRequestAuthenticator", true)!,
            assembly.GetType("Elsa.Diagnostics.OpenTelemetry.Endpoints.OpenTelemetryStreamItemSerializer", true)!,
            assembly.GetType("Elsa.Diagnostics.OpenTelemetry.Endpoints.OpenTelemetryTraceFilterBinder", true)!
        };
        foreach (var type in serviceTypes)
            Assert.NotNull(serviceProvider.GetService(type));

        ExecuteRoute(allRoutes, "/diagnostics/opentelemetry/resources/search", HttpMethods.Post, "{}", serviceProvider);
        foreach (var signal in new[] { "traces", "metrics", "logs" })
            ExecuteRoute(allRoutes, $"/elsa/otlp/v1/{signal}", HttpMethods.Post, string.Empty, serviceProvider, IPAddress.Loopback);
        ExecuteTypedSerialization(serviceProvider, assembly);
        ExecuteCompletedStream(allRoutes, serviceProvider);
        ExecuteCancelledStream(allRoutes, serviceProvider);

        var mapperType = featureType;
        var loadContextReference = new WeakReference(loadContext);
        var assemblyReference = new WeakReference(assembly);
        var mapperReference = new WeakReference(mapperType);
        routes.DataSources.Clear();
        serviceProvider.Dispose();
        feature = null;
        featureType = null!;
        serviceTypes = null!;
        allRoutes = null!;
        routes = null!;
        services = null!;
        loadContext.Unload();
        mapperType = null!;
        assembly = null!;
        serviceProvider = null!;
        loadContext = null!;
        return new Evidence(cycleId, loadContextReference, assemblyReference, mapperReference);
    }

    private static void ExecuteRoute(IEnumerable<RouteEndpoint> routes, string path, string method, string body, IServiceProvider services, IPAddress? remoteAddress = null)
    {
        var endpoint = routes.Single(route => string.Equals(route.RoutePattern.RawText, path, StringComparison.Ordinal));
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.ContentType = method == HttpMethods.Post && path.StartsWith("/elsa/", StringComparison.Ordinal) ? "application/x-protobuf" : "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        if (remoteAddress is not null)
            context.Connection.RemoteIpAddress = remoteAddress;
        endpoint.RequestDelegate!(context).GetAwaiter().GetResult();
        Assert.True(context.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status204NoContent);
    }

    private static void ExecuteCancelledStream(IEnumerable<RouteEndpoint> routes, IServiceProvider services)
    {
        var endpoint = routes.Single(route => route.RoutePattern.RawText?.StartsWith("/_elsa/studio/diagnostics/opentelemetry/stream", StringComparison.Ordinal) == true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellation.Token };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/_elsa/studio/diagnostics/opentelemetry/stream";
        context.Response.Body = new MemoryStream();
        endpoint.RequestDelegate!(context).GetAwaiter().GetResult();
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static void ExecuteCompletedStream(IEnumerable<RouteEndpoint> routes, IServiceProvider services)
    {
        var endpoint = routes.Single(route => route.RoutePattern.RawText?.StartsWith("/_elsa/studio/diagnostics/opentelemetry/stream", StringComparison.Ordinal) == true);
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/_elsa/studio/diagnostics/opentelemetry/stream";
        context.Response.Body = new MemoryStream();
        endpoint.RequestDelegate!(context).GetAwaiter().GetResult();
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("event: resource", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()), StringComparison.Ordinal);
    }

    private static void ExecuteTypedSerialization(IServiceProvider services, Assembly assembly)
    {
        var serializerType = assembly.GetType("Elsa.Diagnostics.OpenTelemetry.Endpoints.OpenTelemetryStreamItemSerializer", true)!;
        var serializer = services.GetRequiredService(serializerType);
        var method = serializerType.GetMethod("Serialize", [typeof(Elsa.Diagnostics.OpenTelemetry.Core.Models.TelemetryResource)])
                     ?? throw new InvalidOperationException("The owner serializer did not expose the typed resource contract.");
        var value = new Elsa.Diagnostics.OpenTelemetry.Core.Models.TelemetryResource(
            "resource-cycle",
            "service-cycle",
            null,
            "dotnet",
            new Dictionary<string, string?>(),
            DateTimeOffset.UtcNow,
            Elsa.Diagnostics.OpenTelemetry.Core.Models.TelemetryResourceStatus.Active);
        var serialized = method.Invoke(serializer, [value]) as string;
        Assert.Contains("resource-cycle", serialized, StringComparison.Ordinal);
    }

    private sealed record Evidence(Guid CycleId, WeakReference LoadContext, WeakReference Assembly, WeakReference MapperType);

    private sealed class CollectibleRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class OpenTelemetryLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CollectibilityLiveFeed : Elsa.Diagnostics.OpenTelemetry.Core.Contracts.IOpenTelemetryLiveFeed
    {
        private int _subscriptionCount;

        public ValueTask PublishAsync(
            Elsa.Diagnostics.OpenTelemetry.Core.Models.OpenTelemetryBatch batch,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public async IAsyncEnumerable<Elsa.Diagnostics.OpenTelemetry.Core.Models.OpenTelemetryStreamItem> SubscribeAsync(
            Elsa.Diagnostics.OpenTelemetry.Core.Models.OpenTelemetryTraceFilter filter,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var subscription = Interlocked.Increment(ref _subscriptionCount);
            yield return new Elsa.Diagnostics.OpenTelemetry.Core.Models.OpenTelemetryStreamItem
            {
                Resource = new Elsa.Diagnostics.OpenTelemetry.Core.Models.TelemetryResource(
                    "resource-cycle",
                    "service-cycle",
                    null,
                    "dotnet",
                    new Dictionary<string, string?>(),
                    DateTimeOffset.UtcNow,
                    Elsa.Diagnostics.OpenTelemetry.Core.Models.TelemetryResourceStatus.Active)
            };
            if (subscription > 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
