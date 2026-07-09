using System.Text.Json;
using Elsa.Activities.Http;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Host-level integration fixture for spec 089 sub-unit A (HTTP endpoint parity, async/202 baseline). It
/// self-hosts a real ASP.NET Core request pipeline over <see cref="TestServer"/> and composes the real Elsa
/// runtime feature set — serialization, expressions, the workflow runtime API spine, the activity-construction
/// runtime, the primitives' <c>ClrActivityConstructor</c>, the triggers/stimulus-routing feature, and the HTTP
/// activities feature — over the default in-memory stores and the in-process workflow agent. Nothing is faked:
/// an inbound request flows through the production <c>HttpEndpointMiddleware</c> → real <c>IStimulusRouter</c> →
/// real <c>IWorkflowStartDispatcher</c> → in-process agent, which runs the workflow to completion synchronously,
/// so the durable value store reflects the run immediately after the 202.
/// </summary>
/// <remarks>
/// <para>
/// <b>Composition path.</b> Full CShells shell composition (<c>AddCShellsAspNetCore</c> + <c>MapShells</c> +
/// shell manifests) is too heavy for a focused TestServer fixture, so — as the task allows — the middleware
/// pipeline is configured directly the way the shell's <see cref="CShells.AspNetCore.Features.IMiddlewareShellFeature"/>
/// seam would: the feature service sets are registered, then <c>app.UseMiddleware&lt;HttpEndpointMiddleware&gt;()</c>
/// is mounted (exactly what <see cref="ActivitiesHttpFeature.UseMiddleware"/> does), followed by a terminal
/// sentinel proving pass-through. The <see cref="ActivitiesHttpFeatureMiddlewareSeamTests"/> unit test covers the
/// other half — that the feature really implements the seam and its <c>UseMiddleware</c> mounts the middleware —
/// so the two together prove the same wiring a real shell performs.
/// </para>
/// <para>
/// The runtime's startup tasks (activity-constructor registry, serialization converters, well-known type
/// registration) are run explicitly after the provider is built, mirroring what a real host does at startup;
/// the CLR construction descriptor depends on that type registration to resolve an activity's stable alias.
/// </para>
/// </remarks>
public sealed class HttpEndpointHostFixture : IAsyncDisposable
{
    /// <summary>The status code the out-of-base-path sentinel terminal middleware returns (proves pass-through).</summary>
    public const int SentinelStatusCode = StatusCodes.Status418ImATeapot;

    private readonly IHost _host;

    private HttpEndpointHostFixture(IHost host) => _host = host;

    public HttpClient Client => _host.GetTestClient();

    public IServiceProvider Services => _host.Services;

    public static async Task<HttpEndpointHostFixture> StartAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddMemoryCache();
                    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

