using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CShells.AspNetCore.Features;
using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using NativeEndpoints;

namespace Elsa.Api.Compatibility.Testing.Collectibility;

/// <summary>
/// Describes one production module whose feature assembly is loaded into a collectible context. The
/// module supplies only what its feature cannot compose for itself: the host services around it and
/// any further module types whose release the cycle must prove.
/// </summary>
public sealed class CollectibleModule
{
    /// <summary>
    /// The module's <see cref="IWebShellFeature"/> as the test references it. Only its assembly location and
    /// full name are used; the fixture loads and instantiates a private collectible copy.
    /// </summary>
    public required Type FeatureType { get; init; }

    public required string AssemblyNamePrefix { get; init; }

    /// <summary>Host composition the feature expects around it (configuration, identity, OpenAPI, ...).</summary>
    public Action<IServiceCollection>? ConfigureHost { get; init; }

    /// <summary>Full names of further module types whose collection the evidence also proves.</summary>
    public IReadOnlyList<string> ObservedTypeNames { get; init; } = [];
}

/// <summary>
/// One composed production module inside its collectible context: its services, its mapped endpoints, and
/// the collectible assembly. It exists only inside the fixture's no-inlining boundary and the module's
/// observation delegate; nothing may keep it or anything resolved from it.
/// </summary>
public sealed class CollectibleModuleHost : IDisposable
{
    private readonly MutableEndpointDataSource _endpoints;
    private ServiceProvider? _services;

    internal CollectibleModuleHost(ServiceProvider services, MutableEndpointDataSource endpoints, Assembly moduleAssembly)
    {
        _services = services;
        _endpoints = endpoints;
        ModuleAssembly = moduleAssembly;
    }

    public IServiceProvider Services => _services ?? throw new ObjectDisposedException(nameof(CollectibleModuleHost));

    /// <summary>The collectible module assembly, for reflection over module-private types.</summary>
    public Assembly ModuleAssembly { get; }

    public IReadOnlyList<Endpoint> Endpoints => _endpoints.Endpoints;

    public IReadOnlyList<EndpointDataSource> DataSources => [_endpoints];

    public RouteEndpoint? FindEndpoint(string routeSuffix, string? httpMethod = null) =>
        Endpoints.OfType<RouteEndpoint>().FirstOrDefault(endpoint =>
            endpoint.RoutePattern.RawText?.EndsWith(routeSuffix, StringComparison.OrdinalIgnoreCase) == true &&
            (httpMethod is null ||
             endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(httpMethod, StringComparer.OrdinalIgnoreCase) == true));

    public void Dispose()
    {
        _endpoints.Replace([]);
        Interlocked.Exchange(ref _services, null)?.Dispose();
    }
}

/// <summary>
/// One production module lifecycle and its weak-only observation handles. <typeparamref name="TObservation"/>
/// is the module's own value-only record of what it exercised while its endpoints were live.
/// </summary>
public sealed class CollectibleModuleCycle<TObservation> : IDisposable
{
    internal CollectibleModuleCycle(
        Guid cycleId,
        RetentionStage requestedStage,
        string assemblyName,
        TObservation? observation,
        WeakReference loadContext,
        WeakReference assembly,
        WeakReference featureType,
        IReadOnlyList<WeakReference> observedTypes)
    {
        CycleId = cycleId;
        RequestedStage = requestedStage;
        AssemblyName = assemblyName;
        Observation = observation;
        LoadContext = loadContext;
        Assembly = assembly;
        FeatureType = featureType;
        ObservedTypes = observedTypes;
    }

    public Guid CycleId { get; }

    public RetentionStage RequestedStage { get; }

    public string AssemblyName { get; }

    /// <summary>What the module observed while mapped; <c>default</c> for a services-only cycle.</summary>
    public TObservation? Observation { get; }

    /// <summary>Weak reference to the collectible load context; never a strong context reference.</summary>
    public WeakReference LoadContext { get; }

    /// <summary>Weak reference to the collectible production assembly.</summary>
    public WeakReference Assembly { get; }

