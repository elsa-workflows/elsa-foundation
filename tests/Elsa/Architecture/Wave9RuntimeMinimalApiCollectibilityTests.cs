using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Executes the Runtime mapper, generated serializer, policy evaluator, and a real route delegate in three collectible cycles.</summary>
[Collection(Wave9RuntimeCollectibilityCollection.Name)]
public sealed class Wave9RuntimeMinimalApiCollectibilityTests
{
    [Fact]
    public void Runtime_owner_is_collectible_after_alternating_real_mapping_openapi_and_serializer_use()
    {
        var failures = new List<string>();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var includeOpenApi = cycle % 2 == 0;
            var evidence = CreateAndRelease(cycle, includeOpenApi, useSerializer: true);
            Assert.Equal(includeOpenApi ? 24 : 0, evidence.OpenApiOperationCount);
            var collected = WaitForCollection(evidence.References);
            var unload = UnloadEvidence.Verify(evidence.CycleId, evidence.LoadContext, evidence.Assembly, evidence.MapperType, 32);
            if (!unload.Collected || !collected)
                failures.Add($"cycle {cycle}: {unload.Diagnostic ?? "owner retained"}; alive={string.Join(",", evidence.References.Where(x => x.Value.IsAlive).Select(x => x.Key))}");
        }

