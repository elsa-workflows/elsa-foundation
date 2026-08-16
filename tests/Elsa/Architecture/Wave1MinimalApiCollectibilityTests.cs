using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Claims;
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
            serviceDescriptors.AddSingleton<IExpressionToolingProvider, CollectibilityExpressionToolingProvider>();
        var services = serviceDescriptors.BuildServiceProvider();
        var routes = new CollectibleRouteBuilder(services);
        mapper.Invoke(null, [routes]);
        var routeCount = routes.DataSources.Sum(source => source.Endpoints.Count);
        if (owner.Owner == "Elsa.Workflows.Design.Api")
        {
            var designEndpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
            if (designEndpoints.Length != 27 || designEndpoints.Any(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>() is null))
                throw new InvalidOperationException("Workflows Design publication did not retain its complete authenticated endpoint metadata.");

            var descriptorEndpoint = designEndpoints.Single(endpoint => endpoint.RoutePattern.RawText == "design/workflows/expression-tooling/descriptors");
            using var delegateBody = new MemoryStream();
            var delegateContext = new DefaultHttpContext
            {
                RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "collectible-author")], "collectible"))
            };
            delegateContext.Response.Body = delegateBody;
            delegateContext.Request.Method = "GET";
            delegateContext.Request.Path = "/design/workflows/expression-tooling/descriptors";
            delegateContext.SetEndpoint(descriptorEndpoint);
            var application = new ApplicationBuilder(services);
            application.UseAuthentication();
            application.UseAuthorization();
            application.Run(httpContext => descriptorEndpoint.RequestDelegate!(httpContext));
            var pipeline = application.Build();
            pipeline(delegateContext).GetAwaiter().GetResult();
            if (delegateContext.Response.StatusCode != StatusCodes.Status200OK || delegateBody.Length == 0)
                throw new InvalidOperationException("Workflows Design mapped descriptor delegate did not execute successfully.");
            delegateContext = null!;
        }
        var endpointReferences = routes.DataSources
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => new WeakReference(endpoint))
            .ToArray();
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
        routes.DataSources.Clear();
        services.Dispose();

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
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
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

    private sealed class ProductionApiLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CollectibilityExpressionToolingProvider : IExpressionToolingProvider
    {
        public string ExpressionType => "Collectible";
        public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;

        public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(
            ExpressionToolingRequestScope scope,
            CancellationToken cancellationToken) => ValueTask.FromResult(
            ExpressionToolingOutcome<ExpressionToolingCapabilities>.Success(
                new(), SupportedVersion, scope.Document.DocumentRevision, scope.Context.ContextRevision));

        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(
            ExpressionCompletionRequest request,
            CancellationToken cancellationToken) => ValueTask.FromResult(
            ExpressionToolingOutcome<ExpressionToolingItems>.Success(
                new(new ExpressionToolingItem[] { }), SupportedVersion, request.Scope.Document.DocumentRevision, request.Scope.Context.ContextRevision));

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
