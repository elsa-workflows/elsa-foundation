using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Middleware;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Testing;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Http.IntegrationTests;

public sealed class HttpTestRunIngressTests
{
    [Fact]
    public async Task RealHttpRequest_DispatchesOnlyThePreparedTargetWithBoundedRequestModel()
    {
        var fixture = await IngressFixture.StartAsync();
        await using (fixture)
        {
            var response = await fixture.Client.SendAsync(Request(HttpMethod.Post, "/workflows/test-runs/run-1/http/orders/42?tag=one&tag=two", "hello"));

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(1, fixture.Dispatcher.CallCount);
            Assert.Equal("run-1", fixture.Dispatcher.TestRunId);
            var request = JsonSerializer.Deserialize<HttpRequestModel>(fixture.Dispatcher.Stimulus!.Value)!;
            Assert.Equal("orders/42", request.Path);
            Assert.Equal("POST", request.Method);
            Assert.Equal("hello", request.Body);
            Assert.Equal(new[] { "one", "two" }, request.Query["tag"]);
            Assert.Equal(new[] { "first", "second" }, request.Headers["X-Trace"]);
            Assert.DoesNotContain("Authorization", request.Headers.Keys);
            Assert.Equal("42", request.RouteData["id"]);
            Assert.NotNull(fixture.Dispatcher.AmbientServices);
        }
    }

    [Fact]
    public async Task RealHttpRequest_UnderDeploymentPathBase_ReachesTheIsolatedIngress()
    {
        var fixture = await IngressFixture.StartAsync(pathBase: "/tenant-host");
        await using (fixture)
        {
            var response = await fixture.Client.SendAsync(Request(
                HttpMethod.Post,
                "/tenant-host/workflows/test-runs/run-1/http/orders/42"));

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(1, fixture.Dispatcher.CallCount);
            Assert.Equal("run-1", fixture.Dispatcher.TestRunId);
            var request = JsonSerializer.Deserialize<HttpRequestModel>(fixture.Dispatcher.Stimulus!.Value)!;
            Assert.Equal("orders/42", request.Path);
            Assert.Equal("42", request.RouteData["id"]);
        }
    }

