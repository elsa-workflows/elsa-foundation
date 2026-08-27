using Elsa.Agent.Api;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeEndpoints;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Xunit;

namespace Elsa.Architecture.Tests;

[Collection(Wave4AgentFastEndpointsCollection.Name)]
public sealed class Wave4AgentCollectibilityTests
{
    [Fact]
    public void Three_real_route_publication_cycles_release_agent_assembly_routes_di_and_serializer_metadata()
    {
        var failures = new List<string>();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var evidence = CreateAndUnload(cycle);
            var unload = UnloadEvidence.Verify(evidence.Id, evidence.LoadContext, evidence.Assembly, evidence.MapperType, 32);
            if (!unload.Collected || !WaitForCollection(evidence.References))
                failures.Add($"cycle {cycle}: {unload.Diagnostic ?? "retained"}; alive={string.Join(",", evidence.References.Where(pair => pair.Value.IsAlive).Select(pair => pair.Key))}");
        }

        Assert.Empty(failures);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Evidence CreateAndUnload(int cycle)
    {
        var path = typeof(AgentApi).Assembly.Location;
        var loadContext = new Wave4LoadContext($"Elsa.Wave4.Agent.{cycle}.{Guid.NewGuid():N}");
        var assembly = loadContext.LoadFromAssemblyPath(path);
        var mapperType = assembly.GetType("Elsa.Agent.Api.AgentApi", true)!;
        var mapper = mapperType.GetMethod("MapAgentApi", BindingFlags.Public | BindingFlags.Static)!;
        var featureType = assembly.GetType("Elsa.Agent.Api.FoundationAgentApiFeature", true)!;
        var feature = Activator.CreateInstance(featureType)!;
        var descriptors = new ServiceCollection().AddLogging().AddRouting();
        // The owner assembly is deliberately loaded collectibly here, so its contract types ARE
        // collectible and the fail-closed lifetime boundary would reject the mapping outright.
        // This probe is the regime the explicit suppression exists for: no host-lifetime OpenAPI
        // document survives the cycle, and the weak-reference assertions below prove the release.
        descriptors.SuppressEndpointLifetimeEnforcement();
        descriptors.AddAuthentication(Wave4AgentAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, Wave4AgentAuthenticationHandler>(
                Wave4AgentAuthenticationHandler.SchemeName, _ => { });
        descriptors.AddAuthorization();
        featureType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.Instance)!.Invoke(feature, [descriptors]);
        var provider = new Wave4CollectibilityProvider();
        var providers = new Wave4CollectibilityProviderRegistry(provider);
        var streaming = new Wave4CollectibilityStreamingService();
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentSessionService, Wave4FixedSessionService>());
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentPolicyEvaluator, Wave4FixedPolicyEvaluator>());
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentContextCollector, Wave4FixedContextCollector>());
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentContextSanitizer, Wave4FixedContextSanitizer>());
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentCapabilityCatalog, Wave4FixedCapabilityCatalog>());
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentProviderRegistry>(providers));
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentStreamingService>(streaming));
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentFeedbackService, Wave4FixedFeedbackService>());
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentProposalService, Wave4FixedProposalService>());
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentAuditReader, Wave4FixedAuditReader>());
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentAuditSink, Wave4FixedAuditSink>());
        descriptors.Replace(ServiceDescriptor.Singleton<IAgentTurnRegistry, Wave4FixedTurnRegistry>());
        var services = descriptors.BuildServiceProvider();
        var routes = new Wave4RouteBuilder(services);
        mapper.Invoke(null, [routes]);
        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).ToArray();
        Assert.Equal(11, endpoints.Length);
        var namedEndpoints = endpoints.Cast<RouteEndpoint>()
            .ToDictionary(endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()!.EndpointName!, StringComparer.Ordinal);
        Assert.All(namedEndpoints.Values, endpoint =>
        {
            Assert.Equal("Elsa.Agent.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId);
            Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
            Assert.Single(endpoint.Metadata.GetOrderedMetadata<EndpointSecurityDispositionMetadata>());
            Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        });

        DefaultHttpContext? authenticationContext = CreateContext(services, new ClaimsPrincipal(), HttpMethod.Get, "/_elsa/agent/bootstrap", null, CancellationToken.None, null);
        authenticationContext.Request.Headers[Wave4AgentHost.IdentityHeader] = "use|actor-1|tenant-1";
        var authenticationResult = services.GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(authenticationContext, Wave4AgentAuthenticationHandler.SchemeName)
            .GetAwaiter()
            .GetResult();
        Assert.True(authenticationResult.Succeeded);
        var principal = authenticationResult.Principal!;
        var bootstrap = Invoke(namedEndpoints["ElsaAgentApiEndpointsBootstrap"], services, principal, HttpMethod.Get, "/_elsa/agent/bootstrap");
        Assert.Equal(StatusCodes.Status200OK, bootstrap.Response.StatusCode);
        Assert.Contains("providerStatus", ReadBody(bootstrap), StringComparison.Ordinal);
        var create = Invoke(namedEndpoints["ElsaAgentApiEndpointsCreateSession"], services, principal, HttpMethod.Post, "/_elsa/agent/sessions", "{}");
        Assert.Equal(StatusCodes.Status200OK, create.Response.StatusCode);
        Assert.Contains("sessionId", ReadBody(create), StringComparison.Ordinal);
        Assert.True(provider.DiagnosticsCalls > 0);
        Assert.Equal(1, provider.CreateSessionCalls);

        var completedStream = Invoke(namedEndpoints["ElsaAgentApiEndpointsStreamSession"], services, principal, HttpMethod.Get, "/_elsa/agent/sessions/session-1/stream", cancellationToken: CancellationToken.None, routeValues: new Dictionary<string, object?> { ["sessionId"] = "session-1" });
        Assert.Contains("data: ", ReadBody(completedStream), StringComparison.Ordinal);
        Assert.True(streaming.Completed.Task.Wait(TimeSpan.FromSeconds(5)));

        using var cancellation = new CancellationTokenSource();
        var cancelledStreamTask = Task.Run(() =>
        {
            var context = CreateContext(services, principal, HttpMethod.Get, "/_elsa/agent/sessions/session-1/stream", null, cancellation.Token, new Dictionary<string, object?> { ["sessionId"] = "session-1" });
            try
            {
                namedEndpoints["ElsaAgentApiEndpointsStreamSession"].RequestDelegate!(context).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
        });
        Assert.True(streaming.CancellationStarted.Task.Wait(TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
        Assert.True(cancelledStreamTask.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(streaming.Cancelled.Task.Wait(TimeSpan.FromSeconds(5)));

        var contextType = assembly.GetType("Elsa.Agent.Api.AgentJsonContext", true)!;
        var context = contextType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)!.GetValue(null) as JsonSerializerContext;
        Assert.NotNull(context);
        Assert.NotNull(contextType.GetProperty("AgentApiResponseAgentBootstrapResponse", BindingFlags.Public | BindingFlags.Instance)!.GetValue(context));

        authenticationContext.Response.Body.Dispose();

        var id = Guid.NewGuid();
        var references = new Dictionary<string, WeakReference>(StringComparer.Ordinal)
        {
            ["load-context"] = new(loadContext),
            ["assembly"] = new(assembly),
            ["mapper-type"] = new(mapperType),
            ["feature"] = new(feature),
            ["services"] = new(services),
            ["context"] = new(context),
            ["provider"] = new(provider),
            ["provider-registry"] = new(providers),
            ["streaming"] = new(streaming),
            ["authentication-context"] = new(authenticationContext)
        };
        foreach (var endpoint in endpoints.Select((value, index) => (value, index)))
            references[$"endpoint-{endpoint.index}"] = new(endpoint.value);

        routes.DataSources.Clear();
        services.Dispose();
        authenticationContext = null;
        loadContext.Unload();
        return new(id, references["load-context"], references["assembly"], references["mapper-type"], references);
    }

    private static DefaultHttpContext Invoke(
        RouteEndpoint endpoint,
        IServiceProvider services,
        ClaimsPrincipal principal,
        HttpMethod method,
        string path,
        string? body = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, object?>? routeValues = null)
    {
        var context = CreateContext(services, principal, method, path, body, cancellationToken, routeValues);
        endpoint.RequestDelegate!(context).GetAwaiter().GetResult();
        return context;
    }

    private static DefaultHttpContext CreateContext(
        IServiceProvider services,
        ClaimsPrincipal principal,
        HttpMethod method,
        string path,
        string? body,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? routeValues)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = principal,
            RequestAborted = cancellationToken
        };
        context.Request.Method = method.Method;
        context.Request.Path = path;
        context.Request.RouteValues = routeValues is null ? [] : new(routeValues);
        context.Response.Body = new MemoryStream();
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bytes.Length;
        }

        return context;
    }

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
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

    private sealed record Evidence(Guid Id, WeakReference LoadContext, WeakReference Assembly, WeakReference MapperType, IReadOnlyDictionary<string, WeakReference> References);

    private sealed class Wave4RouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class Wave4LoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Wave4CollectibilityProviderRegistry(IAgentProvider provider) : IAgentProviderRegistry
    {
        public IAgentProvider? Active { get; } = provider;
    }

    private sealed class Wave4CollectibilityProvider : IAgentProvider
    {
        public int DiagnosticsCalls { get; private set; }
        public int CreateSessionCalls { get; private set; }
        public string ProviderId => "collectibility-provider";

        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
        {
            CreateSessionCalls++;
            return Task.FromResult(new AgentProviderSession("provider-session", ProviderId, new Dictionary<string, string>()));
        }

        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentToolApprovalResult(true, "approved"));

        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
        {
            DiagnosticsCalls++;
            return Task.FromResult(new AgentProviderDiagnostics(ProviderId, true, "available", AgentProviderKind.ProviderSdkBinding, [AgentProviderOperation.Chat], AgentProviderRiskProfile.ReadOnly, new Dictionary<string, string>()));
        }
    }

    private sealed class Wave4CollectibilityStreamingService : IAgentStreamingService
    {
        private int callCount;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(string sessionId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref callCount);
            try
            {
                if (call == 2)
                    CancellationStarted.TrySetResult();
                yield return new AgentStreamEvent($"collectible-{call}", AgentStreamEventKind.Completed, null, null, null, Wave4AgentFixtures.Now);
                if (call == 2)
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                if (call == 1)
                    Completed.TrySetResult();
                if (call == 2)
                    Cancelled.TrySetResult();
            }
        }
    }
}
