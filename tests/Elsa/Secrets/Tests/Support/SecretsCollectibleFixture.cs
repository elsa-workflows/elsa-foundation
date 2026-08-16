using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Secrets.Api.Features;
using Elsa.Secrets.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
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

namespace Elsa.Secrets.Tests.Support;

/// <summary>Weak-reference-only observations for one isolated Secrets API lifecycle.</summary>
public sealed class SecretsCollectibleCycle : IDisposable
{
    private bool _disposed;

    internal SecretsCollectibleCycle(
        Guid cycleId,
        RetentionStage requestedStage,
        string assemblyName,
        string mapperName,
        int routeCount,
        IReadOnlyList<string> policyNames,
        bool jsonExercised,
        bool documentationGenerated,
        WeakReference loadContext,
        WeakReference assembly,
        WeakReference endpointType)
    {
        CycleId = cycleId;
        RequestedStage = requestedStage;
        AssemblyName = assemblyName;
        MapperName = mapperName;
        RouteCount = routeCount;
        PolicyNames = policyNames;
        JsonExercised = jsonExercised;
        DocumentationGenerated = documentationGenerated;
        LoadContext = loadContext;
        Assembly = assembly;
        EndpointType = endpointType;
    }

    public Guid CycleId { get; }
    public RetentionStage RequestedStage { get; }
    public string AssemblyName { get; }
    public string MapperName { get; }
    public int RouteCount { get; }
    public IReadOnlyList<string> PolicyNames { get; }
    public bool JsonExercised { get; }
    public bool DocumentationGenerated { get; }
    public WeakReference LoadContext { get; }
    public WeakReference Assembly { get; }
    public WeakReference EndpointType { get; }

    public UnloadEvidence VerifyCollection(int maxAttempts = UnloadEvidence.DefaultMaxCollectionAttempts) =>
        UnloadEvidence.Verify(CycleId, LoadContext, Assembly, EndpointType, maxAttempts);

    public void ReleaseRetention() => SecretsCollectibleFixture.ReleaseRetention(CycleId);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseRetention();
    }
}

/// <summary>
/// Loads and invokes the production Secrets API mapper inside a collectible context. The fixture
/// intentionally returns only strings, value types, and weak references from its no-inlining boundary.
/// </summary>
public static class SecretsCollectibleFixture
{
    private const string MapperTypeName = "Elsa.Secrets.Api.SecretsApi";
    private const string FeatureTypeName = "Elsa.Secrets.Api.Features.SecretsApiFeature";
    private const string MapperMethodName = "MapSecretsApi";
    private static readonly ConcurrentDictionary<Guid, Action> ReleaseActions = new();
    private static int _assemblyNumber;

