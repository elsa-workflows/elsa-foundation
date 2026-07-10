using System.Text.Json;
using Elsa.Activities.Http;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Http.Core;
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

using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

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

    /// <summary>The durable value id the published workflow captures the <see cref="HttpEndpoint.ParsedContent"/> output under.</summary>
    public const string ParsedContentOutputName = "EndpointParsedContent";

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
                    // The Sequence composite (spec 089 D scenario 2 sequences two endpoints in one workflow). Its
                    // feature only registers the structure handler; construction goes through the primitives'
                    // ClrActivityConstructor, so the primitives feature must precede it (already does).
                    new ActivitiesSequenceFeature().ConfigureServices(services);
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
                    services.AddSingleton<Elsa.Http.Core.Contracts.IRouteTable, FakeRouteTable>();
                    services.AddScoped<Elsa.Workflows.Runtime.Http.Contracts.IHttpEndpointRoutesResolver, Elsa.Workflows.Runtime.Http.Services.HttpEndpointRoutesResolver>();
                    // The single serialization point every route-table refresh (startup task + both observers)
                    // delegates to (spec 089 D review fix) — the same singleton WorkflowsRuntimeHttpFeature contributes.
                    services.TryAddSingleton<Elsa.Workflows.Runtime.Http.Contracts.IHttpEndpointRouteTableSynchronizer, Elsa.Workflows.Runtime.Http.Services.HttpEndpointRouteTableSynchronizer>();
                    services.AddScoped<IStartupTask, Elsa.Workflows.Runtime.Http.Tasks.UpdateRouteTableStartupTask>();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowTriggerIndexObserver, Elsa.Workflows.Runtime.Http.Services.RouteTableTriggerIndexObserver>());

                    // Publish-time (template, method) uniqueness on the indexer's PRE-write seam (issue #592
                    // item 2) — the same validator WorkflowsRuntimeHttpFeature contributes.
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowTriggerIndexValidator, Elsa.Workflows.Runtime.Http.Services.HttpEndpointRoutingUniquenessValidator>());

                    // Spec 089 sub-unit D (T010). The bookmark-lifecycle observer that keeps the route table in step
                    // with waiting mid-flow bookmarks — registered exactly like the publish-side trigger observer
                    // above (the reflective feature loader can't resolve this out-of-assembly type). The
                    // BookmarkLifecycleNotifier that fans into it is already registered by WorkflowsRuntimeApiFeature.
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IBookmarkLifecycleObserver, Elsa.Workflows.Runtime.Http.Services.RouteTableBookmarkObserver>());

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

    /// <summary>
    /// Publishes a workflow whose single node is a <b>mid-flow</b> <see cref="HttpEndpoint"/> on
    /// <paramref name="path"/>/<paramref name="method"/> with <see cref="HttpEndpoint.CanStartWorkflow"/> = false
    /// (spec 089 D). Because the node can never start a workflow it is NOT a trigger node and is not indexed — the
    /// run is started directly via <see cref="StartWorkflowDirectlyAsync"/>, the endpoint suspends on a bookmark,
    /// and a matching request resumes it. The node captures <c>Result</c> into the durable value store under
    /// <paramref name="resultValueId"/> so the resumed run's observed request can be read back.
    /// </summary>
    public async Task PublishResumableHttpEndpointWorkflowAsync(string artifactId, string path, string method, string resultValueId)
    {
        var node = NewHttpEndpointNode(artifactId, path, resultValueId, [method], authorize: null, policy: null, requestSizeLimit: null,
            canStartWorkflow: false, isTriggerNode: false);

        var executable = new WorkflowExecutable(
            identity: NewIdentity(artifactId),
            rootActivity: node,
            resumeTargets: NewHttpEndpointResumeTargets(node.ExecutableNodeId),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

        // Only stored (never indexed): a CanStartWorkflow = false node produces no trigger bindings, so a direct
        // start is the only way in — exactly the spec 089 D US4 independent test's shape.
        await Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
    }

    /// <summary>
    /// Publishes a workflow that sequences two <see cref="HttpEndpoint"/>s (spec 089 D scenario 2): a start-trigger
    /// endpoint on <paramref name="startPath"/> (indexed, starts the run) followed by a mid-flow endpoint on
    /// <paramref name="callbackPath"/> (<see cref="HttpEndpoint.CanStartWorkflow"/> = false, suspends). An HTTP
    /// request to the start template runs the first endpoint and suspends on the second; a request to the callback
    /// template resumes it. Both endpoints capture their <c>Result</c> under distinct durable value ids so both
    /// observed requests can be read back.
    /// </summary>
    public async Task PublishSequencedHttpEndpointsWorkflowAsync(
        string artifactId,
        string startPath,
        string startResultValueId,
        string callbackPath,
        string callbackResultValueId,
        string method)
    {
        var serializer = Services.GetRequiredService<IPayloadSerializer>();

        // The start endpoint is the trigger node (indexed as a start binding); the callback endpoint is mid-flow
        // (CanStartWorkflow = false, not a trigger — it suspends). The extractor flattens the whole tree and keys
        // off each node's own trigger marker, so the marker rides the start CHILD, never the Sequence root.
        var startNode = NewHttpEndpointNode($"{artifactId}-start", startPath, startResultValueId, [method],
            authorize: null, policy: null, requestSizeLimit: null, canStartWorkflow: null, isTriggerNode: true,
            nodeId: "node-start-endpoint");
        var callbackNode = NewHttpEndpointNode($"{artifactId}-callback", callbackPath, callbackResultValueId, [method],
            authorize: null, policy: null, requestSizeLimit: null, canStartWorkflow: false, isTriggerNode: false,
            nodeId: "node-callback-endpoint");

        // A real Sequence container: child slot + ordered structure (mirrors SequenceRuntimeTests). The start
        // endpoint runs first (its node id becomes the run's trigger identity → start path completes), then the
        // Sequence schedules the callback endpoint, which is mid-flow (foreign trigger identity → suspends). The
        // Sequence root itself is not a trigger — only its start child is.
        var sequenceNode = new ExecutableNode(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(serializer, typeof(SequenceActivity)),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [startNode, callbackNode])],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = new[] { startNode.ExecutableNodeId, callbackNode.ExecutableNodeId } })));

        var resumeTargets = NewHttpEndpointResumeTargets(callbackNode.ExecutableNodeId);

        var executable = new WorkflowExecutable(
            identity: NewIdentity(artifactId),
            rootActivity: sequenceNode,
            resumeTargets: resumeTargets,
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

        await Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        // Index so the START endpoint's (template, method) trigger binding lands in the route table.
        await Services.GetRequiredService<IWorkflowTriggerIndexer>().IndexAsync(executable);
    }

    // ---- Spec 089 sub-unit E (synchronous responses) publish helpers ----

    /// <summary>
    /// Publishes a synchronous (spec 089 E) endpoint workflow: a start-trigger <see cref="HttpEndpoint"/> on
    /// <paramref name="path"/>/<paramref name="method"/> authored <see cref="ResponseMode.Sync"/>, sequenced before a
    /// <see cref="WriteHttpResponse"/> authoring <paramref name="statusCode"/>/<paramref name="body"/>/
    /// <paramref name="contentType"/> (and one custom header). An HTTP request runs the endpoint then the
    /// WriteHttpResponse INLINE on the caller's flow, so the middleware returns the workflow-authored response in the
    /// same exchange (scenario 5.1). Pass <paramref name="responseMode"/> = <see cref="ResponseMode.Async"/> for the
    /// async baseline variant (scenario 5.4): the same graph, but the middleware never populates the sink, so the
    /// response is the 202 baseline and the WriteHttpResponse only records the durable artifact.
    /// </summary>
    public Task PublishSyncEndpointWithWriteResponseWorkflowAsync(
        string artifactId,
        string path,
        string method,
        string resultValueId,
        int statusCode,
        string body,
        string contentType,
        (string Name, string Value)? header = null,
        ResponseMode responseMode = ResponseMode.Sync)
    {
        var endpoint = NewHttpEndpointNode($"{artifactId}-endpoint", path, resultValueId, [method],
            authorize: null, policy: null, requestSizeLimit: null, canStartWorkflow: null, isTriggerNode: true,
            nodeId: "node-sync-endpoint", responseMode: responseMode);
        var write = NewWriteHttpResponseNode("node-write-response", statusCode, body, contentType, header);
        return PublishTriggerSequenceAsync(artifactId, endpoint, write);
    }

    /// <summary>
    /// Publishes a synchronous (spec 089 E) endpoint that suspends BEFORE any WriteHttpResponse (scenario 5.2): a
    /// start-trigger <see cref="HttpEndpoint"/> (Sync) sequenced before a mid-flow <see cref="HttpEndpoint"/>
    /// (<see cref="HttpEndpoint.CanStartWorkflow"/> = false — it suspends) and then a <see cref="WriteHttpResponse"/>
    /// that is never reached in the starting exchange. The starting request therefore writes no live response and the
    /// middleware degrades to <c>202</c> with the started execution id.
    /// </summary>
    public Task PublishSyncSuspendBeforeResponseWorkflowAsync(
        string artifactId,
        string startPath,
        string suspendPath,
        string method,
        string startResultValueId)
    {
        var start = NewHttpEndpointNode($"{artifactId}-start", startPath, startResultValueId, [method],
            authorize: null, policy: null, requestSizeLimit: null, canStartWorkflow: null, isTriggerNode: true,
            nodeId: "node-sync-start", responseMode: ResponseMode.Sync);
        var suspend = NewHttpEndpointNode($"{artifactId}-suspend", suspendPath, $"{startResultValueId}-suspend", [method],
            authorize: null, policy: null, requestSizeLimit: null, canStartWorkflow: false, isTriggerNode: false,
            nodeId: "node-sync-suspend");
        var write = NewWriteHttpResponseNode("node-write-after-suspend", 200, "unreachable", "text/plain", header: null);

        return PublishTriggerSequenceAsync(artifactId, start, suspend, write,
            resumeTargetNodeId: suspend.ExecutableNodeId);
    }

    /// <summary>
    /// Publishes a synchronous (spec 089 E) endpoint with a short per-endpoint <paramref name="requestTimeout"/> and a
    /// <see cref="StallingActivity"/> that outlasts it (scenario 5.3): the inline drain stalls on the timeout token,
    /// the stall throws <see cref="OperationCanceledException"/>, and the middleware maps it to <c>408</c>. A
    /// <see cref="WriteHttpResponse"/> follows the stall but is never reached.
    /// </summary>
    public Task PublishSyncStallThenResponseWorkflowAsync(
        string artifactId,
        string path,
        string method,
        string resultValueId,
        TimeSpan requestTimeout,
        TimeSpan stallDuration)
    {
        var endpoint = NewHttpEndpointNode($"{artifactId}-endpoint", path, resultValueId, [method],
            authorize: null, policy: null, requestSizeLimit: null, canStartWorkflow: null, isTriggerNode: true,
            nodeId: "node-sync-stall-endpoint", responseMode: ResponseMode.Sync, requestTimeout: requestTimeout);
        var stall = NewStallingNode("node-stall", stallDuration);
        var write = NewWriteHttpResponseNode("node-write-after-stall", 200, "unreachable", "text/plain", header: null);
        return PublishTriggerSequenceAsync(artifactId, endpoint, stall, write);
    }

    /// <summary>
    /// Publishes a MID-FLOW synchronous (spec 089 E, scenario 5.5) endpoint workflow: a mid-flow
    /// <see cref="HttpEndpoint"/> (<see cref="HttpEndpoint.CanStartWorkflow"/> = false, authored
    /// <see cref="ResponseMode.Sync"/>) sequenced before a <see cref="WriteHttpResponse"/>. The run is started
    /// directly and suspends at the endpoint; the RESUMING request drains the WriteHttpResponse inline on its own
    /// fresh scope, so that request receives the workflow-authored response in the same exchange — the whole point of
    /// the D+E composition. Because the node cannot start a workflow it is not a trigger and is not indexed; the run
    /// is started via <see cref="StartWorkflowDirectlyAsync"/> and its bookmark route goes live for the resume.
    /// </summary>
    public async Task PublishMidFlowSyncWriteResponseWorkflowAsync(
        string artifactId,
        string path,
        string method,
        string resultValueId,
        int statusCode,
        string body,
        string contentType)
    {
        var endpoint = NewHttpEndpointNode($"{artifactId}-endpoint", path, resultValueId, [method],
            authorize: null, policy: null, requestSizeLimit: null, canStartWorkflow: false, isTriggerNode: false,
            nodeId: "node-midflow-sync-endpoint", responseMode: ResponseMode.Sync);
        var write = NewWriteHttpResponseNode("node-write-response", statusCode, body, contentType, header: null);
        var sequence = NewSequenceNode("node-sequence", endpoint, write);

        var executable = new WorkflowExecutable(
            identity: NewIdentity(artifactId),
            rootActivity: sequence,
            resumeTargets: NewHttpEndpointResumeTargets(endpoint.ExecutableNodeId),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

        // Only stored (never indexed): a CanStartWorkflow = false node produces no trigger bindings, so a direct
        // start is the only way in — the mid-flow route goes live off the bookmark-lifecycle notification.
        await Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
    }

    /// <summary>
    /// Composes <paramref name="children"/> under a real <see cref="SequenceActivity"/> whose first child is the
    /// start trigger, stores and indexes the executable so the start endpoint's (template, method) binding lands in
    /// the route table. <paramref name="resumeTargetNodeId"/> (when set) declares the HttpEndpoint resume target for a
    /// mid-flow child so a matching request can resume the suspended run.
    /// </summary>
    private async Task PublishTriggerSequenceAsync(
        string artifactId,
        ExecutableNode first,
        ExecutableNode second,
        ExecutableNode? third = null,
        string? resumeTargetNodeId = null)
    {
        var children = third is null ? new[] { first, second } : [first, second, third];
        var sequence = NewSequenceNode("node-sequence", children);

        var executable = new WorkflowExecutable(
            identity: NewIdentity(artifactId),
            rootActivity: sequence,
            resumeTargets: resumeTargetNodeId is null
                ? new Dictionary<string, WorkflowExecutableResumeTarget>()
                : NewHttpEndpointResumeTargets(resumeTargetNodeId),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

        await Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        // Index so the START endpoint's (template, method) trigger binding lands in the route table.
        await Services.GetRequiredService<IWorkflowTriggerIndexer>().IndexAsync(executable);
    }

    /// <summary>Builds a real <see cref="SequenceActivity"/> container node (child slot + ordered structure) over <paramref name="children"/>.</summary>
    private ExecutableNode NewSequenceNode(string nodeId, params ExecutableNode[] children)
    {
        var serializer = Services.GetRequiredService<IPayloadSerializer>();

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(serializer, typeof(SequenceActivity)),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = children.Select(child => child.ExecutableNodeId).ToArray() })));
    }

    /// <summary>Builds a <see cref="WriteHttpResponse"/> node authoring the given status/body/content-type and an optional header.</summary>
    private ExecutableNode NewWriteHttpResponseNode(string nodeId, int statusCode, string body, string contentType, (string Name, string Value)? header)
    {
        var serializer = Services.GetRequiredService<IPayloadSerializer>();

        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(WriteHttpResponse.StatusCode)] = LiteralBinding(nameof(WriteHttpResponse.StatusCode), statusCode, "System.Int32"),
            [nameof(WriteHttpResponse.Body)] = LiteralBinding(nameof(WriteHttpResponse.Body), body, "System.String"),
            [nameof(WriteHttpResponse.ContentType)] = LiteralBinding(nameof(WriteHttpResponse.ContentType), contentType, "System.String")
        };

        if (header is { } h)
            inputBindings[nameof(WriteHttpResponse.Headers)] = LiteralBinding(
                nameof(WriteHttpResponse.Headers),
                new Dictionary<string, string[]> { [h.Name] = [h.Value] },
                "System.Collections.Generic.IDictionary`2[[System.String],[System.String[]]]");

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(WriteHttpResponse).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(serializer, typeof(WriteHttpResponse)),
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
    }

    /// <summary>Builds a <see cref="StallingActivity"/> node that stalls the inline drain for <paramref name="duration"/> (scenario 5.3 timeout).</summary>
    private ExecutableNode NewStallingNode(string nodeId, TimeSpan duration)
    {
        var serializer = Services.GetRequiredService<IPayloadSerializer>();

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(StallingActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(serializer, typeof(StallingActivity)),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(StallingActivity.Duration)] = LiteralBinding(nameof(StallingActivity.Duration), duration.ToString("c"), "System.TimeSpan")
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
    }

    /// <summary>
    /// Reads the single durable <see cref="HttpResponseInstruction"/> artifact <see cref="WriteHttpResponse"/> records
    /// under the well-known workflow-output name (keyed on the runtime output-name metadata, mirroring the activity's
    /// own execution tests) — the durable proof the artifact was recorded regardless of sync/async delivery.
    /// </summary>
    public async Task<JsonElement> ReadHttpResponseArtifactAsync(string workflowExecutionId)
    {
        var durableValues = await Services.GetRequiredService<IDurableValueStateStore>().ListAsync(workflowExecutionId);
        var artifact = Assert.Single(
            durableValues,
            value => value.Metadata.TryGetValue(RuntimeMetadataKeys.OutputName, out var name)
                && name == HttpResponseInstruction.OutputName);
        Assert.NotNull(artifact.InlineValue);
        return artifact.InlineValue!.Value;
    }

    /// <summary>
    /// Starts a run of a previously-published artifact directly through the runtime API — no HTTP stimulus, no
    /// trigger identity (spec 089 D US4 independent test). The in-process agent drains synchronously, so a mid-flow
    /// endpoint has already suspended (and its route is live) by the time this returns. Returns the started
    /// workflow execution id.
    /// </summary>
    public async Task<string> StartWorkflowDirectlyAsync(string artifactId)
    {
        var result = await Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(artifactId, requestedBy: "integration-test"));
        return result.WorkflowExecutionId;
    }

    /// <summary>Reads all waiting bookmarks for a run (spec 089 D — inspect the mid-flow suspension).</summary>
    public async Task<IReadOnlyCollection<BookmarkState>> ListBookmarksAsync(string workflowExecutionId) =>
        await Services.GetRequiredService<IBookmarkStateStore>().ListAsync(workflowExecutionId);

    /// <summary>
    /// Upserts a bookmark state (spec 089 D scenario 4.3 — overwrite a waiting bookmark with a past
    /// <c>ExpiresAt</c> so the expiry-aware lookup excludes it), then refreshes the route table so the resolver's
    /// union no longer projects the now-expired template.
    /// </summary>
    public async Task ExpireBookmarkAsync(BookmarkState bookmark)
    {
        await Services.GetRequiredService<IBookmarkStateStore>().SaveAsync(bookmark with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

        // Re-project the route table off the durable sources (the store overwrite fired no lifecycle notification).
        using var scope = Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<Elsa.Workflows.Runtime.Http.Contracts.IHttpEndpointRoutesResolver>();
        var routeTable = scope.ServiceProvider.GetRequiredService<Elsa.Http.Core.Contracts.IRouteTable>();
        await routeTable.Refresh(await resolver.ResolveRoutesAsync(CancellationToken.None));
    }

    /// <summary>True when the live route table holds <paramref name="template"/> (endpoint-relative).</summary>
    public bool RouteTableContains(string template)
    {
        var routeTable = Services.GetRequiredService<Elsa.Http.Core.Contracts.IRouteTable>();
        return routeTable.Any(route => StringComparer.Ordinal.Equals(route.Route?.Trim('/'), template.Trim('/')));
    }

    /// <summary>The number of workflow executions the runtime has persisted — 0 proves nothing started (401/413 paths).</summary>
    public async Task<int> CountWorkflowExecutionsAsync() =>
        (await Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync()).Count;

    /// <summary>The single persisted workflow execution's state — asserts exactly one run exists.</summary>
    public async Task<WorkflowExecutionState> SingleWorkflowExecutionAsync() =>
        Assert.Single(await Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync());

    /// <summary>The artifact's trigger bindings in the durable index — empty proves a failed publish wrote nothing.</summary>
    public async Task<IReadOnlyCollection<WorkflowTriggerBinding>> ListTriggerBindingsAsync(string artifactId) =>
        await Services.GetRequiredService<IWorkflowTriggerBindingStore>().ListByArtifactAsync(artifactId);

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
        var node = NewHttpEndpointNode(artifactId, path, resultOutputName, methods, authorize, policy, requestSizeLimit,
            canStartWorkflow: null, isTriggerNode: true);

        return new WorkflowExecutable(
            identity: NewIdentity(artifactId),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    /// <summary>
    /// Builds one HttpEndpoint executable node. <paramref name="isTriggerNode"/> stamps the trigger execution-type
    /// metadata (so a standalone start-trigger endpoint is indexed as a start node); a mid-flow endpoint inside a
    /// composite leaves it off (the composite root carries the trigger marker instead).
    /// <paramref name="canStartWorkflow"/> authors the D-D1 literal (null = leave default true).
    /// </summary>
    private ExecutableNode NewHttpEndpointNode(
        string artifactId,
        string path,
        string resultOutputName,
        string[] methods,
        bool? authorize,
        string? policy,
        long? requestSizeLimit,
        bool? canStartWorkflow,
        bool isTriggerNode,
        string? nodeId = null,
        ResponseMode? responseMode = null,
        TimeSpan? requestTimeout = null)
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

        // D-D1: author the CanStartWorkflow literal when supplied (false = the node never starts a workflow, is not
        // indexed, and always suspends).
        if (canStartWorkflow is { } canStart)
            inputBindings[nameof(HttpEndpoint.CanStartWorkflow)] =
                LiteralBinding(nameof(HttpEndpoint.CanStartWorkflow), canStart, "System.Boolean");

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

        // Spec 089 E: the per-endpoint RequestTimeout literal (scenario 5.3 arms a short timeout so a stalling run
        // trips 408). Authored as an ISO-8601 duration string, exactly the shape the trigger provider decodes.
        if (requestTimeout is { } timeout)
            inputBindings[nameof(HttpEndpoint.RequestTimeout)] =
                LiteralBinding(nameof(HttpEndpoint.RequestTimeout), timeout.ToString("c"), "System.TimeSpan");

        // Spec 089 E (E-D1): the ResponseMode literal. Authored as the underlying numeric value with the enum's CLR
        // type name so it materializes at execution time (the activity's InputArgument<ResponseMode>) AND is decoded
        // by the trigger provider's ReadLiteralEnum (which accepts a defined numeric) at publish/index time — one
        // authoring shape serving both the start-trigger binding metadata and the mid-flow bookmark metadata.
        if (responseMode is { } mode)
            inputBindings[nameof(HttpEndpoint.ResponseMode)] =
                LiteralBinding(nameof(HttpEndpoint.ResponseMode), (int)mode, typeof(ResponseMode).FullName!);

        var metadata = isTriggerNode
            ? new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType }
            : new Dictionary<string, string>();

        return new ExecutableNode(
            executableNodeId: nodeId ?? "node-http-endpoint",
            authoredActivityId: $"authored-{nodeId ?? "node-http-endpoint"}",
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
                    captureOnSuccessfulCompletion: true),
                // Also promote the ParsedContent output — since spec 089 efficiency #9 the parsed body is derived at
                // the activity from Body (not persisted on the wire model), so tests read it from this output.
                [nameof(HttpEndpoint.ParsedContent)] = new RuntimeOutputCapture(
                    outputName: nameof(HttpEndpoint.ParsedContent),
                    valueId: ParsedContentOutputName,
                    type: new RuntimeValueTypeDescriptor("clr", typeof(object).FullName, null),
                    lifecycle: DurableValueLifecycle.Instance,
                    storage: DurableValueStorage.Inline,
                    captureOnSuccessfulCompletion: true)
            },
            metadata: metadata);
    }

    /// <summary>The stable resume-target map the publish compiler emits for an HttpEndpoint node (mirrors Delay).</summary>
    private static Dictionary<string, WorkflowExecutableResumeTarget> NewHttpEndpointResumeTargets(string nodeId) =>
        new(StringComparer.Ordinal)
        {
            [HttpEndpoint.ResumeTargetId] = new(
                ResumeTargetId: HttpEndpoint.ResumeTargetId,
                ExecutableNodeId: nodeId,
                HandlerKey: "OnRequestReceived",
                Metadata: new Dictionary<string, string>())
        };

    private static WorkflowExecutableIdentity NewIdentity(string artifactId) =>
        new(artifactId, $"definition-{artifactId}", $"version-{artifactId}", "1.0.0", $"sha256:{artifactId}");

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
