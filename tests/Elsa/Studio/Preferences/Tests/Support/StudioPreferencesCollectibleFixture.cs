using Elsa.Api.AspNetCore;
using NativeEndpoints;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Studio.Preferences.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Preferences.Tests.Support;

/// <summary>
/// A production Studio Preferences API lifecycle and weak-reference-only observation handles.
/// </summary>
public sealed class StudioPreferencesCollectibleCycle : IDisposable
{
    private bool _disposed;

    internal StudioPreferencesCollectibleCycle(
        Guid cycleId,
        RetentionStage requestedStage,
        string assemblyName,
        string mapperName,
        int routeCount,
        WeakReference loadContext,
        WeakReference assembly,
        WeakReference endpointType)
    {
        CycleId = cycleId;
        RequestedStage = requestedStage;
        AssemblyName = assemblyName;
        MapperName = mapperName;
        RouteCount = routeCount;
        LoadContext = loadContext;
        Assembly = assembly;
        EndpointType = endpointType;
    }

    public Guid CycleId { get; }

    public RetentionStage RequestedStage { get; }

    public string AssemblyName { get; }

    public string MapperName { get; }

    public int RouteCount { get; }

    /// <summary>Weak reference to the collectible load context; never a strong context reference.</summary>
    public WeakReference LoadContext { get; }

    /// <summary>Weak reference to the collectible production API assembly.</summary>
    public WeakReference Assembly { get; }

    /// <summary>Weak reference to a type loaded from the production API assembly.</summary>
    public WeakReference EndpointType { get; }

    public UnloadEvidence VerifyCollection(int maxAttempts = UnloadEvidence.DefaultMaxCollectionAttempts) =>
        UnloadEvidence.Verify(CycleId, LoadContext, Assembly, EndpointType, maxAttempts);

    public void ReleaseRetention() => StudioPreferencesCollectibleFixture.ReleaseRetention(CycleId);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseRetention();
    }
}

/// <summary>
/// Loads the production Studio Preferences API assembly into a collectible context and invokes its
/// mapper or feature mapping seam without allowing a collectible object to cross the lifecycle boundary.
/// </summary>
public static class StudioPreferencesCollectibleFixture
{
    private const string ApiFeatureTypeName = "Elsa.Studio.Preferences.Api.StudioPreferencesApiFeature";
    private const string MapperTypeName = "Elsa.Studio.Preferences.Api.StudioPreferencesApi";
    private const string MapperMethodName = "MapStudioPreferencesApi";
    private static readonly ConcurrentDictionary<Guid, Action> ReleaseActions = new();
    private static int _assemblyNumber;