        if (failures.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join("\n", failures));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Evidence CreateAndRelease(int cycle, bool includeOpenApi, bool useSerializer)
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
        var providerProbeState = new CollectibleProviderProbeState();
        var descriptors = new ServiceCollection();
        descriptors.AddLogging();
        descriptors.AddRouting();
        descriptors.AddSingleton(providerProbeState);
        descriptors.AddScoped<CollectibleRuntimeProviderProbe>();
        descriptors.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(["Wave9Collectibility"], StringComparer.Ordinal));
        descriptors.AddSingleton<IRequestSender>(serviceProvider => new CollectibleRequestSender(serviceProvider.GetRequiredService<IServiceScopeFactory>()));
        descriptors.AddSingleton<ICommandSender, CollectibleCommandSender>();
        var publishedEndpoints = new CollectibleEndpointDataSource();
        descriptors.AddSingleton<EndpointDataSource>(publishedEndpoints);
        descriptors.AddSingleton<IHostEnvironment>(new CollectibilityHostEnvironment());
        if (includeOpenApi)
        {
            descriptors.AddDynamicEndpointApiExplorerRefresh();
            descriptors.AddOpenApi();
        }
        featureType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.Instance)!.Invoke(feature, [descriptors]);
        descriptors.AddAuthentication("Wave9Collectibility")
            .AddScheme<AuthenticationSchemeOptions, CollectibleAuthenticationHandler>("Wave9Collectibility", _ => { });
        descriptors.AddAuthorization();
        var services = descriptors.BuildServiceProvider();
        var jsonOptions = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value;
        var jsonResolver = jsonOptions.SerializerOptions.TypeInfoResolverChain.FirstOrDefault(resolver => resolver.GetType().Name == "WorkflowsRuntimeJsonTypeInfoResolver");
        Assert.NotNull(jsonResolver);
        if (includeOpenApi)
        {
            Assert.Same(jsonResolver, jsonOptions.SerializerOptions.TypeInfoResolverChain.First(resolver => resolver.GetType().Name == "WorkflowsRuntimeJsonTypeInfoResolver"));
        }
        var routes = new RouteBuilder(services);
        mapper.Invoke(null, [routes]);
        publishedEndpoints.SetSources(routes.DataSources);
        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.Owner == "Elsa.Workflows.Runtime.Api")
            .ToArray();
        IOpenApiDocumentProvider? openApiProvider = null;
        OpenApiDocument? openApiDocument = null;
        var openApiOperationCount = 0;
        if (includeOpenApi)
        {
            openApiProvider = services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
            openApiDocument = openApiProvider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
            var openApiPaths = openApiDocument.Paths;
            openApiOperationCount = openApiPaths?.Sum(path => path.Value.Operations?.Count ?? 0) ?? 0;
            Assert.Equal(24, openApiOperationCount);
            Assert.True(openApiPaths?.ContainsKey("/runtime/workflows/instances/{workflowExecutionId}") == true);
        }
        Assert.Equal(24, endpoints.Length);

        var representative = endpoints.SingleOrDefault(endpoint => endpoint.RoutePattern.RawText == "runtime/workflows/instances/{workflowExecutionId}")
            ?? throw new InvalidOperationException($"Runtime instance route was not published. Routes: {string.Join(",", endpoints.Select(endpoint => endpoint.RoutePattern.RawText))}");
        var responseBody = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.RouteValues["workflowExecutionId"] = "collectible-instance";
        http.Response.Body = responseBody;
        var authenticationResult = services.GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(http, "Wave9Collectibility").GetAwaiter().GetResult();
        Assert.True(authenticationResult.Succeeded);
        http.User = authenticationResult.Principal!;
        representative.RequestDelegate!(http).GetAwaiter().GetResult();
        Assert.Equal(StatusCodes.Status404NotFound, http.Response.StatusCode);
        Assert.Equal(1, providerProbeState.HandlerInvocations);
        Assert.Equal(1, providerProbeState.ExecutionStoreCalls);
        Assert.Equal(1, providerProbeState.DispatchStoreCalls);
        Assert.Equal(1, providerProbeState.ExecutableStoreCalls);
        Assert.Equal(1, providerProbeState.SourceReferenceStoreCalls);
        Assert.Equal(1, providerProbeState.AlterationStoreCalls);
        Assert.Equal(1, providerProbeState.DiagnosticsStoreCalls);

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard)], "Wave9Collectibility"));
        var evaluator = services.GetRequiredService<IPermissionEvaluator>();
        Assert.True(evaluator.EvaluateAsync(new PermissionEvaluationContext(principal, "workflow-runtime.read")).GetAwaiter().GetResult().Succeeded);

        JsonTypeInfo? typeInfo = null;
        object? value = null;
        MemoryStream? output = null;
        if (useSerializer)
        {
            var responseType = assembly.GetType("Elsa.Workflows.Runtime.Api.Models.WorkflowInstanceListView", true)!;
            typeInfo = jsonOptions.SerializerOptions.GetTypeInfo(responseType);
            value = JsonSerializer.Deserialize("{\"items\":[],\"nextCursor\":null,\"hasNext\":false,\"count\":0,\"totalCount\":0}", typeInfo)!;
            output = new MemoryStream();
            JsonSerializer.Serialize(output, value, typeInfo);
            Assert.NotEmpty(output.ToArray());
        }

        var references = new Dictionary<string, WeakReference>(StringComparer.Ordinal)
        {
            ["load-context"] = new(loadContext),
            ["assembly"] = new(assembly),
            ["mapper-type"] = new(mapperType),
            ["feature-type"] = new(featureType),
            ["feature"] = new(feature),
            ["services"] = new(services),
            ["response-body"] = new(responseBody),
            ["provider-probe-state"] = new(providerProbeState),
            ["json-resolver"] = new(jsonResolver!)
        };
        if (typeInfo is not null)
            references["serializer-type-info"] = new(typeInfo);
        if (openApiDocument is not null)
            references["openapi-document"] = new(openApiDocument);
        foreach (var endpoint in endpoints.Select((endpoint, index) => (endpoint, index)))
            references[$"endpoint-{endpoint.index}"] = new(endpoint.endpoint);

        publishedEndpoints.SetSources([]);
        routes.DataSources.Clear();
        services.Dispose();
        responseBody.Dispose();
        representative = null!;
        endpoints = null!;
        feature = null!;
        featureType = null!;
        mapper = null!;
        mapperType = null!;
        typeInfo = null;
        value = null;
        output?.Dispose();
        descriptors = null!;
        services = null!;
        routes = null!;
        http = null!;
        openApiProvider = null!;
        openApiDocument = null!;
        loadContext.Unload();
        loadContext = null!;
        assembly = null!;
        return new(Guid.NewGuid(), references["load-context"], references["assembly"], references["mapper-type"], references, openApiOperationCount);
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

    private sealed record Evidence(Guid CycleId, WeakReference LoadContext, WeakReference Assembly, WeakReference MapperType, IReadOnlyDictionary<string, WeakReference> References, int OpenApiOperationCount);

    private sealed class CollectibleEndpointDataSource : EndpointDataSource
    {
        private IReadOnlyList<EndpointDataSource> _sources = [];
        private CancellationTokenSource _changeTokenSource = new();

        public void SetSources(IEnumerable<EndpointDataSource> sources)
        {
            Volatile.Write(ref _sources, sources.ToArray());
            var previous = Interlocked.Exchange(ref _changeTokenSource, new CancellationTokenSource());
            previous.Cancel();
            previous.Dispose();
        }

        public override IReadOnlyList<Endpoint> Endpoints => Volatile.Read(ref _sources).SelectMany(source => source.Endpoints).ToArray();

        public override IChangeToken GetChangeToken() =>
            new CancellationChangeToken(Volatile.Read(ref _changeTokenSource).Token);
    }

    private sealed class RouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class CollectibilityHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = typeof(Wave9RuntimeMinimalApiCollectibilityTests).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RuntimeLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CollectibleRequestSender(IServiceScopeFactory scopeFactory) : IRequestSender
    {
        public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<CollectibleRuntimeProviderProbe>().ProbeAsync(request, cancellationToken);
            return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        }
    }

    private sealed class CollectibleRuntimeProviderProbe(
        IWorkflowExecutionStateStore executionStore,
        IWorkflowDispatchStore dispatchStore,
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowAlterationStore alterationStore,
        IRuntimeDiagnosticsSettingsStore diagnosticsStore,
        CollectibleProviderProbeState state)
    {
        public async ValueTask ProbeAsync(object request, CancellationToken cancellationToken)
        {
            if (!string.Equals(request.GetType().Name, "GetWorkflowInstance", StringComparison.Ordinal))
                return;

            state.HandlerInvocations++;
            await executionStore.FindAsync("collectible-instance", cancellationToken);
            state.ExecutionStoreCalls++;
            await dispatchStore.FindAsync("collectible-dispatch", cancellationToken);
            state.DispatchStoreCalls++;
            await executableStore.FindAsync("collectible-artifact", cancellationToken);
            state.ExecutableStoreCalls++;
            await sourceReferenceStore.FindAsync("collectible-source", cancellationToken);
            state.SourceReferenceStoreCalls++;
            await alterationStore.FindPlanAsync("collectible-plan", cancellationToken);
            state.AlterationStoreCalls++;
            await diagnosticsStore.LoadAsync("collectibility", cancellationToken);
            state.DiagnosticsStoreCalls++;
        }
    }

    [Obsolete]
    private sealed class CollectibleAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder, clock)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard),
                    new Claim(IdentityClaimTypes.Normalized, "v1")
                ],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class CollectibleProviderProbeState
    {
        public int HandlerInvocations { get; set; }
        public int ExecutionStoreCalls { get; set; }
        public int DispatchStoreCalls { get; set; }
        public int ExecutableStoreCalls { get; set; }
        public int SourceReferenceStoreCalls { get; set; }
        public int AlterationStoreCalls { get; set; }
        public int DiagnosticsStoreCalls { get; set; }
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
