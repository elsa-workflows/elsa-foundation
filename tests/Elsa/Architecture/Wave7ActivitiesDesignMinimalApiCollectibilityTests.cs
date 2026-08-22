using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Initial red proof for the Activities Design owner unload gate (T035/T037/T038).
///
/// Each cycle loads the production owner into a collectible context, maps the complete 38-route
/// surface, runs native authorization and OpenAPI, exercises representative mapped delegates, and
/// then removes the endpoint generation before disposing its services. The test intentionally keeps
/// the evidence honest: a failing cycle reports the first surviving generation roots instead of
/// clearing framework caches or manufacturing collection with a sleep.
/// </summary>
[Collection(Wave7ActivitiesDesignCollectibilityCollection.Name)]
public sealed class Wave7ActivitiesDesignMinimalApiCollectibilityTests
{
    private const string OwnerId = "Elsa.Activities.Design.Api";
    private const string AuthenticationScheme = "Wave7ActivitiesDesignCollectibility";
    private const string RoutePrefix = "design/activities";
    private const string OperationPrefix = "ElsaActivitiesDesignApiEndpoints";

    private static readonly RouteSpec[] RouteManifest =
    [
        new("GET", "availability/settings", "AvailabilityGetSettings", "activity-design.read", true, StatusCodes.Status200OK),
        new("GET", "availability/diagnostics", "AvailabilityListDiagnostics", "activity-design.read", false, StatusCodes.Status200OK),
        new("PUT", "availability/settings", "AvailabilitySaveSettings", "activity-design.manage", true, StatusCodes.Status200OK),
        new("GET", "authoring-capabilities", "AuthoringCapabilitiesGet", "activity-design.read", false, StatusCodes.Status200OK),
        new("GET", "catalog", "CatalogList", "activity-design.read", true, StatusCodes.Status200OK),
        new("POST", "definitions", "DefinitionsAdd", "activity-design.manage", true, StatusCodes.Status201Created),
        new("POST", "definitions/{definitionId}/fork-previews", "DefinitionsPreviewFork", "activity-design.manage", true, StatusCodes.Status200OK),
        new("GET", "definitions", "DefinitionsList", "activity-design.read", true, StatusCodes.Status200OK),
        new("GET", "definitions/{definitionId}", "DefinitionsGet", "activity-design.read", true, StatusCodes.Status200OK),
        new("PATCH", "definitions/{definitionId}", "DefinitionsUpdate", "activity-design.manage", true, StatusCodes.Status200OK),
        new("PUT", "definitions/{definitionId}/recommendation", "DefinitionsRecommendation", "activity-design.manage", true, StatusCodes.Status200OK),
        new("GET", "definitions/picker", "DefinitionsPicker", "activity-design.read", true, StatusCodes.Status200OK),
        new("GET", "definitions/{definitionId}/drafts", "DefinitionsListDrafts", "activity-design.read", true, StatusCodes.Status200OK),
        new("POST", "definitions/{definitionId}/drafts", "DefinitionsAddDraft", "activity-design.manage", true, StatusCodes.Status201Created),
        new("GET", "definitions/{definitionId}/versions", "DefinitionsListVersions", "activity-design.read", true, StatusCodes.Status200OK),
        new("GET", "drafts/{draftId}", "DraftsGet", "activity-design.read", true, StatusCodes.Status200OK),
        new("PUT", "drafts/{draftId}", "DraftsReplace", "activity-design.manage", true, StatusCodes.Status200OK),
        new("PATCH", "drafts/{draftId}/presentation", "DraftsUpdatePresentation", "activity-design.manage", true, StatusCodes.Status200OK),
        new("POST", "drafts/{draftId}/conflict-copies", "DraftsConflictCopy", "activity-design.manage", true, StatusCodes.Status201Created),
        new("POST", "drafts/{draftId}/validate", "DraftsValidate", "activity-design.manage", true, StatusCodes.Status200OK),
        new("POST", "drafts/{draftId}/migrate-provider", "DraftsMigrateProvider", "activity-design.manage", true, StatusCodes.Status201Created),
        new("POST", "drafts/{draftId}/contract-proposals", "DraftsProposeContract", "activity-design.manage", true, StatusCodes.Status200OK),
        new("POST", "drafts/{draftId}/contract-proposals/apply", "DraftsApplyContractProposal", "activity-design.manage", true, StatusCodes.Status200OK),
        new("DELETE", "drafts/{draftId}", "DraftsDiscard", "activity-design.manage", true, StatusCodes.Status204NoContent),
        new("POST", "drafts/{draftId}/diff", "DraftsDiff", "activity-design.read", true, StatusCodes.Status200OK),
        new("POST", "fork-candidates/{candidateId}/apply", "ForksApply", "activity-design.manage", true, StatusCodes.Status201Created),
        new("GET", "forks/{idempotencyKey}", "ForksGetStatus", "activity-design.read", true, StatusCodes.Status200OK),
        new("GET", "versions/{versionId}/dependencies", "VersionsDependencies", "activity-design.read", true, StatusCodes.Status200OK),
        new("GET", "versions/{fromVersionId}/diff/{toVersionId}", "VersionsDiff", "activity-design.read", true, StatusCodes.Status200OK),
        new("GET", "versions/{versionId}", "VersionsGet", "activity-design.read", true, StatusCodes.Status200OK),
        new("POST", "versions/{versionId}/retire", "VersionsRetire", "activity-design.manage", true, StatusCodes.Status200OK),
        new("POST", "versions/{versionId}/restore", "VersionsRestore", "activity-design.manage", true, StatusCodes.Status200OK),
        new("POST", "versions/{versionId}/revoke", "VersionsRevoke", "activity-design.manage", true, StatusCodes.Status200OK),
        new("POST", "upgrade-plans", "UpgradePlansCreate", "activity-design.manage", true, StatusCodes.Status201Created),
        new("GET", "upgrade-plans/{planId}", "UpgradePlansGet", "activity-design.read", true, StatusCodes.Status200OK),
        new("POST", "upgrade-plans/{planId}/apply", "UpgradePlansApply", "activity-design.manage", true, StatusCodes.Status200OK),
        new("GET", "upgrade-plans/{planId}/receipts/{receiptId}", "UpgradePlansGetReceipt", "activity-design.read", true, StatusCodes.Status200OK),
        new("POST", "upgrade-plans/{planId}/refresh", "UpgradePlansRefresh", "activity-design.manage", true, StatusCodes.Status201Created)
    ];

