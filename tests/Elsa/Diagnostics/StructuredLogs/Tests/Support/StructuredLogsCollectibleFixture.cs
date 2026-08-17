using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Text;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Tests.Support;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Tests.Support;

/// <summary>
/// A production Structured Logs lifecycle with weak-reference-only observations crossing the
/// no-inlining boundary. The fixture intentionally uses reflection for the production mapper so this
/// evidence can be authored before the replacement mapper exists and cannot accidentally retain its
/// collectible types through a compile-time test reference.
/// </summary>
public sealed class StructuredLogsCollectibleCycle : IDisposable
{
    private bool _disposed;

    internal StructuredLogsCollectibleCycle(
        Guid cycleId,
        StructuredLogsRetentionStage requestedStage,
        string assemblyName,
        string mapperName,
        int routeCount,
        bool queryExercised,
        bool streamStarted,
        bool streamCancelled,
        bool serializerExercised,
        bool documentationGenerated,
        OpenApiCacheInspection openApiCache,
        WeakReference loadContext,
        WeakReference assembly,
        WeakReference endpointType)
    {
        CycleId = cycleId;
        RequestedStage = requestedStage;
        AssemblyName = assemblyName;
        MapperName = mapperName;
        RouteCount = routeCount;
        QueryExercised = queryExercised;
        StreamStarted = streamStarted;
        StreamCancelled = streamCancelled;
        SerializerExercised = serializerExercised;
        DocumentationGenerated = documentationGenerated;
        OpenApiCache = openApiCache;
        LoadContext = loadContext;
        Assembly = assembly;
        EndpointType = endpointType;
    }

    public Guid CycleId { get; }
    public StructuredLogsRetentionStage RequestedStage { get; }
    public string AssemblyName { get; }
    public string MapperName { get; }
    public int RouteCount { get; }
    public bool QueryExercised { get; }
    public bool StreamStarted { get; }
    public bool StreamCancelled { get; }
    public bool SerializerExercised { get; }
    public bool DocumentationGenerated { get; }
    public OpenApiCacheInspection OpenApiCache { get; }

    /// <summary>Weak reference to the collectible load context; never a strong context reference.</summary>
    public WeakReference LoadContext { get; }

    /// <summary>Weak reference to the collectible production assembly.</summary>
    public WeakReference Assembly { get; }

    /// <summary>Weak reference to a type loaded from the collectible production assembly.</summary>
    public WeakReference EndpointType { get; }

    public StructuredLogsUnloadEvidence VerifyCollection(int maxAttempts = StructuredLogsUnloadEvidence.DefaultMaxCollectionAttempts) =>
        StructuredLogsUnloadEvidence.Verify(CycleId, LoadContext, Assembly, EndpointType, DocumentationGenerated, OpenApiCache, maxAttempts);

    public void ReleaseRetention() => StructuredLogsCollectibleFixture.ReleaseRetention(CycleId);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseRetention();
    }
}

public enum StructuredLogsRetentionStage
{
    Clean = 0,
    ExercisedLifecycle = 1,
    OpenApi = 2,
    Harness = 3
}

/// <summary>
/// String/value/weak-reference-only OpenAPI cache evidence. It deliberately does not expose cached
/// contexts, endpoint metadata, <see cref="Type"/>, <see cref="MethodInfo"/>, or delegates.
/// </summary>
public sealed record OpenApiCacheInspection(
    int ContextCount,
    int ModuleOwnedTypeCount,
    int ModuleOwnedMethodInfoCount,
    int ModuleOwnedDelegateCount,
    string ModuleOwnedMetadataKinds,
    bool ServiceInspected)
{
    public static readonly OpenApiCacheInspection Empty = new(0, 0, 0, 0, string.Empty, false);

    public bool HasModuleOwnedMetadata =>
        ModuleOwnedTypeCount != 0 || ModuleOwnedMethodInfoCount != 0 || ModuleOwnedDelegateCount != 0;
}