    /// <summary>Weak reference to the feature type loaded from the collectible assembly.</summary>
    public WeakReference FeatureType { get; }

    /// <summary>Weak references to the module's additionally observed types, in declaration order.</summary>
    public IReadOnlyList<WeakReference> ObservedTypes { get; }

    public UnloadEvidence VerifyCollection(int maxAttempts = UnloadEvidence.DefaultMaxCollectionAttempts) =>
        UnloadEvidence.Verify(CycleId, LoadContext, Assembly, FeatureType, maxAttempts, ObservedTypes);

    public void ReleaseRetention() => CollectibleModuleFixture.ReleaseRetention(CycleId);

    public void Dispose() => ReleaseRetention();
}

/// <summary>
/// Loads a production module's feature assembly into a collectible context, composes it the way a shell
/// would (host services, <see cref="IWebShellFeature.ConfigureServices"/>, <see cref="IWebShellFeature.MapEndpoints"/>),
/// lets the module observe the live composition, then unloads. Only strings, value types, the module's
/// observation, and weak references cross the no-inlining boundary.
/// </summary>
public static class CollectibleModuleFixture
{
    private static readonly ConcurrentDictionary<Guid, IDisposable> RetainedOwners = new();
    private static int _assemblyNumber;

    /// <summary>
    /// Creates one isolated lifecycle. <see cref="RetentionStage.Services"/> composes services without mapping
    /// endpoints and does not invoke <paramref name="observe"/>; every other stage maps and observes.
    /// <see cref="RetentionStage.Serializer"/> additionally materializes serializer metadata for every produced
    /// response type before retaining. Harness retention is covered by <see cref="CollectibleEndpointFixture"/>.
    /// </summary>
    public static CollectibleModuleCycle<TObservation> Create<TObservation>(
        CollectibleModule module,
        RetentionStage retentionStage,
        Func<CollectibleModuleHost, TObservation> observe)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(observe);
        if (retentionStage is RetentionStage.Harness)
            throw new ArgumentOutOfRangeException(nameof(retentionStage), retentionStage,
                "Harness retention is covered by the shared CollectibleEndpointFixture.");