    public static StudioPreferencesCollectibleCycle Create(RetentionStage retentionStage)
    {
        if (retentionStage is not (RetentionStage.Route or RetentionStage.Services))
            throw new ArgumentOutOfRangeException(nameof(retentionStage), retentionStage,
                "The production fixture proves route and service-provider release only.");

        var cycleId = Guid.NewGuid();
        var assemblyName = $"Elsa.Studio.Preferences.Collectible.{Interlocked.Increment(ref _assemblyNumber)}";
        return CreateAndUnload(cycleId, assemblyName, retentionStage);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ReleaseRetention(Guid cycleId)
    {
        if (ReleaseActions.TryRemove(cycleId, out var release))
        {
            release();
            release = null;
        }

        RetentionStageProbe.Release(cycleId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StudioPreferencesCollectibleCycle CreateAndUnload(
        Guid cycleId,
        string assemblyName,
        RetentionStage retentionStage)
    {
        var productionAssemblyPath = typeof(StudioPreferencesApiFeature).Assembly.Location;
        if (string.IsNullOrWhiteSpace(productionAssemblyPath) || !File.Exists(productionAssemblyPath))
            throw new InvalidOperationException($"The production API assembly is not available at '{productionAssemblyPath}'.");

        var loadContext = new ProductionApiLoadContext(assemblyName);
        var assembly = loadContext.LoadFromAssemblyPath(productionAssemblyPath);
        var endpointType = assembly.GetType(ApiFeatureTypeName, throwOnError: true)!;
        var mapperName = InvokeProductionMapper(assembly);
        var routeCount = 0;

        switch (retentionStage)
        {
            case RetentionStage.Route:
            {
                var routeBuilder = new CollectibleRouteBuilder(CreateRouteBuilderServices());
                InvokeMapper(routeBuilder, assembly);
                var routeOwner = new RouteOwner(routeBuilder.DataSources.ToArray());
                routeCount = routeOwner.CountEndpoints();
                if (routeCount == 0)
                    throw new InvalidOperationException("The production Studio Preferences mapper published no endpoints.");

                ReleaseActions[cycleId] = routeOwner.Dispose;
                RetentionStageProbe.PublishRoute(cycleId, routeOwner);
                break;
            }
            case RetentionStage.Services:
            {
                var serviceProvider = BuildProductionServiceProvider(endpointType);
                var serviceOwner = new ServiceOwner(serviceProvider);
                ReleaseActions[cycleId] = serviceOwner.Dispose;
                RetentionStageProbe.PublishServices(cycleId, serviceOwner);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(retentionStage), retentionStage, null);
        }

        var loadContextReference = new WeakReference(loadContext);
        var assemblyReference = new WeakReference(assembly);
        var endpointTypeReference = new WeakReference(endpointType);
        loadContext.Unload();

        // Only strings, value types, and weak references cross this NoInlining boundary. In
        // particular, this cycle never stores an Assembly, Type, route, provider, or delegate.
        return new StudioPreferencesCollectibleCycle(
            cycleId,
            retentionStage,
            assemblyName,
            mapperName,
            routeCount,
            loadContextReference,
            assemblyReference,
            endpointTypeReference);
    }

    private static string InvokeProductionMapper(Assembly assembly)
    {
        var mapperType = assembly.GetType(MapperTypeName);
        var mapper = mapperType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == MapperMethodName);

        if (mapper is not null)
            return $"{mapperType!.FullName}.{mapper.Name}";

        var featureType = assembly.GetType(ApiFeatureTypeName, throwOnError: true)!;
        var featureMapper = featureType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "MapEndpoints" || method.Name.EndsWith(".MapEndpoints", StringComparison.Ordinal));

        if (featureMapper is not null)
            return $"{featureType.FullName}.{featureMapper.Name}";

        throw new InvalidOperationException(
            "The collectible production API assembly does not expose MapStudioPreferencesApi or IWebShellFeature.MapEndpoints.");
    }

    private static void InvokeMapper(CollectibleRouteBuilder routeBuilder, Assembly assembly)
    {
        var mapperType = assembly.GetType(MapperTypeName);
        var mapper = mapperType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == MapperMethodName);

        if (mapper is not null)
        {
            mapper.Invoke(null, BuildArguments(mapper, routeBuilder));
            return;
        }

        var featureType = assembly.GetType(ApiFeatureTypeName, throwOnError: true)!;
        var feature = Activator.CreateInstance(featureType)
            ?? throw new InvalidOperationException($"Could not construct '{ApiFeatureTypeName}'.");
        var featureMapper = featureType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "MapEndpoints" || method.Name.EndsWith(".MapEndpoints", StringComparison.Ordinal));

        if (featureMapper is null)
            throw new InvalidOperationException(
                "The collectible production API assembly does not expose MapStudioPreferencesApi or IWebShellFeature.MapEndpoints.");

        featureMapper.Invoke(feature, BuildArguments(featureMapper, routeBuilder));
    }

    private static object?[] BuildArguments(MethodInfo method, IEndpointRouteBuilder routeBuilder) => method.GetParameters()
        .Select(parameter =>
        {
            if (parameter.ParameterType.IsInstanceOfType(routeBuilder))
                return (object?)routeBuilder;

            if (parameter.HasDefaultValue)
                return parameter.DefaultValue;

            if (!parameter.ParameterType.IsValueType || Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
                return null;

            throw new InvalidOperationException(
                $"Cannot invoke production mapper parameter '{parameter.Name}' of type '{parameter.ParameterType}'.");
        })
        .ToArray();

    private static IServiceProvider CreateRouteBuilderServices()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        // The owner assembly is deliberately loaded collectibly here, so its contract types ARE
        // collectible and the fail-closed lifetime boundary would reject the mapping outright.
        // This probe is the regime the explicit suppression exists for: the weak-reference
        // assertions prove the release honestly.
        services.SuppressEndpointLifetimeEnforcement();
        return services.AddElsaEndpoints().BuildServiceProvider();
    }

    private static IServiceProvider BuildProductionServiceProvider(Type endpointType)
    {
        var services = new ServiceCollection();
        var feature = Activator.CreateInstance(endpointType)
            ?? throw new InvalidOperationException($"Could not construct '{ApiFeatureTypeName}'.");
        var configureServices = endpointType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"'{ApiFeatureTypeName}' does not expose ConfigureServices.");

        configureServices.Invoke(feature, [services]);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

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

    private sealed class RouteOwner : IDisposable
    {
        private EndpointDataSource[] _dataSources;

        public RouteOwner(EndpointDataSource[] dataSources)
        {
            _dataSources = dataSources;
        }

        public int CountEndpoints() => _dataSources.Sum(dataSource => dataSource.Endpoints.Count);

        public void Dispose() => _dataSources = [];
    }

    private sealed class ServiceOwner(IServiceProvider serviceProvider) : IDisposable
    {
        public void Dispose() => (serviceProvider as IDisposable)?.Dispose();
    }
}