/// <summary>Weak-reference-only unload evidence for one Structured Logs lifecycle.</summary>
public sealed class StructuredLogsUnloadEvidence
{
    public const int DefaultMaxCollectionAttempts = 24;
    private const int MaximumCollectionAttempts = 64;

    private StructuredLogsUnloadEvidence(
        Guid cycleId,
        StructuredLogsRetentionStage stage,
        WeakReference loadContext,
        WeakReference assembly,
        WeakReference endpointType,
        bool collected,
        int collectionAttempts,
        string diagnostic,
        OpenApiCacheInspection openApiCache)
    {
        CycleId = cycleId;
        Stage = stage;
        LoadContext = loadContext;
        Assembly = assembly;
        EndpointType = endpointType;
        Collected = collected;
        CollectionAttempts = collectionAttempts;
        Diagnostic = diagnostic;
        OpenApiCache = openApiCache;
    }

    public Guid CycleId { get; }
    public StructuredLogsRetentionStage Stage { get; }
    public WeakReference LoadContext { get; }
    public WeakReference Assembly { get; }
    public WeakReference EndpointType { get; }
    public bool Collected { get; }
    public int CollectionAttempts { get; }
    public string Diagnostic { get; }
    public OpenApiCacheInspection OpenApiCache { get; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static StructuredLogsUnloadEvidence Verify(
        Guid cycleId,
        WeakReference loadContext,
        WeakReference assembly,
        WeakReference endpointType,
        bool documentationGenerated,
        OpenApiCacheInspection openApiCache,
        int maxAttempts)
    {
        ArgumentNullException.ThrowIfNull(loadContext);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(endpointType);
        if (maxAttempts is < 1 or > MaximumCollectionAttempts)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, $"Must be between 1 and {MaximumCollectionAttempts}.");

        var attempts = 0;
        var collected = false;
        for (; attempts < maxAttempts; attempts++)
        {
            ForceCollection();
            if (!loadContext.IsAlive && !assembly.IsAlive && !endpointType.IsAlive)
            {
                collected = true;
                attempts++;
                break;
            }
        }

        var stage = collected
            ? StructuredLogsRetentionStage.Clean
            : StructuredLogsRetentionProbe.PublishedStage(cycleId, documentationGenerated);
        var diagnostic = collected
            ? string.Empty
            : Describe(stage, documentationGenerated, openApiCache);

        return new StructuredLogsUnloadEvidence(
            cycleId,
            stage,
            loadContext,
            assembly,
            endpointType,
            collected,
            attempts,
            diagnostic,
            openApiCache);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static string Describe(
        StructuredLogsRetentionStage stage,
        bool documentationGenerated,
        OpenApiCacheInspection openApiCache) => stage switch
    {
        StructuredLogsRetentionStage.ExercisedLifecycle =>
            "combined exercised route, DI/services, SSE, and serializer lifecycle owner retention",
        StructuredLogsRetentionStage.Harness => "harness retention",
        StructuredLogsRetentionStage.OpenApi when openApiCache.HasModuleOwnedMetadata =>
            "OpenAPI operation-context cache contains module-owned metadata; dynamic documentation lifetime is retained",
        StructuredLogsRetentionStage.OpenApi when documentationGenerated =>
            "OpenAPI generation retained the collectible context without a classified module metadata entry; external gcroot evidence is required",
        _ => "unclassified collectible retention"
    };
}

internal static class StructuredLogsRetentionProbe
{
    private sealed record Retained(Guid CycleId, StructuredLogsRetentionStage Stage, object Owner);

    private static readonly ConcurrentDictionary<Guid, Retained> RetainedOwners = new();
    private static readonly ConcurrentDictionary<Guid, StructuredLogsRetentionStage> ClassifiedStages = new();

    public static void Publish(Guid cycleId, StructuredLogsRetentionStage stage, object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!RetainedOwners.TryAdd(cycleId, new Retained(cycleId, stage, owner)))
            throw new InvalidOperationException($"A retention owner already exists for cycle '{cycleId}'.");
    }

