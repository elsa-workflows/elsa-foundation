using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
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
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Repeats production mapper publication in collectible contexts for every Wave 1 owner.</summary>
[Collection(Wave1MinimalApiCollectibilityCollection.Name)]
public sealed class Wave1MinimalApiCollectibilityTests
{
    private static readonly (string Owner, string AssemblyPath, string MapperType, string MapperMethod, string FeatureType, string JsonContextType, string JsonTypeInfoProperty, string JsonSample)[] Owners =
    [
        ("Elsa.Api.Capabilities", typeof(Elsa.Api.Capabilities.ApiCapabilitiesApi).Assembly.Location,
            "Elsa.Api.Capabilities.ApiCapabilitiesApi", "MapApiCapabilitiesApi", "Elsa.Api.Capabilities.ApiCapabilitiesFeature", "Elsa.Api.Capabilities.ApiCapabilitiesJsonContext", "ApiCapabilitiesDocument", "{\"capabilities\":[]}"),
        ("Elsa.Attention.Api", typeof(Elsa.Attention.Api.AttentionApi).Assembly.Location,
            "Elsa.Attention.Api.AttentionApi", "MapAttentionApi", "Elsa.Attention.Api.AttentionApiFeature", "Elsa.Attention.Api.AttentionJsonContext", "AttentionAggregationResult", "{\"generatedAt\":\"2026-01-01T00:00:00Z\",\"contributors\":[]}"),
        ("Elsa.Expressions.Api", typeof(Elsa.Expressions.Api.ExpressionsApi).Assembly.Location,
            "Elsa.Expressions.Api.ExpressionsApi", "MapExpressionsApi", "Elsa.Expressions.Api.ExpressionsApiFeature", "Elsa.Expressions.Api.ExpressionsJsonContext", "ExpressionDescriptorsResponse", "{\"items\":[]}"),
        ("Elsa.Expressions.JavaScript.Rendering", typeof(Elsa.Expressions.JavaScript.Rendering.JavaScriptRenderingApi).Assembly.Location,
            "Elsa.Expressions.JavaScript.Rendering.JavaScriptRenderingApi", "MapJavaScriptRenderingApi", "Elsa.Expressions.JavaScript.Rendering.JavaScriptRenderingEndpointsFeature", "Elsa.Expressions.JavaScript.Rendering.JavaScriptRenderingJsonContext", "JavaScriptRenderingSuccessResponse", "{\"success\":true,\"document\":\"\"}"),
        ("Elsa.Workflows.Runtime.JavaScript", typeof(Elsa.Workflows.Runtime.JavaScript.JavaScriptExecutionApi).Assembly.Location,
            "Elsa.Workflows.Runtime.JavaScript.JavaScriptExecutionApi", "MapJavaScriptExecutionApi", "Elsa.Workflows.Runtime.JavaScript.JavaScriptActivitiesEndpointsFeature", "Elsa.Workflows.Runtime.JavaScript.JavaScriptExecutionJsonContext", "RequestModel", "{\"script\":\"return 1;\"}"),
        ("Elsa.Workflows.Dashboard", typeof(Elsa.Workflows.Dashboard.WorkflowsDashboardApi).Assembly.Location,
            "Elsa.Workflows.Dashboard.WorkflowsDashboardApi", "MapWorkflowsDashboardApi", "Elsa.Workflows.Dashboard.WorkflowsDashboardFeature", "Elsa.Workflows.Dashboard.WorkflowsDashboardJsonContext", "WorkflowPortfolioSnapshot", "{\"status\":\"ready\",\"generatedAt\":\"2026-01-01T00:00:00Z\",\"activeDefinitionCount\":0,\"publishedDefinitionCount\":0,\"unpublishedDraftCount\":0,\"invalidDraftCount\":0}"),
        ("Elsa.Workflows.Design.Api", typeof(Elsa.Workflows.Design.Api.WorkflowsDesignApi).Assembly.Location,
            "Elsa.Workflows.Design.Api.WorkflowsDesignApi", "MapWorkflowsDesignApi", "Elsa.Workflows.Design.Api.WorkflowsDesignApiFeature", "Elsa.Workflows.Design.Api.WorkflowsDesignJsonContext", "ActivityStructuresResponse", "{\"items\":[],\"fingerprint\":\"sha256:\"}")
    ];