    [Fact]
    public async Task Three_owner_generations_execute_and_release_the_complete_activities_design_surface()
    {
        Assert.Equal(38, RouteManifest.Length);
        var featureType = typeof(ActivitiesDesignApiFeature);
        var configureServices = featureType.GetMethod(nameof(ActivitiesDesignApiFeature.ConfigureServices));
        Assert.True(featureType.IsPublic);
        Assert.False(featureType.IsSealed);
        Assert.NotNull(configureServices);
        Assert.True(configureServices!.IsVirtual);
        Assert.False(configureServices.IsFinal);
        var failures = new List<string>();

        for (var cycle = 0; cycle < 3; cycle++)
        {
            Evidence evidence;
            try
            {
                evidence = await CreateAndRelease(cycle);
            }
            catch (Exception exception)
            {
                throw new Xunit.Sdk.XunitException($"Activities Design collectibility cycle {cycle} failed during execution: {exception.GetBaseException().Message}", exception);
            }

            var collected = WaitForCollection(evidence.References);
            if (!collected)
                failures.Add($"cycle {cycle}: surviving roots={DescribeAlive(evidence.References)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<Evidence> CreateAndRelease(int cycle)
    {
        var cycleId = Guid.NewGuid();
        var sourcePath = typeof(ActivitiesDesignApiFeature).Assembly.Location;
        var assemblyBytes = File.ReadAllBytes(sourcePath);
        var loadContext = new ActivitiesDesignLoadContext($"Elsa.Wave7.ActivitiesDesign.{cycle}.{Guid.NewGuid():N}");
        Assembly assembly;
        using (var stream = new MemoryStream(assemblyBytes, writable: false))
            assembly = loadContext.LoadFromStream(stream);

        var featureType = assembly.GetType(typeof(ActivitiesDesignApiFeature).FullName!, throwOnError: true)!;
        var mapperType = assembly.GetType(typeof(ActivitiesDesignApi).FullName!, throwOnError: true)!;
        var feature = Activator.CreateInstance(featureType)
            ?? throw new InvalidOperationException($"Unable to create {featureType.FullName}.");
        var mapper = mapperType.GetMethod(nameof(ActivitiesDesignApi.MapActivitiesDesignApi), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Activities Design mapper is not public and static.");

        var publishedDataSource = new CollectibilityEndpointDataSource();
        var observations = new CollectibilityProbeState();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddHttpContextAccessor();
        services.AddSingleton<IHostEnvironment>(new CollectibilityHostEnvironment(OwnerId));
        services.AddSingleton<EndpointDataSource>(publishedDataSource);
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddOpenApi();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>([AuthenticationScheme], StringComparer.Ordinal));
        services.ReplacePermissionEvaluator<CollectibilityPermissionEvaluator>();
        services.AddAuthentication(AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, CollectibilityAuthenticationHandler>(AuthenticationScheme, _ => { });
        services.AddAuthorization();

        // Configure real host-selected seams before the owner is composed. The request sender below
        // calls these ports on every representative delegate, so a route can only be successful if
        // authorization, provider, store, and request-scoped adapter wiring are all present.
        services.AddSingleton(observations);
        services.AddSingleton<IActivityDefinitionStore, CollectibilityDefinitionStore>();
        services.AddSingleton<IActivityDefinitionVersionStore, CollectibilityVersionStore>();
        services.AddSingleton<IActivityAvailabilitySettingsStore, CollectibilityAvailabilitySettingsStore>();
        services.AddSingleton<IActivityProvider, CollectibilityActivityProvider>();
        services.AddScoped<IActivityAuthoringContextAsync, CollectibilityAuthoringContext>();
        services.AddScoped<IActivityDependencyContextAsync, CollectibilityDependencyContext>();

        featureType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.Instance)!.Invoke(feature, [services]);
        services.AddSingleton<CollectibilityRequestSender>();
        services.AddSingleton<IRequestSender>(provider => provider.GetRequiredService<CollectibilityRequestSender>());
        services.AddSingleton<CollectibilityCommandSender>();
        services.AddSingleton<ICommandSender>(provider => provider.GetRequiredService<CollectibilityCommandSender>());

        var serviceProvider = services.BuildServiceProvider();
        var routeBuilder = new CollectibilityRouteBuilder(serviceProvider);
        mapper.Invoke(null, [routeBuilder]);
        var endpoints = routeBuilder.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        publishedDataSource.SetEndpoints(endpoints);

        AssertRouteMetadata(endpoints, assembly);
        var resolver = AssertGeneratedJsonBoundary(serviceProvider, endpoints, assembly);

        OpenApiDocument? openApiDocument = null;
        IOpenApiDocumentProvider? openApiProvider = serviceProvider.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        if (cycle % 2 == 0)
            openApiDocument = await openApiProvider.GetOpenApiDocumentAsync(CancellationToken.None);

        if (cycle % 2 == 0)
            AssertGeneratedBindingAndSerialization(serviceProvider, endpoints, resolver, assembly);
        else
        {
            AssertGeneratedBindingAndSerialization(serviceProvider, endpoints, resolver, assembly);
            openApiDocument = await openApiProvider.GetOpenApiDocumentAsync(CancellationToken.None);
        }

        AssertNativeOpenApi(openApiDocument!, endpoints);
        await ExerciseRepresentativeDelegatesAsync(serviceProvider, endpoints, observations);
        Assert.True(observations.AuthorizationEvaluations >= 6, $"Real authorization did not evaluate all representative routes ({observations.AuthorizationEvaluations}).");
        Assert.True(observations.Requests >= 4, $"Representative request delegates did not dispatch ({observations.Requests}).");
        Assert.True(observations.Commands >= 2, $"Representative command delegates did not dispatch ({observations.Commands}).");
        Assert.True(observations.ProviderCalls >= 6, "Configured provider was not traversed by representative delegates.");
        Assert.True(observations.StoreCalls >= 18, "Configured stores were not traversed by representative delegates.");
        Assert.True(observations.AdapterCalls >= 12, "Configured authoring/dependency adapters were not traversed by representative delegates.");

        var references = new Dictionary<string, WeakReference>(StringComparer.Ordinal)
        {
            ["load-context"] = new(loadContext),
            ["assembly"] = new(assembly),
            ["feature-type"] = new(featureType),
            ["mapper-type"] = new(mapperType),
            ["feature"] = new(feature),
            ["services"] = new(serviceProvider),
            ["route-builder"] = new(routeBuilder),
            ["endpoint-data-source"] = new(publishedDataSource),
            ["resolver"] = new(resolver),
            ["openapi-provider"] = new(openApiProvider),
            ["openapi-document"] = new(openApiDocument!),
            ["observations"] = new(observations),
            ["request-sender"] = new(serviceProvider.GetRequiredService<CollectibilityRequestSender>()),
            ["command-sender"] = new(serviceProvider.GetRequiredService<CollectibilityCommandSender>()),
            ["definition-store"] = new(serviceProvider.GetRequiredService<IActivityDefinitionStore>()),
            ["version-store"] = new(serviceProvider.GetRequiredService<IActivityDefinitionVersionStore>()),
            ["availability-store"] = new(serviceProvider.GetRequiredService<IActivityAvailabilitySettingsStore>()),
            ["provider"] = new(serviceProvider.GetRequiredService<IActivityProvider>())
        };
        for (var index = 0; index < endpoints.Length; index++)
            references[$"endpoint-{index}"] = new(endpoints[index]);

        // Publish no framework-wide retention probe and do not clear private or global caches. The
        // owner data source itself is the ordinary replacement/removal seam exercised by a host.
        publishedDataSource.SetEndpoints([]);
        routeBuilder.DataSources.Clear();
        Assert.Empty(publishedDataSource.Endpoints);

        await ((IAsyncDisposable)serviceProvider).DisposeAsync();
        Assert.Equal(4, observations.Disposals);
        openApiProvider = null;
        openApiDocument = null;
        resolver = null!;
        endpoints = null!;
        routeBuilder = null!;
        publishedDataSource = null!;
        serviceProvider = null!;
        featureType = null!;
        mapperType = null!;
        mapper = null!;
        feature = null!;
        services = null!;
        assembly = null!;
        loadContext.Unload();
        loadContext = null!;

        return new(cycleId, references);
    }

    private static void AssertRouteMetadata(IReadOnlyCollection<RouteEndpoint> endpoints, Assembly ownerAssembly)
    {
        Assert.Equal(38, endpoints.Count);
        var actual = endpoints.Select(endpoint =>
        {
            var method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.SingleOrDefault()
                ?? throw new Xunit.Sdk.XunitException($"Route '{endpoint.RoutePattern.RawText}' has no single HTTP method.");
            var operationName = endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName ?? string.Empty;
            var disposition = endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>()?.Value;
            var permission = disposition is null
                ? string.Empty
                : new PermissionPolicyCodec().Parse(disposition).Descriptor?.Permissions.SingleOrDefault() ?? string.Empty;
            var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
            var successStatus = produces.Single(metadata => metadata.StatusCode is not (401 or 403)).StatusCode;
            return new RouteSpec(
                method,
                endpoint.RoutePattern.RawText!.TrimStart('/')[RoutePrefix.Length..].TrimStart('/'),
                operationName.StartsWith(OperationPrefix, StringComparison.Ordinal)
                    ? operationName[OperationPrefix.Length..]
                    : operationName,
                permission,
                endpoint.Metadata.GetMetadata<IAcceptsMetadata>() is not null,
                successStatus);
        }).ToArray();

        var expected = RouteManifest
            .Select(route => route with { Permission = route.Permission.ToUpperInvariant() })
            .OrderBy(route => route.Identity);
        Assert.Equal(expected, actual.OrderBy(route => route.Identity));
        Assert.All(endpoints, endpoint =>
        {
            Assert.Equal(OwnerId, endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
            Assert.Equal([OwnerId], endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags);
            Assert.NotNull(endpoint.Metadata.GetMetadata<AuthorizeAttribute>());
            Assert.Equal(typeof(RequestDelegate).Assembly, endpoint.Metadata.GetMetadata<MethodInfo>()?.DeclaringType?.Assembly);
            Assert.NotEqual(ownerAssembly, endpoint.Metadata.GetMetadata<MethodInfo>()?.DeclaringType?.Assembly);

            var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
            Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status401Unauthorized);
            Assert.Contains(produces, metadata => metadata.StatusCode == StatusCodes.Status403Forbidden);
            foreach (var metadata in produces.Where(metadata => metadata.Type != typeof(void)))
            {
                Assert.NotNull(metadata.Type);
                Assert.NotEqual(ownerAssembly, metadata.Type!.Assembly);
            }
            var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
            if (accepts is not null)
                Assert.NotEqual(ownerAssembly, accepts.RequestType!.Assembly);
        });
    }

    private static JsonTypeInfo AssertGeneratedJsonBoundary(IServiceProvider services, IReadOnlyCollection<RouteEndpoint> endpoints, Assembly ownerAssembly)
    {
        var options = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var ownerResolvers = options.TypeInfoResolverChain.Where(resolver => resolver.GetType().Assembly == ownerAssembly).ToArray();
        Assert.Single(ownerResolvers);
        var resolver = ownerResolvers[0];
        var contractTypes = endpoints
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>()
                .Select(metadata => metadata.RequestType)
                .Concat(endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(metadata => metadata.Type)))
            .Where(type => type is not null && type != typeof(void))
            .Cast<Type>()
            .Distinct()
            .ToArray();
        Assert.NotEmpty(contractTypes);
        Assert.All(contractTypes, type =>
        {
            Assert.NotEqual(ownerAssembly, type.Assembly);
            Assert.NotNull(resolver.GetTypeInfo(type, options));
            Assert.Same(resolver, options.TypeInfoResolverChain.First(candidate => candidate.GetTypeInfo(type, options) is not null));
        });
        var representativeType = endpoints.Single(endpoint => endpoint.RoutePattern.RawText!.EndsWith("/catalog", StringComparison.Ordinal))
            .Metadata.GetMetadata<IAcceptsMetadata>()!.RequestType!;
        Assert.NotEqual(ownerAssembly, representativeType.Assembly);
        var typeInfo = resolver.GetTypeInfo(representativeType, options);
        Assert.NotNull(typeInfo);
        return typeInfo!;
    }

    private static void AssertGeneratedBindingAndSerialization(
        IServiceProvider services,
        IReadOnlyCollection<RouteEndpoint> endpoints,
        JsonTypeInfo representativeRequestTypeInfo,
        Assembly ownerAssembly)
    {
        var options = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var requestType = representativeRequestTypeInfo.Type;
        var boundRequest = JsonSerializer.Deserialize("{}", requestType, options);
        Assert.NotNull(boundRequest);

        var responseType = endpoints.Single(endpoint => endpoint.RoutePattern.RawText!.EndsWith("/catalog", StringComparison.Ordinal))
            .Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Single(metadata => metadata.StatusCode == StatusCodes.Status200OK).Type;
        Assert.NotNull(responseType);
        Assert.NotEqual(ownerAssembly, responseType.Assembly);
        var response = RuntimeHelpers.GetUninitializedObject(responseType);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(response, responseType, options);
        Assert.NotEmpty(serialized);
    }

    private static void AssertNativeOpenApi(OpenApiDocument document, IReadOnlyCollection<RouteEndpoint> endpoints)
    {
        var operationCount = document.Paths?.Sum(path => path.Value.Operations?.Count ?? 0) ?? 0;
        Assert.Equal(38, operationCount);
        Assert.Equal(38, endpoints.Count);
        Assert.Contains(document.Paths!, path => path.Key.EndsWith("/catalog", StringComparison.Ordinal));
        Assert.Contains(document.Paths!, path => path.Key.EndsWith("/upgrade-plans", StringComparison.Ordinal));
        Assert.NotNull(document.Components?.Schemas);
        Assert.NotEmpty(document.Components!.Schemas!);
    }

    private static async Task ExerciseRepresentativeDelegatesAsync(
        IServiceProvider services,
        IReadOnlyCollection<RouteEndpoint> endpoints,
        CollectibilityProbeState observations)
    {
        var pipeline = new ApplicationBuilder(services);
        pipeline.UseAuthentication();
        pipeline.UseAuthorization();
        pipeline.Run(context => context.GetEndpoint()!.RequestDelegate!(context));
        var application = pipeline.Build();

        await InvokeAsync(services, endpoints, "GET", "design/activities/catalog", null, null, application);
        await InvokeAsync(services, endpoints, "GET", "design/activities/definitions/{definitionId}", null,
            new Dictionary<string, object?> { ["definitionId"] = "collectible-definition" }, application);
        await InvokeAsync(services, endpoints, "GET", "design/activities/availability/settings", null, null, application);
        await InvokeAsync(services, endpoints, "GET", "design/activities/versions/{versionId}/dependencies", null,
            new Dictionary<string, object?> { ["versionId"] = "collectible-version" }, application);
        await InvokeAsync(services, endpoints, "POST", "design/activities/versions/{versionId}/retire",
            "{\"expectedLifecycle\":\"active\",\"reason\":\"collectibility\"}",
            new Dictionary<string, object?> { ["versionId"] = "collectible-version" }, application);
        await InvokeAsync(services, endpoints, "POST", "design/activities/upgrade-plans",
            "{\"replacements\":[],\"roots\":[],\"includeTransitiveDependents\":true,\"createDraftsForPublishedDependents\":false}", null, application);

        Assert.Equal(6, observations.RepresentativeRoutes);
    }

    private static async Task InvokeAsync(
        IServiceProvider services,
        IReadOnlyCollection<RouteEndpoint> endpoints,
        string method,
        string route,
        string? body,
        IReadOnlyDictionary<string, object?>? routeValues,
        RequestDelegate application)
    {
        var endpoint = endpoints.Single(candidate =>
            candidate.RoutePattern.RawText!.TrimStart('/') == route &&
            candidate.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase));
        await using var requestBody = body is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(body));
        await using var responseBody = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Request.Method = method;
        context.Request.Path = "/" + route.Replace("{definitionId}", "collectible-definition", StringComparison.Ordinal)
            .Replace("{versionId}", "collectible-version", StringComparison.Ordinal);
        context.Request.ContentType = body is null ? null : "application/json";
        context.Request.Body = requestBody ?? Stream.Null;
        if (routeValues is not null)
            foreach (var value in routeValues)
                context.Request.RouteValues[value.Key] = value.Value;
        context.Response.Body = responseBody;
        context.SetEndpoint(endpoint);
        var authentication = await context.AuthenticateAsync(AuthenticationScheme);
        Assert.True(authentication.Succeeded);
        context.User = authentication.Principal!;
        await application(context);
        Assert.DoesNotContain(context.Response.StatusCode, new[] { StatusCodes.Status401Unauthorized, StatusCodes.Status403Forbidden });
        Assert.NotEqual(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
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

    private static string DescribeAlive(IReadOnlyDictionary<string, WeakReference> references) =>
        string.Join(",", references.Where(reference => reference.Value.IsAlive).Select(reference => reference.Key));

    private sealed record Evidence(Guid CycleId, IReadOnlyDictionary<string, WeakReference> References);

    private sealed record RouteSpec(string Method, string Path, string Operation, string Permission, bool Accepts, int SuccessStatus)
    {
        public string Identity => $"{Method} {RoutePrefix}/{Path}";
    }

    private sealed class ActivitiesDesignLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CollectibilityRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class CollectibilityEndpointDataSource : EndpointDataSource
    {
        private IReadOnlyList<Endpoint> _endpoints = [];
        public override IReadOnlyList<Endpoint> Endpoints => _endpoints;
        public override IChangeToken GetChangeToken() => NullChangeToken.Singleton;
        public void SetEndpoints(IEnumerable<Endpoint> endpoints) => _endpoints = endpoints.ToArray();
    }

    private sealed class CollectibilityHostEnvironment(string owner) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = owner;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CollectibilityProbeState
    {
        public int AuthorizationEvaluations { get; set; }
        public int Requests { get; set; }
        public int Commands { get; set; }
        public int ProviderCalls { get; set; }
        public int StoreCalls { get; set; }
        public int AdapterCalls { get; set; }
        public int RepresentativeRoutes { get; set; }
        public int Disposals { get; set; }
    }

    private sealed class CollectibilityAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        CollectibilityProbeState observations)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            observations.AuthorizationEvaluations++;
            var identity = new ClaimsIdentity(
                [
                    new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard),
                    new Claim(IdentityClaimTypes.TenantId, "collectible-tenant"),
                    new Claim(IdentityClaimTypes.Normalized, "v1"),
                    new Claim(IdentityClaimTypes.Provider, "collectibility-provider"),
                    new Claim(ClaimTypes.NameIdentifier, "collectibility-actor")
                ],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class CollectibilityPermissionEvaluator(
        IPermissionCatalog catalog,
        CollectibilityProbeState observations) : IPermissionEvaluator
    {
        private readonly ClaimsPermissionEvaluator _inner = new(catalog);

        public ValueTask<PermissionEvaluationResult> EvaluateAsync(
            PermissionEvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            observations.AuthorizationEvaluations++;
            return _inner.EvaluateAsync(context, cancellationToken);
        }
    }

    private sealed class CollectibilityRequestSender(
        IActivityDefinitionStore definitionStore,
        IActivityDefinitionVersionStore versionStore,
        IActivityAvailabilitySettingsStore availabilityStore,
        IActivityProvider provider,
        IActivityAuthoringContextAsync authoringContext,
        IActivityDependencyContextAsync dependencyContext,
        CollectibilityProbeState observations) : IRequestSender
    {
        public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            observations.Requests++;
            observations.RepresentativeRoutes++;
            await definitionStore.ListAsync(new ActivityDefinitionFilter(), cancellationToken);
            await versionStore.ListAsync(cancellationToken);
            await availabilityStore.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope, cancellationToken);
            await authoringContext.GetAuthorizationProfileAsync(cancellationToken);
            await authoringContext.CanAuthorProviderAsync("collectible", cancellationToken);
            await dependencyContext.GetAuthorizationProfileAsync(cancellationToken);
            await dependencyContext.CanReadAsync(new ActivityDefinitionReference("ActivityDefinition", "collectible-definition"), cancellationToken);
            await provider.ValidateAsync(
                new ActivityProviderManifest("collectible", "1", JsonSerializer.SerializeToElement(new { generation = "collectible" })),
                new ActivityContract("1", [], [], []),
                cancellationToken);
            observations.ProviderCalls++;
            observations.StoreCalls += 3;
            observations.AdapterCalls += 4;
            return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        }
    }

    private sealed class CollectibilityCommandSender(
        IActivityDefinitionStore definitionStore,
        IActivityDefinitionVersionStore versionStore,
        IActivityAvailabilitySettingsStore availabilityStore,
        IActivityProvider provider,
        IActivityAuthoringContextAsync authoringContext,
        IActivityDependencyContextAsync dependencyContext,
        CollectibilityProbeState observations) : ICommandSender
    {
        public async Task<T> Send<T>(ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull
        {
            observations.Commands++;
            observations.RepresentativeRoutes++;
            await definitionStore.GetAsync("collectible-definition", cancellationToken);
            await versionStore.GetAsync("collectible-version", cancellationToken);
            await availabilityStore.SaveAsync(new ActivityAvailabilitySettings(), cancellationToken);
            await authoringContext.CanManageActivityDefinitionsAsync(cancellationToken);
            await dependencyContext.GetAuthorizationProfileAsync(cancellationToken);
            _ = provider.AuthoringCapabilities;
            observations.StoreCalls += 3;
            observations.AdapterCalls += 2;
            observations.ProviderCalls++;
            return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        }

        public Task Send(ICommand command, CancellationToken cancellationToken = default)
        {
            observations.Commands++;
            observations.ProviderCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class CollectibilityDefinitionStore(CollectibilityProbeState observations) : IActivityDefinitionStore, IDisposable
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActivityDefinition { Id = id, ActivityTypeKey = "collectible", Category = "Collectibility" });

        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinition?>(null);

        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinition>>([new ActivityDefinition { Id = "collectible-definition", ActivityTypeKey = "collectible", Category = "Collectibility" }]);

        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivityDefinition?>(null);

        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public void Dispose() => observations.Disposals++;
    }

    private sealed class CollectibilityVersionStore(CollectibilityProbeState observations) : IActivityDefinitionVersionStore, IDisposable
    {
        private static ActivityDefinitionVersion Version(string id) => new("1.0.0", "collectible-definition") { Id = id, ProviderKey = "collectible", ProviderSchemaVersion = "1", ConsumerKey = "collectible", ConsumerSchemaVersion = "1" };

        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => Task.FromResult(Version(versionId));
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => Task.FromResult(Version(versionId));
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersion?>(Version("collectible-version"));
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([Version("collectible-version")]);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([Version("collectible-version")]);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([Version("collectible-version")]);

        public void Dispose() => observations.Disposals++;
    }

    private sealed class CollectibilityAvailabilitySettingsStore(CollectibilityProbeState observations) : IActivityAvailabilitySettingsStore, IDisposable
    {
        public Task<ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default) => Task.FromResult<ActivityAvailabilitySettings?>(new ActivityAvailabilitySettings { Scope = scope });
        public Task SaveAsync(ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() => observations.Disposals++;
    }

    private sealed class CollectibilityActivityProvider(CollectibilityProbeState observations) : IActivityProvider, IAsyncDisposable
    {
        public string ProviderKey => "collectible";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string>(["1"], StringComparer.Ordinal);
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new(
            "Collectible provider",
            [new ActivityProviderManifestSchemaCapabilities("1", true, new HashSet<string>(StringComparer.Ordinal))],
            new ActivityProviderContractConstraints([]));

        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityContractProposal([], []));
        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);
        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityManifestMigration(request.Source, []));

        public ValueTask DisposeAsync()
        {
            observations.Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CollectibilityAuthoringContext : IActivityAuthoringContextAsync
    {
        public string? TenantId => "collectible-tenant";
        public string ActorId => "collectible-actor";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("collectible-profile");
        public ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class CollectibilityDependencyContext : IActivityDependencyContextAsync
    {
        public string? TenantId => "collectible-tenant";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("collectible-profile");
        public ValueTask<bool> CanReadAsync(ActivityDefinitionReference reference, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wave7ActivitiesDesignCollectibilityCollection
{
    public const string Name = "Wave 7 Activities Design Minimal API collectibility";
}