    public static SecretsCollectibleCycle Create(
        RetentionStage retentionStage = RetentionStage.Clean,
        bool generateDocumentation = false)
    {
        if (retentionStage is RetentionStage.Harness)
            throw new ArgumentOutOfRangeException(nameof(retentionStage), retentionStage,
                "Harness retention is covered by the shared compatibility fixture.");

        var cycleId = Guid.NewGuid();
        var assemblyName = $"Elsa.Collectible.Secrets.Api.{Interlocked.Increment(ref _assemblyNumber)}";
        return CreateAndUnload(cycleId, assemblyName, retentionStage, generateDocumentation);
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
    private static SecretsCollectibleCycle CreateAndUnload(
        Guid cycleId,
        string assemblyName,
        RetentionStage retentionStage,
        bool generateDocumentation)
    {
        var productionPath = typeof(SecretsApiFeature).Assembly.Location;
        if (string.IsNullOrWhiteSpace(productionPath) || !File.Exists(productionPath))
            throw new InvalidOperationException($"The production Secrets API assembly is unavailable at '{productionPath}'.");

        var loadContext = new ProductionApiLoadContext(assemblyName);
        var assembly = loadContext.LoadFromAssemblyPath(productionPath);
        var representativeType = assembly.GetType(FeatureTypeName, throwOnError: true)!;
        var mapper = FindMapper(assembly);
        var routeCount = 0;
        IReadOnlyList<string> policyNames = [];
        var jsonExercised = false;
        var documentationGenerated = false;

        switch (retentionStage)
        {
            case RetentionStage.Clean:
            {
                var owner = MapAndMaterialize(assembly, generateDocumentation, out routeCount, out policyNames, out jsonExercised, out documentationGenerated);
                owner.Dispose();
                break;
            }
            case RetentionStage.Route:
            {
                var owner = MapAndMaterialize(assembly, generateDocumentation, out routeCount, out policyNames, out jsonExercised, out documentationGenerated);
                ReleaseActions[cycleId] = owner.Dispose;
                RetentionStageProbe.PublishRoute(cycleId, owner);
                break;
            }
            case RetentionStage.Services:
            {
                var owner = BuildServicesOwner(representativeType);
                ReleaseActions[cycleId] = owner.Dispose;
                RetentionStageProbe.PublishServices(cycleId, owner);
                break;
            }
            case RetentionStage.Serializer:
            {
                var owner = MapAndBuildDocumentation(assembly, out routeCount, out policyNames, out jsonExercised, out documentationGenerated);
                ReleaseActions[cycleId] = owner.Dispose;
                RetentionStageProbe.PublishSerializer(cycleId, owner);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(retentionStage), retentionStage, null);
        }

        var loadContextReference = new WeakReference(loadContext);
        var assemblyReference = new WeakReference(assembly);
        var typeReference = new WeakReference(representativeType);
        loadContext.Unload();

        return new SecretsCollectibleCycle(
            cycleId,
            retentionStage,
            assemblyName,
            mapper,
            routeCount,
            policyNames,
            jsonExercised,
            documentationGenerated,
            loadContextReference,
            assemblyReference,
            typeReference);
    }

    private static string FindMapper(Assembly assembly)
    {
        var mapperType = assembly.GetType(MapperTypeName);
        var mapper = mapperType?.GetMethod(MapperMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (mapper is not null)
            return $"{mapperType!.FullName}.{mapper.Name}";

        var featureType = assembly.GetType(FeatureTypeName, throwOnError: true)!;
        var featureMapper = featureType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "MapEndpoints" || method.Name.EndsWith(".MapEndpoints", StringComparison.Ordinal));
        if (featureMapper is not null)
            return $"{featureType.FullName}.{featureMapper.Name}";

        throw new InvalidOperationException(
            "The production Secrets API assembly must expose MapSecretsApi or IWebShellFeature.MapEndpoints.");
    }

    private static RouteOwner MapAndMaterialize(
        Assembly assembly,
        bool generateDocumentation,
        out int routeCount,
        out IReadOnlyList<string> policyNames,
        out bool jsonExercised,
        out bool documentationGenerated)
    {
        var routeServices = CreateServices();
        var builder = new CollectibleRouteBuilder(routeServices);
        InvokeMapper(assembly, builder);
        var owner = new RouteOwner(builder.DataSources.ToArray(), routeServices);
        routeCount = owner.Endpoints.Count(endpoint =>
            (endpoint as RouteEndpoint)?.RoutePattern.RawText?.StartsWith("/secrets", StringComparison.Ordinal) == true);
        if (routeCount == 0)
            throw new InvalidOperationException("The production Secrets API mapper published no endpoints.");

        policyNames = owner.Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>()?.Policy)
            .Where(policy => policy is not null)
            .Select(policy => policy!)
            .ToArray();
        jsonExercised = ExerciseJson(owner);
        documentationGenerated = generateDocumentation && GenerateOpenApiDocument(owner);
        return owner;
    }

    private static DocumentationOwner MapAndBuildDocumentation(
        Assembly assembly,
        out int routeCount,
        out IReadOnlyList<string> policyNames,
        out bool jsonExercised,
        out bool documentationGenerated)
    {
        var owner = MapAndMaterialize(assembly, generateDocumentation: true, out routeCount, out policyNames, out jsonExercised, out documentationGenerated);
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        foreach (var type in owner.Endpoints
                     .SelectMany(endpoint => endpoint.Metadata.OfType<IProducesResponseTypeMetadata>())
                     .Where(metadata => metadata.Type is not null && metadata.Type != typeof(void))
                     .Select(metadata => metadata.Type!)
                     .Distinct())
        {
            _ = serializerOptions.GetTypeInfo(type);
        }

        return new DocumentationOwner(owner, serializerOptions);
    }

