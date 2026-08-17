using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
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
    public async Task Runtime_owner_is_collectible_after_alternating_real_mapping_openapi_and_serializer_use()
    {
        var failures = new List<string>();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var evidence = await CreateAndRelease(cycle);
            Assert.Equal(24, evidence.OpenApiOperationCount);
            var collected = WaitForCollection(evidence.References);
            var unload = UnloadEvidence.Verify(evidence.CycleId, evidence.LoadContext, evidence.Assembly, evidence.MapperType, 32);
            if (!unload.Collected || !collected)
                failures.Add($"cycle {cycle}: {unload.Diagnostic ?? "owner retained"}; alive={string.Join(",", evidence.References.Where(x => x.Value.IsAlive).Select(x => x.Key))}");
        }

        if (failures.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join("\n", failures));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<Evidence> CreateAndRelease(int cycle)
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
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "Elsa.Workflows.Runtime.Api",
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddRouting();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(providerProbeState);
        builder.Services.AddScoped<CollectibleRuntimeProviderProbe>();
        builder.Services.AddSingleton<IStimulusRouter, CollectibleStimulusRouter>();
        builder.Services.AddScoped<IPermissionResourceHandler, CollectibleResourceHandler>();
        builder.Services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(["Wave9Collectibility"], StringComparer.Ordinal));
        builder.Services.AddSingleton<IRequestSender>(serviceProvider => new CollectibleRequestSender(serviceProvider.GetRequiredService<IServiceScopeFactory>(), providerProbeState));
        builder.Services.AddSingleton<ICommandSender, CollectibleCommandSender>();
        builder.Services.AddDynamicEndpointApiExplorerRefresh();
        builder.Services.AddOpenApi();
        featureType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.Instance)!.Invoke(feature, [builder.Services]);
        builder.Services.AddAuthentication("Wave9Collectibility")
            .AddScheme<AuthenticationSchemeOptions, CollectibleAuthenticationHandler>("Wave9Collectibility", _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        mapper.Invoke(null, [app]);
        await app.StartAsync().ConfigureAwait(false);
        var services = app.Services;
        var client = app.GetTestClient();
        var jsonOptions = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value;
        var jsonResolver = jsonOptions.SerializerOptions.TypeInfoResolverChain.FirstOrDefault(resolver => resolver.GetType().Name == "WorkflowsRuntimeJsonTypeInfoResolver");
        Assert.NotNull(jsonResolver);
        Assert.Same(jsonResolver, jsonOptions.SerializerOptions.TypeInfoResolverChain.First(resolver => resolver.GetType().Name == "WorkflowsRuntimeJsonTypeInfoResolver"));
        var endpoints = services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.Owner == "Elsa.Workflows.Runtime.Api")
            .ToArray();
        Assert.Equal(24, endpoints.Length);

        var openApiProvider = services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        OpenApiDocument? openApiDocument = null;
        var openApiOperationCount = 0;
        if (cycle % 2 == 1)
        {
            openApiDocument = openApiProvider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
            openApiOperationCount = openApiDocument.Paths?.Sum(path => path.Value.Operations?.Count ?? 0) ?? 0;
        }

        ExerciseRealPipeline(client, providerProbeState);

        if (cycle % 2 == 0)
        {
            openApiDocument = openApiProvider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
            openApiOperationCount = openApiDocument.Paths?.Sum(path => path.Value.Operations?.Count ?? 0) ?? 0;
        }

        Assert.Equal(24, openApiOperationCount);
        Assert.True(openApiDocument?.Paths?.ContainsKey("/runtime/workflows/instances/{workflowExecutionId}") == true);
        Assert.Equal(1, providerProbeState.HandlerInvocations);
        Assert.Equal(1, providerProbeState.ExecutionStoreCalls);
        Assert.Equal(1, providerProbeState.DispatchStoreCalls);
        Assert.Equal(1, providerProbeState.ExecutableStoreCalls);
        Assert.Equal(1, providerProbeState.SourceReferenceStoreCalls);
        Assert.Equal(1, providerProbeState.AlterationStoreCalls);
        Assert.Equal(1, providerProbeState.DiagnosticsStoreCalls);

        Assert.Equal(2, providerProbeState.ResourceHandlerInvocations);
        Assert.Equal(1, providerProbeState.BodyBoundRequestInvocations);
        var references = new Dictionary<string, WeakReference>(StringComparer.Ordinal)
        {
            ["load-context"] = new(loadContext),
            ["assembly"] = new(assembly),
            ["mapper-type"] = new(mapperType),
            ["feature-type"] = new(featureType),
            ["feature"] = new(feature),
            ["services"] = new(services),
            ["app"] = new(app),
            ["client"] = new(client),
            ["provider-probe-state"] = new(providerProbeState),
            ["json-resolver"] = new(jsonResolver!)
        };
        if (openApiDocument is not null)
            references["openapi-document"] = new(openApiDocument);
        foreach (var endpoint in endpoints.Select((endpoint, index) => (endpoint, index)))
            references[$"endpoint-{endpoint.index}"] = new(endpoint.endpoint);

        client.Dispose();
        await app.StopAsync().ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
        endpoints = null!;
        featureType = null!;
        mapper = null!;
        mapperType = null!;
        services = null!;
        openApiDocument = null!;
        openApiProvider = null!;
        builder = null!;
        app = null!;
        loadContext.Unload();
        loadContext = null!;
        assembly = null!;
        return new(Guid.NewGuid(), references["load-context"], references["assembly"], references["mapper-type"], references, openApiOperationCount);
    }

    private static void ExerciseRealPipeline(HttpClient client, CollectibleProviderProbeState state)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/runtime/workflows/instances/collectible-instance"))
        {
            request.Headers.TryAddWithoutValidation("X-Wave9-Collectibility-Tenant", "tenant-a");
            using var response = client.SendAsync(request).GetAwaiter().GetResult();
            Assert.Equal(StatusCodes.Status200OK, (int)response.StatusCode);
            using var document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("collectible-instance", document.RootElement.GetProperty("instance").GetProperty("workflowExecutionId").GetString());
        }

        using (var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/executables/collectible-artifact/execute")
        {
            Content = new StringContent("{\"inputs\":{\"answer\":42}}", Encoding.UTF8, "application/json")
        })
        {
            request.Headers.TryAddWithoutValidation("X-Wave9-Collectibility-Tenant", "tenant-a");
            using var response = client.SendAsync(request).GetAwaiter().GetResult();
            Assert.Equal(StatusCodes.Status200OK, (int)response.StatusCode);
            using var document = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("collectible-artifact", document.RootElement.GetProperty("artifactId").GetString());
        }

        Assert.Equal(1, state.HandlerInvocations);
        Assert.Equal(1, state.BodyBoundRequestInvocations);
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

    private sealed class RuntimeLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CollectibleRequestSender(IServiceScopeFactory scopeFactory, CollectibleProviderProbeState state) : IRequestSender
    {
        public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<CollectibleRuntimeProviderProbe>().ProbeAsync(request, cancellationToken);
            if (request is GetWorkflowInstance)
            {
                var summary = new WorkflowInstanceSummaryView(
                    "collectible-instance",
                    "collectible-artifact",
                    "collectible-definition",
                    "collectible-version",
                    "1.0.0",
                    "collectible-hash",
                    "Running",
                    "Root",
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    null,
                    null,
                    null,
                    null,
                    null,
                    0,
                    0);
                var details = new WorkflowInstanceDetailsView(summary, [], [], new Dictionary<string, WorkflowOutputView>(), "Immediate", null, "activity-level");
                return (T)(object)new GetWorkflowInstanceResponse(details);
            }

            if (request is ExecuteWorkflow)
            {
                state.BodyBoundRequestInvocations++;
                return (T)(object)new WorkflowExecutionStartDispatchView(
                    "collectible-execution",
                    "collectible-artifact",
                    "1.0.0",
                    "collectible-hash",
                    "Accepted",
                    "collectible-envelope",
                    "collectible-agent",
                    "collectibility",
                    null);
            }

            return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        }
    }

    private sealed class CollectibleResourceHandler(CollectibleProviderProbeState state) : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            state.ResourceHandlerInvocations++;
            return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Success);
        }
    }

    private sealed class CollectibleStimulusRouter : IStimulusRouter
    {
        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new StimulusRoutingResult([], []));
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

    private sealed class CollectibleAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard),
                    new Claim(IdentityClaimTypes.TenantId, "tenant-a"),
                    new Claim(IdentityClaimTypes.Normalized, "v1")
                ],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class CollectibleProviderProbeState
    {
        public int HandlerInvocations { get; set; }
        public int BodyBoundRequestInvocations { get; set; }
        public int ResourceHandlerInvocations { get; set; }
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