    public static void Classify(Guid cycleId, StructuredLogsRetentionStage stage) => ClassifiedStages[cycleId] = stage;

    public static StructuredLogsRetentionStage PublishedStage(Guid cycleId, bool documentationGenerated)
    {
        if (RetainedOwners.TryGetValue(cycleId, out var retained))
            return retained.Stage;

        return ClassifiedStages.TryGetValue(cycleId, out var stage)
            ? stage
            : documentationGenerated ? StructuredLogsRetentionStage.OpenApi : StructuredLogsRetentionStage.Harness;
    }

    public static void Release(Guid cycleId)
    {
        if (RetainedOwners.TryRemove(cycleId, out var retained) && retained.Owner is IDisposable disposable)
            disposable.Dispose();

        ClassifiedStages.TryRemove(cycleId, out _);
    }
}

/// <summary>
/// Loads the production Structured Logs assembly into a collectible context, invokes its real mapper,
/// exercises query/stream/serialization/documentation, inspects OpenAPI's operation cache, and returns
/// only weak references and value diagnostics.
/// </summary>
public static class StructuredLogsCollectibleFixture
{
    private const string FeatureTypeName = "Elsa.Diagnostics.StructuredLogs.StructuredLogsFeature";
    private const string MapperTypeName = "Elsa.Diagnostics.StructuredLogs.Endpoints.StructuredLogsApi";
    private const string MapperMethodName = "MapStructuredLogsApi";
    private const string SerializerTypeName = "Elsa.Diagnostics.StructuredLogs.Endpoints.StructuredLogEntrySerializer";
    private static int _assemblyNumber;

    public static StructuredLogsCollectibleCycle Create(
        StructuredLogsRetentionStage retentionStage = StructuredLogsRetentionStage.Clean,
        bool generateDocumentation = true)
    {
        if (retentionStage == StructuredLogsRetentionStage.Harness)
            throw new ArgumentOutOfRangeException(nameof(retentionStage), retentionStage, "Harness retention is covered by the shared fixture.");

        var cycleId = Guid.NewGuid();
        var assemblyName = $"Elsa.Diagnostics.StructuredLogs.Collectible.{Interlocked.Increment(ref _assemblyNumber)}";
        return CreateAndUnload(cycleId, assemblyName, retentionStage, generateDocumentation);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ReleaseRetention(Guid cycleId) => StructuredLogsRetentionProbe.Release(cycleId);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StructuredLogsCollectibleCycle CreateAndUnload(
        Guid cycleId,
        string assemblyName,
        StructuredLogsRetentionStage retentionStage,
        bool generateDocumentation)
    {
        var productionPath = typeof(StructuredLogsFeature).Assembly.Location;
        if (string.IsNullOrWhiteSpace(productionPath) || !File.Exists(productionPath))
            throw new InvalidOperationException($"The production Structured Logs assembly is unavailable at '{productionPath}'.");

        var loadContext = new ProductionApiLoadContext(assemblyName);
        var assembly = loadContext.LoadFromAssemblyPath(productionPath);
        var featureType = assembly.GetType(FeatureTypeName, throwOnError: true)!;
        var mapperName = FindMapper(assembly);
        var lifecycle = BuildLifecycleOwner(featureType, assembly, generateDocumentation, out var routeCount, out var queryExercised, out var streamStarted, out var streamCancelled, out var serializerExercised, out var documentationGenerated, out var cacheInspection);

        if (routeCount == 0)
            throw new InvalidOperationException("The production Structured Logs mapper published no endpoints.");

        if (generateDocumentation && documentationGenerated)
            StructuredLogsRetentionProbe.Classify(cycleId, StructuredLogsRetentionStage.OpenApi);

        switch (retentionStage)
        {
            case StructuredLogsRetentionStage.Clean:
                lifecycle.Dispose();
                break;
            case StructuredLogsRetentionStage.ExercisedLifecycle:
                StructuredLogsRetentionProbe.Publish(cycleId, retentionStage, lifecycle);
                break;
            case StructuredLogsRetentionStage.OpenApi:
                lifecycle.Dispose();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(retentionStage), retentionStage, null);
        }

        var loadContextReference = new WeakReference(loadContext);
        var assemblyReference = new WeakReference(assembly);
        var endpointTypeReference = new WeakReference(featureType);
        loadContext.Unload();

        return new StructuredLogsCollectibleCycle(
            cycleId,
            retentionStage,
            assemblyName,
            mapperName,
            routeCount,
            queryExercised,
            streamStarted,
            streamCancelled,
            serializerExercised,
            documentationGenerated,
            cacheInspection,
            loadContextReference,
            assemblyReference,
            endpointTypeReference);
    }