    private static void InvokeMapper(Assembly assembly, IEndpointRouteBuilder routeBuilder)
    {
        var mapperType = assembly.GetType(MapperTypeName);
        var mapper = mapperType?.GetMethod(MapperMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (mapper is not null)
        {
            mapper.Invoke(null, BuildArguments(mapper, routeBuilder));
            return;
        }

        var featureType = assembly.GetType(FeatureTypeName, throwOnError: true)!;
        var feature = Activator.CreateInstance(featureType)
            ?? throw new InvalidOperationException($"Could not construct '{FeatureTypeName}'.");
        var featureMapper = featureType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "MapEndpoints" || method.Name.EndsWith(".MapEndpoints", StringComparison.Ordinal));
        if (featureMapper is null)
            throw new InvalidOperationException("No production endpoint mapper was found.");

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
            throw new InvalidOperationException($"Cannot supply mapper parameter '{parameter.Name}'.");
        })
        .ToArray();

    private static IServiceProvider CreateServices() =>
        CreateServiceCollection().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private static ServiceOwner BuildServicesOwner(Type featureType)
    {
        var services = CreateServiceCollection();
        var feature = Activator.CreateInstance(featureType)
            ?? throw new InvalidOperationException($"Could not construct '{FeatureTypeName}'.");
        var configureServices = featureType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"'{FeatureTypeName}' does not expose ConfigureServices.");
        configureServices.Invoke(feature, [services]);
        return new ServiceOwner(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));
    }

    private static ServiceCollection CreateServiceCollection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Secrets:EncryptionKey"] = "collectibility-test-encryption-key"
            })
            .Build();
        var services = new ServiceCollection();
        AddCoreServices(services, configuration);
        return services;
    }

    private static void AddCoreServices(IServiceCollection services, IConfiguration? configuration = null)
    {
        configuration ??= new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Secrets:EncryptionKey"] = "collectibility-test-encryption-key"
            })
            .Build();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new CollectibilityHostEnvironment());
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "test" });
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSecrets(configuration);
    }

    private static bool ExerciseJson(RouteOwner owner)
    {
        var endpoint = owner.Endpoints.OfType<RouteEndpoint>().FirstOrDefault(endpoint =>
            endpoint.RoutePattern.RawText?.EndsWith("/secrets/picker", StringComparison.Ordinal) == true &&
            endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("POST", StringComparer.OrdinalIgnoreCase) == true);
        if (endpoint?.RequestDelegate is null)
            return false;

        using var requestScope = owner.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(IdentityClaimTypes.TenantId, "collectibility-tenant")],
            "test"));
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/secrets/picker";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        try
        {
            endpoint.RequestDelegate(context).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool GenerateOpenApiDocument(RouteOwner owner)
    {
        var services = CreateServiceCollection();
        services.AddOpenApi();
        foreach (var dataSource in owner.DataSources)
            services.AddSingleton(dataSource);
        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var provider = serviceProvider.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = provider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
        return document.Paths?.Count(path => path.Key.StartsWith("/secrets", StringComparison.Ordinal)) == 7;
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

    private sealed class RouteOwner(EndpointDataSource[] dataSources, IServiceProvider services) : IDisposable
    {
        private EndpointDataSource[] _dataSources = dataSources;
        private IServiceProvider? _services = services;
        public IReadOnlyList<EndpointDataSource> DataSources => _dataSources;
        public IServiceProvider Services => _services ?? throw new ObjectDisposedException(nameof(RouteOwner));
        public IReadOnlyList<Endpoint> Endpoints => _dataSources.SelectMany(dataSource => dataSource.Endpoints).ToArray();
        public void Dispose()
        {
            _dataSources = [];
            var currentServices = Interlocked.Exchange(ref _services, null);
            (currentServices as IDisposable)?.Dispose();
        }
    }

    private sealed class ServiceOwner(IServiceProvider serviceProvider) : IDisposable
    {
        public void Dispose() => (serviceProvider as IDisposable)?.Dispose();
    }

    private sealed class CollectibilityHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = typeof(SecretsCollectibleFixture).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class DocumentationOwner(RouteOwner routeOwner, JsonSerializerOptions serializerOptions) : IDisposable
    {
        public void Dispose()
        {
            serializerOptions.TypeInfoResolver = null;
            routeOwner.Dispose();
        }
    }
}