                    // The real runtime feature set, composed the way a host composes it (see the runtime
                    // end-to-end acceptance tests). Order matters only for TryAdd defaults, not for these.
                    new EventsFeature().ConfigureServices(services);
                    new SerializationFeature().ConfigureServices(services);
                    new ExpressionsFeature().ConfigureServices(services);
                    new WorkflowsRuntimeApiFeature().ConfigureServices(services);
                    new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);
                    new ActivitiesRuntimeFeature().ConfigureServices(services);
                    new ActivitiesPrimitivesFeature().ConfigureServices(services);
                    new ActivitiesHttpFeature().ConfigureServices(services);

                    // The real route-table stack (spec 089 B). The REAL resolver, route-table startup task, and
                    // publish-time index observer from Elsa.Workflows.Runtime.Http are registered directly — the
                    // same services WorkflowsRuntimeHttpFeature contributes, but registered explicitly because that
                    // feature (like HttpFeature) resolves its defaults reflectively via Type.GetType(simpleName),
                    // which cannot resolve types outside this assembly/mscorlib. This keeps the fixture focused
                    // while leaving the publish→observer→resolver→route-table wiring under test fully real.
                    //
                    // The IRouteTable/IRouteMatcher IMPLEMENTATIONS are internal to Elsa.Http; production-equivalent
                    // doubles (see RouteTableTestDoubles.cs) stand in for just those two services.
                    services.AddSingleton<Elsa.Http.Core.Contracts.IRouteMatcher, TestRouteMatcher>();
                    services.AddSingleton<Elsa.Http.Core.Contracts.IRouteTable, MemoryCacheRouteTable>();
                    services.AddScoped<Elsa.Workflows.Runtime.Http.Contracts.IHttpEndpointRoutesResolver, Elsa.Workflows.Runtime.Http.Services.HttpEndpointRoutesResolver>();
                    services.AddScoped<IStartupTask, Elsa.Workflows.Runtime.Http.Tasks.UpdateRouteTableStartupTask>();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowTriggerIndexObserver, Elsa.Workflows.Runtime.Http.Services.RouteTableTriggerIndexObserver>());

                    // Spec 089 sub-unit C (T010). The endpoint-policy seam services the middleware resolves from
                    // RequestServices: the REAL authorization + fault handlers from Elsa.Workflows.Runtime.Http and
                    // the REAL request-body parser from Elsa.Http (again registered explicitly rather than via the
                    // reflective feature loader, which can't resolve out-of-assembly types).
                    services.AddSingleton<Elsa.Http.Core.Contracts.IHttpEndpointAuthorizationHandler,
                        Elsa.Workflows.Runtime.Http.Services.AuthenticationBasedHttpEndpointAuthorizationHandler>();
                    services.AddSingleton<Elsa.Http.Core.Contracts.IHttpEndpointFaultHandler,
                        Elsa.Workflows.Runtime.Http.Services.HttpEndpointFaultHandler>();
                    services.AddSingleton<Elsa.Http.Core.Contracts.IHttpRequestBodyParser,
                        Elsa.Http.Services.HttpRequestBodyParser>();

                    // A test authentication scheme honoring "Authorization: Test <name>" (the standard test-handler
                    // pattern), plus authorization services the auth handler evaluates policies against. The auth
                    // handler resolves the authenticated principal via this scheme.
                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                });
                webHost.Configure(app =>
                {
                    // Authentication runs before the endpoint middleware, mirroring the prod root-pipeline order
                    // (Program.cs runs UseAuthentication ahead of the shell branch), so HttpContext.User is
                    // populated by the time the endpoint's authorization handler evaluates the request.
                    app.UseAuthentication();

                    // Mount the inbound endpoint middleware exactly as ActivitiesHttpFeature.UseMiddleware does.
                    app.UseMiddleware<Elsa.Activities.Http.Middleware.HttpEndpointMiddleware>();

                    // Terminal sentinel: any request the endpoint middleware passed through (outside the base
                    // path) lands here, proving non-endpoint traffic flows on down the pipeline.
                    app.Run(context =>
                    {
                        context.Response.StatusCode = SentinelStatusCode;
                        return Task.CompletedTask;
                    });
                });
            })
            .Build();

        await host.StartAsync();

        RunStartupTasks(host.Services);

        return new HttpEndpointHostFixture(host);
    }

    /// <summary>
    /// Publishes a workflow whose single start-trigger node is an <see cref="HttpEndpoint"/> on
    /// <paramref name="path"/> accepting <paramref name="methods"/> (routing-significant — spec 089 B), indexing
    /// its trigger binding the way the publish flow does. The node captures the endpoint's <c>Result</c> output
    /// into the durable value store under the durable value id <paramref name="resultValueId"/> so the live
    /// request the run observed (including its extracted RouteData) can be read back.
    /// </summary>
    public async Task PublishHttpEndpointWorkflowAsync(string artifactId, string path, string resultValueId, params string[] methods) =>
        await PublishHttpEndpointWorkflowAsync(artifactId, path, resultValueId, methods, authorize: null, policy: null, requestSizeLimit: null);

    /// <summary>
    /// As <see cref="PublishHttpEndpointWorkflowAsync(string,string,string,string[])"/>, additionally authoring the
    /// spec 089 C endpoint-option literals — <see cref="HttpEndpoint.Authorize"/>, <see cref="HttpEndpoint.Policy"/>,
    /// <see cref="HttpEndpoint.RequestSizeLimit"/> — so their values ride the trigger-binding metadata the middleware
    /// enforces. Null inputs are omitted (the option defaults apply).
    /// </summary>
    public async Task PublishHttpEndpointWorkflowAsync(
        string artifactId,
        string path,
        string resultValueId,
        string[] methods,
        bool? authorize,
        string? policy,
        long? requestSizeLimit)
    {
        var executable = NewHttpEndpointExecutable(artifactId, path, resultValueId, methods, authorize, policy, requestSizeLimit);

        // Store the executable (start dispatch resolves it by artifact id) and index its trigger binding so the
        // stimulus router can match an inbound request to it — the two things the publish flow does. IndexAsync
        // also fires the route-table index observer, so the published template lands in the live route table.
        await Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        await Services.GetRequiredService<IWorkflowTriggerIndexer>().IndexAsync(executable);
    }

    /// <summary>The number of workflow executions the runtime has persisted — 0 proves nothing started (401/413 paths).</summary>
    public async Task<int> CountWorkflowExecutionsAsync() =>
        (await Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync()).Count;

    /// <summary>Reads the single durable value captured under the durable value id <paramref name="valueId"/> for a run.</summary>
    public async Task<JsonElement> ReadCapturedOutputAsync(string workflowExecutionId, string valueId)
    {
        var durableValues = await Services.GetRequiredService<IDurableValueStateStore>().ListAsync(workflowExecutionId);
        var captured = Assert.Single(durableValues, value => value.ValueId == valueId);
        Assert.NotNull(captured.InlineValue);
        return captured.InlineValue!.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private WorkflowExecutable NewHttpEndpointExecutable(
        string artifactId,
        string path,
        string resultOutputName,
        string[] methods,
        bool? authorize = null,
        string? policy = null,
        long? requestSizeLimit = null)
    {
        var serializer = Services.GetRequiredService<IPayloadSerializer>();

        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), path, "System.String")
        };

        // SupportedMethods is routing-significant (spec 089 B). Authored as a literal string array; unauthored
        // would default to GET, so a POST endpoint must author ["POST"] or the request 404s under the GET default.
        if (methods.Length > 0)
            inputBindings[nameof(HttpEndpoint.SupportedMethods)] =
                LiteralBinding(nameof(HttpEndpoint.SupportedMethods), methods, "System.Collections.Generic.ICollection`1[[System.String]]");

        // Spec 089 C endpoint-option literals (mirrors how SupportedMethods is authored). The trigger provider
        // reads these at publish time and stamps them on the binding metadata the middleware enforces.
        if (authorize is { } authorizeValue)
            inputBindings[nameof(HttpEndpoint.Authorize)] =
                LiteralBinding(nameof(HttpEndpoint.Authorize), authorizeValue, "System.Boolean");
        if (policy is not null)
            inputBindings[nameof(HttpEndpoint.Policy)] =
                LiteralBinding(nameof(HttpEndpoint.Policy), policy, "System.String");
        if (requestSizeLimit is { } sizeLimit)
            inputBindings[nameof(HttpEndpoint.RequestSizeLimit)] =
                LiteralBinding(nameof(HttpEndpoint.RequestSizeLimit), sizeLimit, "System.Int64");

        var node = new ExecutableNode(
            executableNodeId: "node-http-endpoint",
            authoredActivityId: "authored-node-http-endpoint",
            activityType: HttpEndpoint.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(serializer, typeof(HttpEndpoint)),
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>
            {
                // Promote the endpoint's Result output (the live HttpRequestModel) into a durable, readable value.
                // The capture dictionary is keyed by the activity's output name; the durable value id carries the
                // caller-chosen id used to read the capture back.
                [nameof(HttpEndpoint.Result)] = new RuntimeOutputCapture(
                    outputName: nameof(HttpEndpoint.Result),
                    valueId: resultOutputName,
                    type: new RuntimeValueTypeDescriptor("clr", typeof(object).FullName, null),
                    lifecycle: DurableValueLifecycle.Instance,
                    storage: DurableValueStorage.Inline,
                    captureOnSuccessfulCompletion: true)
            },
            // Mark this node a start-trigger so the trigger extractor indexes it (E3-1).
            metadata: new Dictionary<string, string>
            {
                [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType
            });

        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(artifactId, $"definition-{artifactId}", $"version-{artifactId}", "1.0.0", $"sha256:{artifactId}"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static RuntimeInputBinding LiteralBinding(string inputName, object value, string typeName) =>
        new(
            inputName: inputName,
            source: RuntimeInputBindingSource.Literal,
            literalValue: JsonSerializer.SerializeToElement(value),
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = typeName
            });

    private static void RunStartupTasks(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
            task.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}

/// <summary>
/// A minimal test authentication scheme (the standard ASP.NET test-handler pattern) honoring an
/// <c>Authorization: Test &lt;name&gt;</c> header: any such header authenticates a caller named <c>&lt;name&gt;</c>;
/// its absence yields no result (anonymous). It stands in for the shell's authentication stack so the real
/// <c>AuthenticationBasedHttpEndpointAuthorizationHandler</c> can resolve a principal (spec 089 C, T010).
/// </summary>
public sealed class TestAuthHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
    Microsoft.Extensions.Logging.ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header))
            return Task.FromResult(AuthenticateResult.NoResult());

        var value = header.ToString();
        const string prefix = "Test ";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length <= prefix.Length)
            return Task.FromResult(AuthenticateResult.Fail("Malformed test authorization header."));

        var name = value[prefix.Length..].Trim();
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, name)],
            SchemeName);
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