    [Fact]
    public async Task RealHttpRequest_RunsAuthenticationRequestHandlersBeforeIngressFallback()
    {
        var fixture = await IngressFixture.StartAsync(useRequestHandlerAuthentication: true);
        await using (fixture)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/workflows/test-runs/run-1/http/orders/42");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "valid");

            var response = await fixture.Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(1, fixture.Dispatcher.CallCount);
            Assert.Contains(fixture.PermissionAuthorization.LastContext!.Principal.Identities,
                identity => identity.IsAuthenticated && identity.Name == "bearer-user");
        }
    }

    [Fact]
    public async Task MissingAuthentication_AndManagementDenial_DoNotRevealOrDispatchTarget()
    {
        var anonymousFixture = await IngressFixture.StartAsync();
        await using (anonymousFixture)
        {
            var anonymous = await anonymousFixture.Client.GetAsync("/workflows/test-runs/run-1/http/orders/42");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Equal(0, anonymousFixture.TargetStore.FindCount);
            Assert.Equal(0, anonymousFixture.Dispatcher.CallCount);
        }

        var deniedFixture = await IngressFixture.StartAsync(allowManagement: false);
        await using (deniedFixture)
        {
            var denied = await deniedFixture.Client.SendAsync(Request(HttpMethod.Get, "/workflows/test-runs/run-1/http/orders/42"));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(0, deniedFixture.TargetStore.FindCount);
            Assert.Equal(0, deniedFixture.Dispatcher.CallCount);
            Assert.Equal("workflows/publishing:manage", deniedFixture.PermissionAuthorization.LastContext!.Permission);
        }
    }

    [Fact]
    public async Task AuthoredEndpointPolicyDenial_DoesNotDispatch()
    {
        var fixture = await IngressFixture.StartAsync(allowEndpoint: false, authorizeEndpoint: true);
        await using (fixture)
        {
            var response = await fixture.Client.SendAsync(Request(HttpMethod.Post, "/workflows/test-runs/run-1/http/orders/42"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(1, fixture.EndpointAuthorization.CallCount);
            Assert.Equal("orders-admin", fixture.EndpointAuthorization.LastPolicy);
            Assert.Equal(0, fixture.Dispatcher.CallCount);
        }
    }

    [Fact]
    public async Task OversizeBodyAndWrongMethod_AreRejectedBeforeDispatch()
    {
        var fixture = await IngressFixture.StartAsync();
        await using (fixture)
        {
            var oversize = await fixture.Client.SendAsync(Request(HttpMethod.Post, "/workflows/test-runs/run-1/http/orders/42", "123456"));
            Assert.Equal((HttpStatusCode)413, oversize.StatusCode);

            var wrongMethod = await fixture.Client.SendAsync(Request(HttpMethod.Delete, "/workflows/test-runs/run-1/http/orders/42"));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
            Assert.Equal(0, fixture.Dispatcher.CallCount);
        }
    }

    [Fact]
    public async Task UnknownOrClosedTestTarget_IsIsolatedFromProductionHttpIngress()
    {
        var fixture = await IngressFixture.StartAsync();
        await using (fixture)
        {
            var unknown = await fixture.Client.SendAsync(Request(HttpMethod.Get, "/workflows/test-runs/other-run/http/orders/42"));
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal(0, fixture.Dispatcher.CallCount);

            // The test ingress owns a separate namespace; production endpoints continue through their own middleware.
            var production = await fixture.Client.GetAsync("/workflows/http/orders/42");
            Assert.Equal((HttpStatusCode)418, production.StatusCode);
            Assert.Equal(0, fixture.Dispatcher.CallCount);
        }
    }

    [Fact]
    public async Task ExpiredOrStoppedTarget_IsRejectedBeforeDispatch()
    {
        var expiredFixture = await IngressFixture.StartAsync(expired: true);
        await using (expiredFixture)
        {
            var expired = await expiredFixture.Client.SendAsync(Request(HttpMethod.Post, "/workflows/test-runs/run-1/http/orders/42"));
            Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
            Assert.Equal(0, expiredFixture.Dispatcher.CallCount);
        }

        var stoppedFixture = await IngressFixture.StartAsync(stopped: true);
        await using (stoppedFixture)
        {
            var stopped = await stoppedFixture.Client.SendAsync(Request(HttpMethod.Post, "/workflows/test-runs/run-1/http/orders/42"));
            Assert.Equal(HttpStatusCode.Gone, stopped.StatusCode);
            Assert.Equal(0, stoppedFixture.Dispatcher.CallCount);
        }

        var closedFixture = await IngressFixture.StartAsync(closedScope: true);
        await using (closedFixture)
        {
            var closed = await closedFixture.Client.SendAsync(Request(HttpMethod.Post, "/workflows/test-runs/run-1/http/orders/42"));
            Assert.Equal(HttpStatusCode.Gone, closed.StatusCode);
            Assert.Equal(0, closedFixture.Dispatcher.CallCount);
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Authorization", "Test author");
        request.Headers.TryAddWithoutValidation("X-Trace", ["first", "second"]);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
        return request;
    }

    private sealed class IngressFixture(TestServer server, TargetStore targetStore, CapturingDispatcher dispatcher,
        TestPermissionAuthorizationService permissionAuthorization, TestEndpointAuthorization endpointAuthorization) : IAsyncDisposable
    {
        public HttpClient Client { get; } = server.CreateClient();
        public TargetStore TargetStore { get; } = targetStore;
        public CapturingDispatcher Dispatcher { get; } = dispatcher;
        public TestPermissionAuthorizationService PermissionAuthorization { get; } = permissionAuthorization;
        public TestEndpointAuthorization EndpointAuthorization { get; } = endpointAuthorization;

        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            server.Dispose();
            return ValueTask.CompletedTask;
        }

        public static async Task<IngressFixture> StartAsync(
            bool allowManagement = true,
            bool allowEndpoint = true,
            bool authorizeEndpoint = false,
            bool expired = false,
            bool stopped = false,
            bool closedScope = false,
            string? pathBase = null,
            bool useRequestHandlerAuthentication = false)
        {
            var targets = new TargetStore();
            var scopes = new ScopeStore();
            var target = Target("run-1", authorizeEndpoint, expired);
            if (stopped)
                target = target.WithStopped();
            targets.Add(target);
            var scopeRecord = closedScope
                ? new WorkflowTestScopeRecord(
                    target.Scope,
                    WorkflowTestScopeState.Closed,
                    target.CreatedAt,
                    closingAt: target.CreatedAt.AddSeconds(1),
                    closedAt: target.CreatedAt.AddSeconds(2),
                    closeReason: WorkflowTestScopeCloseReason.ExplicitTeardown)
                : new WorkflowTestScopeRecord(target.Scope, WorkflowTestScopeState.Open, target.CreatedAt);
            scopes.Add(scopeRecord);
            var dispatcher = new CapturingDispatcher(targets);
            var permissions = new TestPermissionAuthorizationService(allowManagement);
            var endpointAuthorization = new TestEndpointAuthorization(allowEndpoint);
            var server = new TestServer(new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    if (useRequestHandlerAuthentication)
                    {
                        services.AddAuthentication(OpenIddictLikeAuthHandler.SchemeName)
                            .AddScheme<AuthenticationSchemeOptions, OpenIddictLikeAuthHandler>(OpenIddictLikeAuthHandler.SchemeName, _ => { });
                    }
                    else
                    {
                        services.AddAuthentication(TestAuthHandler.SchemeName)
                            .AddScheme<AuthenticationSchemeOptions, TestIngressAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                    }
                    services.AddAuthorization();
                    services.AddSingleton<IRouteMatcher, TestRouteMatcher>();
                    services.AddSingleton<ITestRunTargetStore>(targets);
                    services.AddSingleton<IWorkflowTestScopeStore>(scopes);
                    services.AddSingleton<ITestRunTargetDispatcher>(dispatcher);
                    services.AddSingleton<IPermissionAuthorizationService>(permissions);
                    services.AddSingleton<IHttpEndpointAuthorizationHandler>(endpointAuthorization);
                    services.AddSingleton<IWorkflowExecutionPartitionAccessor>(new PartitionAccessor("test-partition"));
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton<IOptions<HttpEndpointOptions>>(
                        Microsoft.Extensions.Options.Options.Create(new HttpEndpointOptions { MaxRequestBodyBytes = 5 }));
                    services.AddScoped<HttpTestRunIngressMiddleware>();
                })
                .Configure(app =>
                {
                    if (!string.IsNullOrWhiteSpace(pathBase))
                        app.UsePathBase(pathBase);
                    if (useRequestHandlerAuthentication)
                        app.UseAuthentication();
                    app.UseMiddleware<HttpTestRunIngressMiddleware>();
                    app.Run(context =>
                    {
                        context.Response.StatusCode = 418;
                        return Task.CompletedTask;
                    });
                }));
            return new IngressFixture(server, targets, dispatcher, permissions, endpointAuthorization);
        }
    }

    private static TestRunTarget Target(string id, bool authorizeEndpoint, bool expired = false)
    {
        const string artifactId = "artifact-1";
        const string method = "post";
        const string template = "orders/{id}";
        var expiresAt = expired ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddMinutes(5);
        var createdAt = expiresAt.AddMinutes(-5);
        var identity = new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1", "hash-1");
        var node = new ExecutableNode(
            "node-1", "authored-1", HttpEndpoint.ActivityType, "1", "test", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var executable = new WorkflowExecutable(identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UtcNow, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HttpEndpointRouting.TemplateMetadataKey] = template,
            [HttpEndpointRouting.MethodMetadataKey] = method
        };
        if (authorizeEndpoint)
        {
            metadata[HttpEndpointRouting.AuthorizeMetadataKey] = "true";
            metadata[HttpEndpointRouting.PolicyMetadataKey] = "orders-admin";
        }
        var binding = new WorkflowTriggerBinding("binding-1", artifactId, identity.DefinitionId, identity.ArtifactVersion,
            identity.ArtifactHash, node.ExecutableNodeId, HttpEndpointStimulus.StimulusType,
            HttpEndpointStimulus.Hash(template, method), null, metadata, DateTimeOffset.UtcNow,
            Cardinality: TriggerCardinality.Exclusive);
        var scope = new WorkflowTestScope(id, expiresAt, "tenant-1", new WorkflowExecutionPartition("test-partition"));

        return new TestRunTarget(id, "Elsa.HttpEndpoint", "Elsa.HttpEndpoint", "http-request", identity, "source-1", binding, scope,
            "test-run", $"execution-{id}", $"idem-{id}", new Dictionary<string, JsonElement>(),
            new Dictionary<string, object?>(), null, createdAt);
    }

    private sealed class TargetStore : ITestRunTargetStore
    {
        private readonly Dictionary<string, TestRunTarget> _targets = new(StringComparer.Ordinal);
        public int FindCount { get; private set; }
        public void Add(TestRunTarget target) => _targets[target.TestRunId] = target;

        public ValueTask<TestRunTarget?> FindAsync(string testRunId, CancellationToken cancellationToken = default)
        {
            FindCount++;
            _targets.TryGetValue(testRunId, out var target);
            return ValueTask.FromResult(target);
        }

        public ValueTask<TestRunTarget> SaveAsync(TestRunTarget target, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TestRunTargetAdmission> BeginDispatchAsync(string testRunId, string stimulusDigest, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TestRunTarget?> CompleteDispatchAsync(string testRunId, TestRunTargetDispatchResult result, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TestRunTarget?> MarkStoppedAsync(string testRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<TestRunTarget?> RemoveAsync(string testRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ScopeStore : IWorkflowTestScopeStore
    {
        private readonly Dictionary<string, WorkflowTestScopeRecord> _scopes = new(StringComparer.Ordinal);
        public void Add(WorkflowTestScopeRecord scope) => _scopes[scope.Scope.ScopeId] = scope;
        public ValueTask<WorkflowTestScopeRecord?> FindAsync(string scopeId, CancellationToken cancellationToken = default)
        {
            _scopes.TryGetValue(scopeId, out var record);
            return ValueTask.FromResult(record);
        }
        public ValueTask<WorkflowTestScopeRecord> CreateAsync(WorkflowTestScope scope, DateTimeOffset createdAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTestScopeCloseResult> CloseAsync(WorkflowTestScopeCloseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTestScopeRecord> CompleteAsync(string scopeId, DateTimeOffset completedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTestScopePage> QueryAsync(WorkflowTestScopePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingDispatcher(TargetStore targets) : ITestRunTargetDispatcher
    {
        public int CallCount { get; private set; }
        public string? TestRunId { get; private set; }
        public JsonElement? Stimulus { get; private set; }
        public IServiceProvider? AmbientServices { get; private set; }

        public ValueTask<TestRunTargetDispatchResult> DispatchAsync(string testRunId, JsonElement? stimulusInput, CancellationToken cancellationToken = default) =>
            DispatchAsync(testRunId, stimulusInput, null, cancellationToken);

        public async ValueTask<TestRunTargetDispatchResult> DispatchAsync(
            string testRunId,
            JsonElement? stimulusInput,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            TestRunId = testRunId;
            Stimulus = stimulusInput?.Clone();
            AmbientServices = dispatchOptions?.AmbientServices;
            var target = (await targets.FindAsync(testRunId, cancellationToken))!;
            var now = DateTimeOffset.UtcNow;
            var commandDispatch = new WorkflowExecutionCommandDispatchResult("envelope-1", target.WorkflowExecutionId,
                WorkflowExecutionCommandDispatchStatus.Accepted, now);
            var agent = new WorkflowExecutionActorDescriptor(target.WorkflowExecutionId, "agent-1", "test", WorkflowExecutionActorStatus.Active,
                WorkflowExecutionActorCapabilities.None, now);
            var dispatch = new WorkflowExecutionStartDispatchResult(target.WorkflowExecutionId, target.Executable, commandDispatch, agent);
            return new TestRunTargetDispatchResult(dispatch, AlreadyDispatched: false);
        }
    }

    private sealed class TestPermissionAuthorizationService(bool allowed) : IPermissionAuthorizationService
    {
        public PermissionEvaluationContext? LastContext { get; private set; }
        public ValueTask<PermissionEvaluationResult> AuthorizeAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            LastContext = context;
            return ValueTask.FromResult(allowed ? PermissionEvaluationResult.Success : PermissionEvaluationResult.Denied());
        }
    }

    private sealed class TestEndpointAuthorization(bool allowed) : IHttpEndpointAuthorizationHandler
    {
        public int CallCount { get; private set; }
        public string? LastPolicy { get; private set; }
        public ValueTask<bool> AuthorizeAsync(AuthorizeHttpEndpointContext context)
        {
            CallCount++;
            LastPolicy = context.Policy;
            return ValueTask.FromResult(allowed);
        }
    }

    private sealed class PartitionAccessor(string partition) : IWorkflowExecutionPartitionAccessor
    {
        public WorkflowExecutionPartition Current { get; } = new(partition);
    }

    private sealed class TestIngressAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var header)
                || !header.ToString().StartsWith("Test ", StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, header.ToString()[5..]),
                new Claim(IdentityClaimTypes.TenantId, "tenant-1")
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class OpenIddictLikeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder), IAuthenticationRequestHandler
    {
        public const string SchemeName = "OpenIddictLike";
        private const string RequestHandledKey = "OpenIddictLike.RequestHandled";

        public Task<bool> HandleRequestAsync()
        {
            Context.Items[RequestHandledKey] = true;
            return Task.FromResult(false);
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Context.Items.ContainsKey(RequestHandledKey))
                throw new InvalidOperationException("The request handler must initialize authentication state first.");

            if (!Request.Headers.TryGetValue("Authorization", out var header)
                || !StringComparer.Ordinal.Equals(header.ToString(), "Bearer valid"))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "bearer-user"),
                new Claim(IdentityClaimTypes.TenantId, "tenant-1")
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