    [Fact]
    public void Every_wave_one_owner_releases_repeatedly_after_route_publication()
    {
        var failures = new List<string>();
        foreach (var owner in Owners)
        {
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var evidence = CreateAndUnload(owner, cycle);
                if (evidence.RouteCount == 0)
                {
                    failures.Add($"{owner.Owner} cycle {cycle}: published no endpoints");
                    continue;
                }

                var unload = UnloadEvidence.Verify(evidence.CycleId, evidence.LoadContext, evidence.Assembly, evidence.MapperType, 32);
                var publicationCollected = WaitForCollection(evidence.References);
                if (!unload.Collected || !publicationCollected)
                    failures.Add($"{owner.Owner} cycle {cycle}: {unload.Diagnostic ?? "unknown retention"}; unloadCollected={unload.Collected}; alive={DescribeAlive(evidence.References)}");
            }
        }

        Assert.Empty(failures);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CollectibilityEvidence CreateAndUnload(
        (string Owner, string AssemblyPath, string MapperType, string MapperMethod, string FeatureType, string JsonContextType, string JsonTypeInfoProperty, string JsonSample) owner,
        int cycle)
    {
        if (string.IsNullOrWhiteSpace(owner.AssemblyPath) || !File.Exists(owner.AssemblyPath))
            throw new InvalidOperationException($"Production assembly for {owner.Owner} is unavailable.");

        var cycleId = Guid.NewGuid();
        var loadContext = (AssemblyLoadContext?)new ProductionApiLoadContext($"Elsa.Wave1.{owner.Owner}.{cycle}.{Guid.NewGuid():N}");
        // Snapshot the owner assembly before loading it. The feature-delivery loop can build
        // owner projects concurrently; loading directly from a shared bin path would make the
        // unload probe race the build outputs and produce nondeterministic harness retention.
        var assemblyBytes = File.ReadAllBytes(owner.AssemblyPath);
        var assembly = (Assembly?)null;
        using (var assemblyStream = new MemoryStream(assemblyBytes, writable: false))
            assembly = loadContext!.LoadFromStream(assemblyStream);
        var mapperType = assembly.GetType(owner.MapperType, throwOnError: true)!;
        var mapper = mapperType.GetMethod(owner.MapperMethod, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{owner.Owner} does not expose {owner.MapperMethod}.");
        var featureType = assembly.GetType(owner.FeatureType, throwOnError: true)!;
        var feature = Activator.CreateInstance(featureType)
            ?? throw new InvalidOperationException($"Unable to create {owner.FeatureType}.");
        var serviceDescriptors = new ServiceCollection().AddLogging().AddRouting();
        serviceDescriptors.AddSingleton<IHostEnvironment>(new CollectibilityHostEnvironment(owner.Owner));
        serviceDescriptors.AddOpenApi();
        var publishedDataSource = new CollectibilityEndpointDataSource();
        serviceDescriptors.AddSingleton<EndpointDataSource>(publishedDataSource);
        serviceDescriptors.AddDynamicEndpointApiExplorerRefresh();
        if (owner.Owner == "Elsa.Workflows.Design.Api")
        {
            serviceDescriptors.AddAuthentication(CollectibilityAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, CollectibilityAuthenticationHandler>(CollectibilityAuthenticationHandler.SchemeName, _ => { });
            serviceDescriptors.AddAuthorization();
            serviceDescriptors.AddFoundationIdentityAbstractions(options =>
                options.NormalizedAuthenticationTypes = new HashSet<string>([CollectibilityAuthenticationHandler.SchemeName], StringComparer.Ordinal));
        }
        var configureServices = featureType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{owner.FeatureType} does not expose ConfigureServices.");
        configureServices.Invoke(feature, [serviceDescriptors]);
        if (owner.Owner == "Elsa.Workflows.Design.Api")
        {
            serviceDescriptors.AddSingleton<CollectibilityExpressionToolingProvider>();
            serviceDescriptors.AddSingleton<IExpressionToolingProvider>(services => services.GetRequiredService<CollectibilityExpressionToolingProvider>());
            serviceDescriptors.AddSingleton<IExpressionToolingProviderResolver>(services =>
                new CollectibilityExpressionToolingProviderResolver(services.GetRequiredService<CollectibilityExpressionToolingProvider>()));
            serviceDescriptors.AddSingleton<IExpressionAuthoringContextSource, CollectibilityExpressionAuthoringContextSource>();
            serviceDescriptors.AddSingleton<CollectibilityActivityVersionStore>();
            serviceDescriptors.AddSingleton<IActivityDefinitionVersionStore>(services => services.GetRequiredService<CollectibilityActivityVersionStore>());
            serviceDescriptors.AddSingleton<CollectibilityActivityStructureService>();
            serviceDescriptors.AddSingleton<IActivityStructureService>(services => services.GetRequiredService<CollectibilityActivityStructureService>());
            serviceDescriptors.AddSingleton<CollectibilityActivityInputOptionsProvider>();
            serviceDescriptors.AddSingleton<IActivityInputOptionsProvider>(services => services.GetRequiredService<CollectibilityActivityInputOptionsProvider>());
            serviceDescriptors.AddSingleton<CollectibilityCommandSender>();
            serviceDescriptors.AddSingleton<ICommandSender>(services => services.GetRequiredService<CollectibilityCommandSender>());
        }
        var services = serviceDescriptors.BuildServiceProvider();
        var routes = new CollectibleRouteBuilder(services);
        mapper.Invoke(null, [routes]);
        publishedDataSource.SetEndpoints(routes.DataSources.SelectMany(source => source.Endpoints));
        var routeCount = routes.DataSources.Sum(source => source.Endpoints.Count);
        var endpointReferences = routes.DataSources
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => new WeakReference(endpoint))
            .ToArray();
        var operationalReferences = new Dictionary<string, WeakReference>(StringComparer.Ordinal);
        if (owner.Owner == "Elsa.Workflows.Design.Api")
        {
            var designEndpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("design/", StringComparison.Ordinal) == true)
                .ToArray();
            if (designEndpoints.Length != 27 || designEndpoints.Any(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>() is null))
                throw new InvalidOperationException("Workflows Design publication did not retain its complete authenticated endpoint metadata.");
            var requestDelegateDescription = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
                ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");
            var selectedDescriptionMethods = designEndpoints
                .Select(endpoint => endpoint.Metadata.GetMetadata<MethodInfo>())
                .ToArray();
            if (selectedDescriptionMethods.Any(method => method != requestDelegateDescription || method.DeclaringType?.Assembly == assembly))
                throw new InvalidOperationException("Workflows Design endpoint descriptions did not select the shared RequestDelegate.Invoke metadata.");

            var httpJsonOptionsValue = services.GetRequiredService<IOptions<JsonOptions>>().Value;
            var ownerJsonResolvers = httpJsonOptionsValue.SerializerOptions.TypeInfoResolverChain
                .Where(resolver => resolver.GetType().FullName == "Elsa.Workflows.Design.Api.WorkflowsDesignJsonTypeInfoResolver")
                .ToArray();
            if (ownerJsonResolvers.Length != 1 || ownerJsonResolvers[0].GetType().Assembly != assembly)
                throw new InvalidOperationException("Workflows Design did not register exactly one owner source-generated JSON resolver with HTTP JSON options.");
            var ownerJsonResolver = ownerJsonResolvers[0];
            var schemaTypes = designEndpoints
                .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
                    .Select(metadata => metadata.Type)
                    .Concat(endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>().Select(metadata => metadata.RequestType)))
                .Where(type => type is not null && type != typeof(void))
                .Distinct()
                .ToArray();
            var missingSchemaTypes = FindUnresolvedOwnerJsonTypes(ownerJsonResolver, httpJsonOptionsValue.SerializerOptions, schemaTypes!, assembly);
            if (missingSchemaTypes.Length > 0)
                throw new InvalidOperationException($"Workflows Design source-generated JSON context is missing OpenAPI types: {string.Join(", ", missingSchemaTypes)}.");

            // Alternate the order across cycles so the owner resolver is proven both
            // after source-generated serialization and before it.
            if (cycle % 2 == 0)
            {
                var preSerializationContextType = assembly.GetType(owner.JsonContextType, throwOnError: true)!;
                var preSerializationDefault = preSerializationContextType.GetProperty(
                    "Default",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as JsonSerializerContext
                    ?? throw new InvalidOperationException($"{owner.JsonContextType}.Default is not a JsonSerializerContext.");
                var preSerializationTypeInfo = preSerializationDefault.GetTypeInfo(schemaTypes[0]!)
                    ?? throw new InvalidOperationException($"{owner.JsonContextType} has no generated metadata for {schemaTypes[0]}.");
                _ = JsonSerializer.Deserialize(owner.JsonSample, preSerializationTypeInfo);
                preSerializationTypeInfo = null;
                preSerializationDefault = null;
                preSerializationContextType = null;
            }

            var openApiProvider = services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
            var openApiDocument = openApiProvider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (openApiDocument.Paths is null || !openApiDocument.Paths.Keys.Any(path => path.Contains("expression-tooling", StringComparison.Ordinal)))
                throw new InvalidOperationException("Workflows Design OpenAPI document did not contain owner operation paths.");
            if (openApiDocument.Components?.Schemas is null ||
                !openApiDocument.Components.Schemas.Keys.Any(schema => schema.Contains("ActivityStructuresResponse", StringComparison.Ordinal) ||
                    schema.Contains("ExpressionToolingDescriptorsResponse", StringComparison.Ordinal)))
                throw new InvalidOperationException("Workflows Design OpenAPI document did not contain owner response schemas.");
            var openApiDocumentReference = new WeakReference(openApiDocument!);
            var openApiProviderReference = new WeakReference(openApiProvider);
            openApiProvider = null!;
            openApiDocument = null!;

            var descriptorEndpoint = designEndpoints.Single(endpoint => endpoint.RoutePattern.RawText == "design/workflows/definitions/{definitionId}" &&
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("DELETE", StringComparer.OrdinalIgnoreCase) == true);
            var delegateBody = new MemoryStream();
            var delegateContext = new DefaultHttpContext
            {
                RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "collectible-author")], "collectible"))
            };
            delegateContext.Response.Body = delegateBody;
            delegateContext.Request.Method = "DELETE";
            delegateContext.Request.Path = "/design/workflows/definitions/collectible-definition";
            delegateContext.Request.RouteValues["definitionId"] = "collectible-definition";
            delegateContext.SetEndpoint(descriptorEndpoint);
            var application = new ApplicationBuilder(services);
            application.UseAuthentication();
            application.UseAuthorization();
            application.Run(httpContext => descriptorEndpoint.RequestDelegate!(httpContext));
            var pipeline = application.Build();
            pipeline(delegateContext).GetAwaiter().GetResult();
            if (delegateContext.Response.StatusCode != StatusCodes.Status204NoContent)
                throw new InvalidOperationException("Workflows Design mapped delete delegate did not execute successfully.");
            var commandSender = services.GetRequiredService<CollectibilityCommandSender>();
            if (commandSender.LastCommand?.GetType().Name != nameof(SoftDeleteDefinition) ||
                !string.Equals(commandSender.LastCommand.GetType().GetProperty("DefinitionId")?.GetValue(commandSender.LastCommand) as string, "collectible-definition", StringComparison.Ordinal))
                throw new InvalidOperationException($"Workflows Design mapped delete delegate did not dispatch its route command ({commandSender.LastCommand?.GetType().FullName ?? "null"}).");

            var expressionProvider = services.GetRequiredService<CollectibilityExpressionToolingProvider>();
            var expressionEndpoint = designEndpoints.Single(endpoint => endpoint.RoutePattern.RawText == "design/workflows/expression-tooling/completions");
            var expressionContext = new DefaultHttpContext
            {
                RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "collectible-author")], "collectible"))
            };
            expressionContext.Request.Method = "POST";
            expressionContext.Request.Path = "/design/workflows/expression-tooling/completions";
            expressionContext.Request.ContentType = "application/json";
            var expressionRequestBody = new MemoryStream(Encoding.UTF8.GetBytes(
                "{\"contractVersion\":{\"major\":1,\"minor\":0},\"workflowDraftId\":\"draft\",\"nodeId\":\"node\",\"propertyKey\":\"text\",\"expressionType\":\"Collectible\",\"documentRevision\":\"document\",\"source\":\"symbols\",\"cursor\":{\"line\":0,\"character\":0}}"));
            expressionContext.Request.Body = expressionRequestBody;
            var expressionResponseBody = new MemoryStream();
            expressionContext.Response.Body = expressionResponseBody;
            expressionContext.SetEndpoint(expressionEndpoint);
            var expressionApplication = new ApplicationBuilder(services);
            expressionApplication.UseAuthentication();
            expressionApplication.UseAuthorization();
            expressionApplication.Run(httpContext => expressionEndpoint.RequestDelegate!(httpContext));
            expressionApplication.Build()(expressionContext).GetAwaiter().GetResult();
            if (expressionContext.Response.StatusCode != StatusCodes.Status200OK || expressionProvider.CompletionCount != 1)
                throw new InvalidOperationException($"Workflows Design mapped expression completion did not invoke its provider (status {expressionContext.Response.StatusCode}, count {expressionProvider.CompletionCount}).");

            var activityStore = services.GetRequiredService<CollectibilityActivityVersionStore>();
            var structureService = services.GetRequiredService<CollectibilityActivityStructureService>();
            var activityOptionsProvider = services.GetRequiredService<CollectibilityActivityInputOptionsProvider>();
            var activityEndpoint = designEndpoints.Single(endpoint => endpoint.RoutePattern.RawText == "design/workflows/activities/{activityVersionId}/inputs/{inputName}/options");
            var activityContext = new DefaultHttpContext
            {
                RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "collectible-author")], "collectible"))
            };
            activityContext.Request.Method = "POST";
            activityContext.Request.Path = "/design/workflows/activities/collectible-activity/inputs/route-input/options";
            activityContext.Request.RouteValues["activityVersionId"] = "collectible-activity";
            activityContext.Request.RouteValues["inputName"] = "route-input";
            activityContext.Request.ContentType = "application/json";
            var activityRequestBody = new MemoryStream(Encoding.UTF8.GetBytes(
                "{\"activityVersionId\":\"wrong-activity\",\"inputName\":\"wrong-input\",\"nodeId\":\"node\",\"workflowState\":{\"rootActivity\":{\"nodeId\":\"root\",\"activityVersionId\":\"root-activity\",\"inputs\":[],\"outputs\":[],\"structure\":{\"kind\":\"collectible\",\"schemaVersion\":\"1\",\"payload\":{}}}}}"));
            activityContext.Request.Body = activityRequestBody;
            var activityResponseBody = new MemoryStream();
            activityContext.Response.Body = activityResponseBody;
            activityContext.SetEndpoint(activityEndpoint);
            var activityApplication = new ApplicationBuilder(services);
            activityApplication.UseAuthentication();
            activityApplication.UseAuthorization();
            activityApplication.Run(httpContext => activityEndpoint.RequestDelegate!(httpContext));
            activityApplication.Build()(activityContext).GetAwaiter().GetResult();
            if (activityContext.Response.StatusCode != StatusCodes.Status200OK || activityStore.CallCount != 1 || structureService.CallCount == 0 || activityOptionsProvider.CallCount != 1)
                throw new InvalidOperationException("Workflows Design mapped activity options did not traverse store, structure, and provider seams.");

            operationalReferences["openapi-provider"] = openApiProviderReference;
            operationalReferences["openapi-document"] = openApiDocumentReference;
            operationalReferences["command-sender"] = new WeakReference(commandSender);
            operationalReferences["expression-provider"] = new WeakReference(expressionProvider);
            operationalReferences["activity-store"] = new WeakReference(activityStore);
            operationalReferences["activity-structure"] = new WeakReference(structureService);
            operationalReferences["activity-options-provider"] = new WeakReference(activityOptionsProvider);
            expressionRequestBody.Dispose();
            expressionResponseBody.Dispose();
            activityRequestBody.Dispose();
            activityResponseBody.Dispose();
            delegateBody.Dispose();
            expressionContext = null!;
            activityContext = null!;
            delegateContext = null!;
            activityStore = null!;
            structureService = null!;
            expressionProvider = null!;
            activityOptionsProvider = null!;
            expressionApplication = null!;
            activityApplication = null!;
            application = null!;
            pipeline = null!;
            descriptorEndpoint = null!;
        }
        // Exercise the same source-generated context/options path used by the production endpoint
        // result writers. Reflection metadata is intentionally not a fallback here: it retains
        // collectible owner types through process-global resolver caches.
        var serializerContextType = assembly.GetType(owner.JsonContextType, throwOnError: true)!;
        var defaultContextProperty = serializerContextType.GetProperty(
            "Default",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{owner.JsonContextType} does not expose Default.");
        var serializerContext = defaultContextProperty.GetValue(null) as JsonSerializerContext
            ?? throw new InvalidOperationException($"{owner.JsonContextType}.Default is not a JsonSerializerContext.");
        var typeInfoProperty = serializerContextType.GetProperty(
            owner.JsonTypeInfoProperty,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{owner.JsonContextType} has no generated {owner.JsonTypeInfoProperty} metadata.");
        var typeInfo = typeInfoProperty.GetValue(serializerContext) as JsonTypeInfo
            ?? throw new InvalidOperationException($"{owner.JsonContextType}.{typeInfoProperty.Name} is not JsonTypeInfo.");
        var serializerOptions = serializerContext.Options;
        // Deserialize and re-serialize a real request/response DTO through the same generated
        // metadata used by production handlers. This catches accidental reflection fallback while
        // ensuring the DTO instance itself is not the retained root after publication is released.
        var serializedDto = JsonSerializer.Deserialize(owner.JsonSample, typeInfo)
            ?? throw new InvalidOperationException($"{owner.JsonContextType}.{owner.JsonTypeInfoProperty} could not deserialize its production sample.");
        var responseBody = new MemoryStream();
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.Response.Body = responseBody;
        var result = Results.Json(serializedDto, typeInfo);
        result.ExecuteAsync(httpContext).GetAwaiter().GetResult();
        if (responseBody.Length == 0)
            throw new InvalidOperationException($"{owner.Owner} production JSON result wrote no response payload.");
        var payloadReference = new WeakReference(serializedDto);
        var resultReference = new WeakReference(result);
        var httpContextReference = new WeakReference(httpContext);
        serializedDto = null;
        result = null;
        httpContext = null;
        responseBody.Dispose();
        responseBody = null;
        publishedDataSource.SetEndpoints([]);
        routes.DataSources.Clear();
        ((IDisposable)services).Dispose();

        var loadContextReference = new WeakReference(loadContext);
        var assemblyReference = new WeakReference(assembly);
        var mapperTypeReference = new WeakReference(mapperType);
        var featureTypeReference = new WeakReference(featureType);
        var featureReference = new WeakReference(feature);
        var serviceProviderReference = new WeakReference(services);
        var serializerOptionsReference = new WeakReference(serializerOptions);
        var serializerContextReference = new WeakReference(serializerContext);
        var serializerTypeInfoReference = new WeakReference(typeInfo);
        assembly = null;
        mapperType = null;
        mapper = null;
        featureType = null;
        feature = null;
        configureServices = null;
        serviceDescriptors = null;
        serializerOptions = null;
        serializerContext = null;
        typeInfo = null;
        serializerContextType = null;
        defaultContextProperty = null;
        typeInfoProperty = null;
        routes = null;
        services = null;
        loadContext!.Unload();
        loadContext = null;

        return new CollectibilityEvidence(
            cycleId,
            routeCount,
            loadContextReference,
            assemblyReference,
            mapperTypeReference,
            new Dictionary<string, WeakReference>(StringComparer.Ordinal)
            {
                ["load-context"] = loadContextReference,
                ["assembly"] = assemblyReference,
                ["mapper-type"] = mapperTypeReference,
                ["feature-type"] = featureTypeReference,
                ["feature"] = featureReference,
                ["service-provider"] = serviceProviderReference,
                ["serializer-options"] = serializerOptionsReference,
                ["serializer-context"] = serializerContextReference,
                ["serializer-type-info"] = serializerTypeInfoReference,
                ["payload"] = payloadReference,
                ["result"] = resultReference,
                ["http-context"] = httpContextReference
            }.Concat(endpointReferences.Select((reference, index) => new KeyValuePair<string, WeakReference>($"endpoint-{index}", reference)))
                .Concat(operationalReferences)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    private static string[] FindUnresolvedOwnerJsonTypes(
        IJsonTypeInfoResolver resolver,
        JsonSerializerOptions options,
        IEnumerable<Type> roots,
        Assembly ownerAssembly)
    {
        var pending = new Queue<Type>(roots.Where(type => type is not null)!);
        var visited = new HashSet<Type>();
        var unresolved = new HashSet<string>(StringComparer.Ordinal);

        while (pending.Count > 0)
        {
            var type = pending.Dequeue();
            if (!visited.Add(type))
                continue;

            var typeInfo = resolver.GetTypeInfo(type, options);
            if (typeInfo is null)
            {
                if (type.Assembly == ownerAssembly)
                    unresolved.Add(type.FullName ?? type.Name);
                continue;
            }

            foreach (var propertyType in typeInfo.Properties.Select(property => property.PropertyType))
                pending.Enqueue(propertyType);
            if (typeInfo.ElementType is not null)
                pending.Enqueue(typeInfo.ElementType);
            if (typeInfo.KeyType is not null)
                pending.Enqueue(typeInfo.KeyType);
        }

        return unresolved.Order(StringComparer.Ordinal).ToArray();
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
        string.Join(",", references.Where(pair => pair.Value.IsAlive).Select(pair => pair.Key));

    private sealed record CollectibilityEvidence(
        Guid CycleId,
        int RouteCount,
        WeakReference LoadContext,
        WeakReference Assembly,
        WeakReference MapperType,
        IReadOnlyDictionary<string, WeakReference> References);

    private sealed class CollectibleRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
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

    private sealed class CollectibilityActivityVersionStore : IActivityDefinitionVersionStore
    {
        public int CallCount { get; private set; }
        private readonly ActivityDefinitionVersion _version = new("1.0.0", "collectible-definition")
        {
            Id = "collectible-activity",
            Inputs =
            [
                new InputDefinition("route-input", "route-input", new("String"), null, "Route input", null, false,
                    UISpecifications: JsonSerializer.SerializeToElement(new { optionsProvider = new { key = "collectible-options" } }))
            ]
        };
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_version);
        }
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => Task.FromResult(_version);
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersion?>(_version);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([_version]);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([_version]);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([_version]);
    }

    private sealed class CollectibilityActivityStructureService : IActivityStructureService
    {
        public int CallCount { get; private set; }
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity)
        {
            CallCount++;
            return [new ActivityChildProjection("collectible", [new ActivityNode("node", "collectible-activity", [], [])])];
        }
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;
        public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }

    private sealed class CollectibilityActivityInputOptionsProvider : IActivityInputOptionsProvider
    {
        public string Key => "collectible-options";
        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(ActivityInputOptionsContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult<IReadOnlyList<ActivityInputOption>>([new("collectible", "Collectible")]);
        }
    }

    private sealed class CollectibilityCommandSender : ICommandSender
    {
        public object? LastCommand { get; private set; }

        public Task<T> Send<T>(ICommand<T> command, CancellationToken cancellationToken = default) where T : notnull
        {
            LastCommand = command;
            return Task.FromResult(default(T)!);
        }

        public Task Send(ICommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.CompletedTask;
        }
    }

    private sealed class ProductionApiLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CollectibilityExpressionToolingProvider : IExpressionToolingProvider
    {
        public int CompletionCount { get; private set; }
        public string ExpressionType => "Collectible";
        public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;

        public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(
            ExpressionToolingRequestScope scope,
            CancellationToken cancellationToken) => ValueTask.FromResult(
            ExpressionToolingOutcome<ExpressionToolingCapabilities>.Success(
                new(), SupportedVersion, scope.Document.DocumentRevision, scope.Context.ContextRevision));

        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(
            ExpressionCompletionRequest request,
            CancellationToken cancellationToken)
        {
            CompletionCount++;
            return ValueTask.FromResult(
                ExpressionToolingOutcome<ExpressionToolingItems>.Success(
                    new(new ExpressionToolingItem[] { }), SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));
        }

        public ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(
            ExpressionHoverRequest request,
            CancellationToken cancellationToken) => ValueTask.FromResult(
            ExpressionToolingOutcome<ExpressionHover>.Success(
                new(""), SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));

        public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(
            ExpressionValidationRequest request,
            CancellationToken cancellationToken) => ValueTask.FromResult(
            ExpressionToolingOutcome<ExpressionDiagnosticSet>.Success(
                new(Array.Empty<ExpressionDiagnostic>()), SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));
    }

    private sealed class CollectibilityExpressionToolingProviderResolver(IExpressionToolingProvider provider) : IExpressionToolingProviderResolver
    {
        public IExpressionToolingProvider? Find(string expressionType) =>
            string.Equals(expressionType, provider.ExpressionType, StringComparison.Ordinal) ? provider : null;
    }

    private sealed class CollectibilityExpressionAuthoringContextSource : IExpressionAuthoringContextSource
    {
        public ValueTask<ExpressionAuthoringContext?> TryResolveAsync(
            ResolveExpressionAuthoringContextRequest request,
            ExpressionAuthoringAuthorization authorization,
            CancellationToken cancellationToken)
        {
            var document = new ExpressionAuthoringDocument(request.WorkflowDraftId, request.WorkflowDraftId, request.NodeId,
                request.PropertyKey, request.ExpressionType, request.DocumentRevision);
            var context = new ExpressionAuthoringContext(request.ContractVersion, document, request.ContextRevision ?? "context", "symbols", [], new());
            return ValueTask.FromResult<ExpressionAuthoringContext?>(context);
        }
    }

    private sealed class CollectibilityAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ElsaCollectibility";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(Scheme.Name);
            identity.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));
            identity.AddClaim(new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard));
            identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, "collectible-tenant"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wave1MinimalApiCollectibilityCollection
{
    public const string Name = "Wave 1 Minimal API collectibility";
}