        var assemblyName = $"{module.AssemblyNamePrefix}.{Interlocked.Increment(ref _assemblyNumber)}";
        return CreateAndUnload(module, Guid.NewGuid(), assemblyName, retentionStage, observe);
    }

    /// <summary>Releases the deliberate retention for a cycle. It is safe to call repeatedly.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ReleaseRetention(Guid cycleId)
    {
        if (RetainedOwners.TryRemove(cycleId, out var owner))
            owner.Dispose();

        RetentionStageProbe.Release(cycleId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CollectibleModuleCycle<TObservation> CreateAndUnload<TObservation>(
        CollectibleModule module,
        Guid cycleId,
        string assemblyName,
        RetentionStage retentionStage,
        Func<CollectibleModuleHost, TObservation> observe)
    {
        var productionPath = module.FeatureType.Assembly.Location;
        if (string.IsNullOrWhiteSpace(productionPath) || !File.Exists(productionPath))
            throw new InvalidOperationException($"The production module assembly is unavailable at '{productionPath}'.");

        var loadContext = new ProductionModuleLoadContext(assemblyName);
        var assembly = loadContext.LoadFromAssemblyPath(productionPath);
        var featureType = assembly.GetType(module.FeatureType.FullName!, throwOnError: true)!;
        var observedTypes = module.ObservedTypeNames
            .Select(name => new WeakReference(assembly.GetType(name, throwOnError: true)!))
            .ToArray();
        // The feature interface lives in the default context; only its implementation is collectible.
        var feature = (IWebShellFeature)(Activator.CreateInstance(featureType)
            ?? throw new InvalidOperationException($"Could not construct '{featureType.FullName}'."));

        var mapEndpoints = retentionStage is not RetentionStage.Services;
        var host = ComposeHost(module, feature, assembly, mapEndpoints);
        var observation = mapEndpoints ? observe(host) : default;
        IDisposable owner = retentionStage is RetentionStage.Serializer
            ? new SerializerOwner(host, MaterializeResponseSerializers(host))
            : host;

        switch (retentionStage)
        {
            case RetentionStage.Clean:
                owner.Dispose();
                break;
            case RetentionStage.Route:
                RetentionStageProbe.PublishRoute(cycleId, owner);
                break;
            case RetentionStage.Services:
                RetentionStageProbe.PublishServices(cycleId, owner);
                break;
            case RetentionStage.Serializer:
                RetentionStageProbe.PublishSerializer(cycleId, owner);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(retentionStage), retentionStage, null);
        }

        if (retentionStage is not RetentionStage.Clean)
            RetainedOwners[cycleId] = owner;

        var loadContextReference = new WeakReference(loadContext);
        var assemblyReference = new WeakReference(assembly);
        var featureTypeReference = new WeakReference(featureType);
        loadContext.Unload();

        // No collectible object, Type, Assembly, route, provider, or delegate leaves this boundary.
        return new CollectibleModuleCycle<TObservation>(
            cycleId,
            retentionStage,
            assemblyName,
            observation,
            loadContextReference,
            assemblyReference,
            featureTypeReference,
            observedTypes);
    }

    private static CollectibleModuleHost ComposeHost(CollectibleModule module, IWebShellFeature feature, Assembly assembly, bool mapEndpoints)
    {
        var endpoints = new MutableEndpointDataSource();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new CollectibleHostEnvironment());
        services.AddSingleton<EndpointDataSource>(endpoints);
        // The module assembly is deliberately loaded collectibly, so its contract types ARE collectible and the
        // fail-closed endpoint lifetime boundary would reject the mapping outright. This probe is the regime the
        // explicit suppression exists for: the weak-reference assertions prove the release honestly.
        services.SuppressEndpointLifetimeEnforcement();
        services.AddElsaEndpoints();
        module.ConfigureHost?.Invoke(services);
        feature.ConfigureServices(services);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        if (mapEndpoints)
        {
            var routeBuilder = new CollectibleRouteBuilder(provider);
            feature.MapEndpoints(routeBuilder, environment: null);
            endpoints.Replace(routeBuilder.DataSources.SelectMany(source => source.Endpoints).ToArray());
        }

        return new CollectibleModuleHost(provider, endpoints, assembly);
    }

    private static JsonSerializerOptions MaterializeResponseSerializers(CollectibleModuleHost host)
    {
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        foreach (var type in host.Endpoints
                     .SelectMany(endpoint => endpoint.Metadata.OfType<IProducesResponseTypeMetadata>())
                     .Select(metadata => metadata.Type)
                     .Where(type => type is not null && type != typeof(void))
                     .Distinct())
        {
            _ = serializerOptions.GetTypeInfo(type!);
        }

        return serializerOptions;
    }

    private sealed class ProductionModuleLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
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

    private sealed class SerializerOwner(CollectibleModuleHost host, JsonSerializerOptions serializerOptions) : IDisposable
    {
        public void Dispose()
        {
            serializerOptions.TypeInfoResolver = null;
            host.Dispose();
        }
    }

    private sealed class CollectibleHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = typeof(CollectibleModuleFixture).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>
/// The host-registered endpoint source a composed module publishes into, so API Explorer and OpenAPI see the
/// mapped endpoints through the same provider. Replacing its contents signals the change token, and clearing
/// it on release drops the last host-side reference to the collectible endpoints.
/// </summary>
internal sealed class MutableEndpointDataSource : EndpointDataSource
{
    private IReadOnlyList<Endpoint> _endpoints = [];
    private CancellationTokenSource _changeTokenSource = new();

    public override IReadOnlyList<Endpoint> Endpoints => Volatile.Read(ref _endpoints);

    public override IChangeToken GetChangeToken() =>
        new CancellationChangeToken(Volatile.Read(ref _changeTokenSource).Token);

    public void Replace(IReadOnlyList<Endpoint> endpoints)
    {
        Volatile.Write(ref _endpoints, endpoints);
        Interlocked.Exchange(ref _changeTokenSource, new CancellationTokenSource()).Cancel();
    }
}