    private static LifecycleOwner BuildLifecycleOwner(
        Type featureType,
        Assembly assembly,
        bool generateDocumentation,
        out int routeCount,
        out bool queryExercised,
        out bool streamStarted,
        out bool streamCancelled,
        out bool serializerExercised,
        out bool documentationGenerated,
        out OpenApiCacheInspection cacheInspection)
    {
        var services = CreateProductionServices(featureType);
        var routeBuilder = new CollectibleRouteBuilder(services);
        InvokeMapper(assembly, routeBuilder);
        var owner = new LifecycleOwner(routeBuilder.DataSources.ToArray(), services);
        routeCount = owner.Endpoints.Count(endpoint => endpoint is RouteEndpoint);
        queryExercised = ExerciseQuery(owner);
        (streamStarted, streamCancelled) = ExerciseStream(owner);
        serializerExercised = ExerciseSerializer(services, assembly);
        cacheInspection = generateDocumentation
            ? GenerateOpenApiDocument(owner, assembly)
            : OpenApiCacheInspection.Empty;
        documentationGenerated = generateDocumentation && cacheInspection.ServiceInspected;
        return owner;
    }

    private static string FindMapper(Assembly assembly)
    {
        var mapperType = assembly.GetType(MapperTypeName, throwOnError: true)!;
        var mapper = mapperType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == MapperMethodName);
        return mapper is not null
            ? $"{mapperType.FullName}.{mapper.Name}"
            : throw new InvalidOperationException(
                $"The production Structured Logs assembly must expose '{MapperTypeName}.{MapperMethodName}'.");
    }

    private static void InvokeMapper(Assembly assembly, IEndpointRouteBuilder routeBuilder)
    {
        var mapperType = assembly.GetType(MapperTypeName, throwOnError: true)!;
        var mapper = mapperType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(method =>
                method.Name == MapperMethodName &&
                method.GetParameters() is [var parameter] &&
                parameter.ParameterType.IsInstanceOfType(routeBuilder))
            ?? throw new InvalidOperationException(
                $"The production Structured Logs assembly must expose '{MapperTypeName}.{MapperMethodName}(IEndpointRouteBuilder)'.");
        mapper.Invoke(null, [routeBuilder]);
    }

    private static IServiceProvider CreateProductionServices(Type featureType)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DiagnosticsStructuredLogs:ServiceName"] = "collectibility",
            ["DiagnosticsStructuredLogs:SourceDisplayName"] = "Collectibility",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new CollectibilityHostEnvironment());

        var feature = Activator.CreateInstance(featureType)
            ?? throw new InvalidOperationException($"Could not construct '{FeatureTypeName}'.");
        var configureServices = featureType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"'{FeatureTypeName}' does not expose ConfigureServices.");
        configureServices.Invoke(feature, [services]);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static bool ExerciseQuery(LifecycleOwner owner)
    {
        var endpoint = owner.FindEndpoint("recent");
        if (endpoint?.RequestDelegate is null)
            return false;

        using var scope = owner.Services.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(IStructuredLogStore)) is not IStructuredLogStore store)
            return false;

        store.AppendAsync(CreateEntry(1, "query")).GetAwaiter().GetResult();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = endpoint.RoutePattern.RawText ?? "/recent";
        context.Request.QueryString = new QueryString("?take=1");
        context.Response.Body = new MemoryStream();
        endpoint.RequestDelegate(context).GetAwaiter().GetResult();
        return context.Response.StatusCode is >= 200 and < 500;
    }

    private static (bool Started, bool Cancelled) ExerciseStream(LifecycleOwner owner)
    {
        var endpoint = owner.FindEndpoint("stream");
        if (endpoint?.RequestDelegate is null)
            return (false, false);

        using var scope = owner.Services.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(IStructuredLogStore)) is not IStructuredLogStore store)
            return (false, false);

        store.AppendAsync(CreateEntry(2, "stream")).GetAwaiter().GetResult();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = endpoint.RoutePattern.RawText ?? "/stream";
        context.RequestAborted = cancellation.Token;
        context.Response.Body = new MemoryStream();
        var task = endpoint.RequestDelegate(context);
        // DefaultHttpContext has no server response feature to flip HasStarted. The production handler
        // establishes the observable SSE boundary by setting the content type and preamble headers before
        // it enters its first asynchronous wait, which is the relevant signal in this direct invocation.
        var started = SpinUntil(
            () => string.Equals(context.Response.ContentType, "text/event-stream", StringComparison.Ordinal),
            TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        try
        {
            task.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Cancellation is an expected terminal path for a live stream.
        }

        return (started, cancellation.IsCancellationRequested);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ExerciseSerializer(IServiceProvider services, Assembly assembly)
    {
        var serializerType = assembly.GetType(SerializerTypeName);
        var serializer = serializerType is null ? null : services.GetService(serializerType);
        var method = serializerType?.GetMethod(
            "Serialize",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(StructuredLogEntry)],
            modifiers: null);
        if (serializer is null || method is null)
            return false;

        _ = method.Invoke(serializer, [CreateEntry(3, "serializer")]);
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OpenApiCacheInspection GenerateOpenApiDocument(LifecycleOwner owner, Assembly moduleAssembly)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new CollectibilityHostEnvironment());
        services.AddOpenApi();
        foreach (var dataSource in owner.DataSources)
            services.AddSingleton(dataSource);

        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var provider = serviceProvider.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = provider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
        var serviceType = typeof(IOpenApiDocumentProvider).Assembly.GetType("Microsoft.AspNetCore.OpenApi.OpenApiDocumentService")
            ?? Type.GetType("Microsoft.AspNetCore.OpenApi.OpenApiDocumentService, Microsoft.AspNetCore.OpenApi");
        var documentService = serviceType is null ? null : serviceProvider.GetKeyedService(serviceType, "v1");
        var cache = documentService is null
            ? OpenApiCacheInspection.Empty
            : InspectCache(documentService, moduleAssembly);
        var paths = document.Paths?.Count(path => path.Key.Contains("structured-logs", StringComparison.OrdinalIgnoreCase)) ?? 0;
        return cache with { ServiceInspected = cache.ServiceInspected && paths > 0 };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static OpenApiCacheInspection InspectCache(object documentService, Assembly moduleAssembly)
    {
        var moduleLoadContext = AssemblyLoadContext.GetLoadContext(moduleAssembly)
            ?? throw new InvalidOperationException("The collectible Structured Logs assembly has no load context.");
        var cacheField = documentService.GetType().GetField(
            "_operationTransformerContextCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (cacheField?.GetValue(documentService) is not IEnumerable entries)
            return OpenApiCacheInspection.Empty;

        var contextCount = 0;
        var typeCount = 0;
        var methodCount = 0;
        var delegateCount = 0;
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            contextCount++;
            var value = entry?.GetType().GetProperty("Value")?.GetValue(entry);
            var description = value?.GetType().GetProperty("Description")?.GetValue(value);
            var actionDescriptor = description?.GetType().GetProperty("ActionDescriptor")?.GetValue(description);
            if (actionDescriptor?.GetType().GetProperty("EndpointMetadata")?.GetValue(actionDescriptor) is not IEnumerable metadata)
                continue;

            foreach (var item in metadata)
                InspectMetadata(item, moduleLoadContext, ref typeCount, ref methodCount, ref delegateCount, kinds);
        }

        return new OpenApiCacheInspection(
            contextCount,
            typeCount,
            methodCount,
            delegateCount,
            string.Join(",", kinds.Order(StringComparer.Ordinal)),
            ServiceInspected: true);
    }

    private static void InspectMetadata(
        object? item,
        AssemblyLoadContext moduleLoadContext,
        ref int typeCount,
        ref int methodCount,
        ref int delegateCount,
        ISet<string> kinds)
    {
        if (item is null)
            return;

        if (item is Type type && IsModuleOwned(type.Assembly, moduleLoadContext))
        {
            typeCount++;
            kinds.Add("Type");
        }
        else if (item is MethodInfo method &&
                 method.DeclaringType is { } methodType &&
                 IsModuleOwned(methodType.Assembly, moduleLoadContext))
        {
            methodCount++;
            kinds.Add("MethodInfo");
        }
        else if (item is Delegate @delegate &&
                 @delegate.Method.DeclaringType is { } delegateType &&
                 IsModuleOwned(delegateType.Assembly, moduleLoadContext))
        {
            delegateCount++;
            kinds.Add("Delegate");
        }

        foreach (var field in item.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType == typeof(Type) &&
                field.GetValue(item) is Type fieldType &&
                IsModuleOwned(fieldType.Assembly, moduleLoadContext))
            {
                typeCount++;
                kinds.Add("Type");
            }
            else if (typeof(MethodInfo).IsAssignableFrom(field.FieldType) &&
                     field.GetValue(item) is MethodInfo fieldMethod &&
                     fieldMethod.DeclaringType is { } fieldMethodType &&
                     IsModuleOwned(fieldMethodType.Assembly, moduleLoadContext))
            {
                methodCount++;
                kinds.Add("MethodInfo");
            }
            else if (typeof(Delegate).IsAssignableFrom(field.FieldType) &&
                     field.GetValue(item) is Delegate fieldDelegate &&
                     fieldDelegate.Method.DeclaringType is { } fieldDelegateType &&
                     IsModuleOwned(fieldDelegateType.Assembly, moduleLoadContext))
            {
                delegateCount++;
                kinds.Add("Delegate");
            }
        }
    }

    private static bool IsModuleOwned(Assembly assembly, AssemblyLoadContext moduleLoadContext) =>
        ReferenceEquals(AssemblyLoadContext.GetLoadContext(assembly), moduleLoadContext);

    private static bool SpinUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
            Thread.Yield();
        return predicate();
    }

    private static StructuredLogEntry CreateEntry(long sequence, string message) => new()
    {
        Sequence = sequence,
        Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(sequence),
        Level = LogLevel.Information,
        Category = "Collectibility",
        Message = message,
        SourceId = "collectibility"
    };

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

    private sealed class LifecycleOwner(EndpointDataSource[] dataSources, IServiceProvider services) : IDisposable
    {
        private EndpointDataSource[] _dataSources = dataSources;
        private IServiceProvider? _services = services;

        public EndpointDataSource[] DataSources => _dataSources;
        public IServiceProvider Services => _services ?? throw new ObjectDisposedException(nameof(LifecycleOwner));
        public IReadOnlyList<Endpoint> Endpoints => _dataSources.SelectMany(dataSource => dataSource.Endpoints).ToArray();

        public RouteEndpoint? FindEndpoint(string suffix) =>
            Endpoints.OfType<RouteEndpoint>().FirstOrDefault(endpoint =>
                endpoint.RoutePattern.RawText?.EndsWith('/' + suffix, StringComparison.OrdinalIgnoreCase) == true);

        public void Dispose()
        {
            var dataSources = Interlocked.Exchange(ref _dataSources, []);
            foreach (var dataSource in dataSources)
                (dataSource as IDisposable)?.Dispose();

            var currentServices = Interlocked.Exchange(ref _services, null);
            (currentServices as IDisposable)?.Dispose();
        }
    }

    private sealed class CollectibilityHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = typeof(StructuredLogsCollectibleFixture).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
