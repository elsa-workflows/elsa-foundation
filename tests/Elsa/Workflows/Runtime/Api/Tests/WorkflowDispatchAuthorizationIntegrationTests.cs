using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

[Collection(FastEndpointsHostCollection.Name)]
public sealed class WorkflowDispatchAuthorizationIntegrationTests
{
    private const string PermissionHeader = "X-Test-Permission";
    private const string TenantHeader = "X-Test-Tenant";
    private const string PermissionClaimType = "permissions";

    [Fact]
    public async Task Unauthenticated_requests_are_rejected_before_read_or_redrive_handlers_run()
    {
        await using var host = await DispatchApiHost.StartAsync();

        using var list = await host.Client.GetAsync($"/runtime/workflows/dispatches?parentWorkflowExecutionId={host.TenantADispatch.ParentWorkflowExecutionId}");
        using var get = await host.Client.GetAsync($"/runtime/workflows/dispatches/{host.TenantADispatch.DispatchId}");
        using var redrive = await host.Client.PostAsJsonAsync(
            $"/runtime/workflows/dispatches/{host.TenantADispatch.DispatchId}/redrive",
            new { requestId = "unauthenticated-request" });

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, redrive.StatusCode);
        host.AssertDispatchUnchanged(host.TenantADispatch);
    }

    [Fact]
    public async Task Read_permission_allows_same_tenant_inspection_but_forbids_redrive_without_mutation()
    {
        await using var host = await DispatchApiHost.StartAsync();
        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");

        using var list = await host.Client.GetAsync($"/runtime/workflows/dispatches?parentWorkflowExecutionId={host.TenantADispatch.ParentWorkflowExecutionId}");
        using var get = await host.Client.GetAsync($"/runtime/workflows/dispatches/{host.TenantADispatch.DispatchId}");
        using var redrive = await host.Client.PostAsJsonAsync(
            $"/runtime/workflows/dispatches/{host.TenantADispatch.DispatchId}/redrive",
            new { requestId = "read-only-request" });

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, redrive.StatusCode);
        host.AssertDispatchUnchanged(host.TenantADispatch);
    }

    [Fact]
    public async Task Cross_tenant_read_and_list_fail_closed_without_disclosing_the_foreign_tenant()
    {
        await using var host = await DispatchApiHost.StartAsync();
        host.Authorize(PermissionNames.WorkflowRuntimeRead, "tenant-a");

        using var list = await host.Client.GetAsync($"/runtime/workflows/dispatches?parentWorkflowExecutionId={host.TenantBDispatch.ParentWorkflowExecutionId}");
        using var get = await host.Client.GetAsync($"/runtime/workflows/dispatches/{host.TenantBDispatch.DispatchId}");
        var listBody = await list.Content.ReadAsStringAsync();
        var getBody = await get.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, list.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, get.StatusCode);
        Assert.DoesNotContain("tenant-b", listBody, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-b", getBody, StringComparison.Ordinal);
        Assert.DoesNotContain(host.TenantBDispatch.DispatchId, listBody, StringComparison.Ordinal);
        Assert.DoesNotContain(host.TenantBDispatch.DispatchId, getBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manage_permission_cannot_read_and_cross_tenant_redrive_fails_without_mutation()
    {
        await using var host = await DispatchApiHost.StartAsync();
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");

        using var list = await host.Client.GetAsync($"/runtime/workflows/dispatches?parentWorkflowExecutionId={host.TenantADispatch.ParentWorkflowExecutionId}");
        using var get = await host.Client.GetAsync($"/runtime/workflows/dispatches/{host.TenantADispatch.DispatchId}");
        using var redrive = await host.Client.PostAsJsonAsync(
            $"/runtime/workflows/dispatches/{host.TenantBDispatch.DispatchId}/redrive",
            new { requestId = "cross-tenant-request" });
        var body = await redrive.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, redrive.StatusCode);
        Assert.DoesNotContain("tenant-b", body, StringComparison.Ordinal);
        Assert.DoesNotContain(host.TenantBDispatch.DispatchId, body, StringComparison.Ordinal);
        host.AssertDispatchUnchanged(host.TenantBDispatch);
    }

    [Fact]
    public async Task Manage_permission_redrives_matching_tenant_once_and_rejects_ineligible_without_mutation()
    {
        await using var host = await DispatchApiHost.StartAsync();
        host.Authorize(PermissionNames.WorkflowRuntimeManage, "tenant-a");

        using var acceptedResponse = await host.Client.PostAsJsonAsync(
            $"/runtime/workflows/dispatches/{host.TenantADispatch.DispatchId}/redrive",
            new { requestId = "accepted-request" });
        var accepted = await acceptedResponse.Content.ReadFromJsonAsync<WorkflowDispatchRedriveView>();
        Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
        Assert.NotNull(accepted);
        Assert.Equal(WorkflowDispatchRedriveDisposition.Accepted, accepted.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Pending, accepted.Status);
        Assert.Equal(1, accepted.DeliveryGeneration);

        using var replayResponse = await host.Client.PostAsJsonAsync(
            $"/runtime/workflows/dispatches/{host.TenantADispatch.DispatchId}/redrive",
            new { requestId = "accepted-request" });
        var replay = await replayResponse.Content.ReadFromJsonAsync<WorkflowDispatchRedriveView>();
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.NotNull(replay);
        Assert.Equal(WorkflowDispatchRedriveDisposition.AlreadyApplied, replay.Disposition);
        Assert.Equal(1, replay.DeliveryGeneration);

        var ineligibleBefore = host.TenantAIneligibleDispatch;
        using var ineligibleResponse = await host.Client.PostAsJsonAsync(
            $"/runtime/workflows/dispatches/{ineligibleBefore.DispatchId}/redrive",
            new { requestId = "ineligible-request" });
        var ineligible = await ineligibleResponse.Content.ReadFromJsonAsync<WorkflowDispatchRedriveView>();
        Assert.Equal(HttpStatusCode.OK, ineligibleResponse.StatusCode);
        Assert.NotNull(ineligible);
        Assert.Equal(WorkflowDispatchRedriveDisposition.NotEligible, ineligible.Disposition);
        Assert.Equal(0, ineligible.DeliveryGeneration);
        host.AssertDispatchUnchanged(ineligibleBefore);
    }

    private sealed class DispatchApiHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly InMemoryWorkflowDispatchStore _rawDispatchStore;

        private DispatchApiHost(
            WebApplication app,
            HttpClient client,
            InMemoryWorkflowDispatchStore rawDispatchStore,
            WorkflowDispatchRecord tenantADispatch,
            WorkflowDispatchRecord tenantBDispatch,
            WorkflowDispatchRecord tenantAIneligibleDispatch)
        {
            _app = app;
            Client = client;
            _rawDispatchStore = rawDispatchStore;
            TenantADispatch = tenantADispatch;
            TenantBDispatch = tenantBDispatch;
            TenantAIneligibleDispatch = tenantAIneligibleDispatch;
        }

        public HttpClient Client { get; }
        public WorkflowDispatchRecord TenantADispatch { get; }
        public WorkflowDispatchRecord TenantBDispatch { get; }
        public WorkflowDispatchRecord TenantAIneligibleDispatch { get; }

        public static async Task<DispatchApiHost> StartAsync()
        {
            var state = new InMemoryRuntimeCheckpointStoreState();
            var rawDispatchStore = new InMemoryWorkflowDispatchStore(state);
            var tenantAPending = Pending("parent-a", "activity-a", "tenant-a");
            var tenantBDispatch = Pending("parent-b", "activity-b", "tenant-b");
            var tenantAIneligibleDispatch = Pending("parent-a-ineligible", "activity-a-ineligible", "tenant-a");
            await rawDispatchStore.SaveAsync(tenantAPending);
            await rawDispatchStore.SaveAsync(tenantBDispatch);
            await rawDispatchStore.SaveAsync(tenantAIneligibleDispatch);
            var atomicStore = new InMemoryRuntimeCheckpointCommitStore(state: state, workflowDispatchStore: rawDispatchStore);
            var tenantADispatch = await SeedEligibleFailureAsync(atomicStore, rawDispatchStore, tenantAPending);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(state);
            builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            new WorkflowsRuntimeApiFeature().ConfigureServices(builder.Services);
            builder.Services.AddScoped<IRequestSender, DispatchInspectionRequestSender>();
            builder.Services.AddFastEndpoints(options =>
            {
                options.Assemblies = [typeof(WorkflowsRuntimeApiFeature).Assembly];
                options.Filter = type => type.FullName is
                    "Elsa.Workflows.Runtime.Api.Endpoints.ListWorkflowDispatchesEndpoint" or
                    "Elsa.Workflows.Runtime.Api.Endpoints.GetWorkflowDispatchEndpoint" or
                    "Elsa.Workflows.Runtime.Api.Endpoints.RedriveWorkflowDispatchEndpoint";
            });

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue(TenantHeader, out var tenant) &&
                    !string.IsNullOrWhiteSpace(tenant))
                {
                    context.RequestServices.GetRequiredService<IPersistenceAccessContextBinder>()
                        .Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant.ToString())));
                }
                await next();
            });
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseFastEndpoints(options => options.Security.PermissionsClaimType = PermissionClaimType);
            await app.StartAsync();

            return new DispatchApiHost(
                app,
                app.GetTestClient(),
                rawDispatchStore,
                tenantADispatch,
                tenantBDispatch,
                tenantAIneligibleDispatch);
        }

        private static async Task<WorkflowDispatchRecord> SeedEligibleFailureAsync(
            InMemoryRuntimeCheckpointCommitStore atomicStore,
            InMemoryWorkflowDispatchStore dispatchStore,
            WorkflowDispatchRecord pending)
        {
            var identity = new WorkflowDispatchIdentity(
                pending.ParentWorkflowExecutionId,
                pending.ParentActivityExecutionId);
            var outboxId = $"outbox:{pending.DispatchId}";
            var item = new RuntimePostCommitOutboxItem(
                outboxId,
                new RuntimePostCommitIntent(
                    identity.StartIntentId,
                    pending.ParentWorkflowExecutionId,
                    WorkflowDispatchLifecycle.StartChildIntentKind,
                    pending.CreatedAt,
                    pending.ParentActivityExecutionId,
                    identity.StartIdempotencyKey,
                    payload: null,
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.DispatchId] = pending.DispatchId,
                        [RuntimeMetadataKeys.ChildWorkflowExecutionId] = pending.ChildWorkflowExecutionId
                    }),
                RuntimePostCommitOutboxStatus.Pending,
                pending.CreatedAt,
                pending.CreatedAt,
                new RuntimePostCommitRetryPolicy(1, TimeSpan.FromSeconds(1)));
            await atomicStore.AddPendingForTestingAsync(item);
            var claim = Assert.Single(await atomicStore.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                "seed-owner",
                pending.CreatedAt,
                TimeSpan.FromMinutes(1),
                1)));
            var failedAt = pending.CreatedAt.AddSeconds(1);
            await atomicStore.RecordDeliveryResultAsync(
                claim,
                new RuntimePostCommitOutboxDeliveryResult(
                    outboxId,
                    RuntimePostCommitOutboxStatus.FailedFinal,
                    failedAt,
                    "fixed-safe-summary"));
            var failed = WorkflowDispatchLifecycle.TransitionToDispatchFailed(
                pending,
                outboxId,
                generation: 0,
                attemptCount: 1,
                firstAttemptAt: pending.CreatedAt,
                failedAt);
            await dispatchStore.SaveAsync(failed);
            return failed;
        }

        public void Authorize(string permission, string tenant)
        {
            Client.DefaultRequestHeaders.Remove(PermissionHeader);
            Client.DefaultRequestHeaders.Remove(TenantHeader);
            Client.DefaultRequestHeaders.Add(PermissionHeader, permission);
            Client.DefaultRequestHeaders.Add(TenantHeader, tenant);
        }

        public void AssertDispatchUnchanged(WorkflowDispatchRecord expected)
        {
            var actual = _rawDispatchStore.FindAsync(expected.DispatchId).AsTask().GetAwaiter().GetResult();
            Assert.NotNull(actual);
            Assert.True(WorkflowDispatchLifecycle.RecordsEqual(expected, actual!));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    private sealed class DispatchInspectionRequestSender(IServiceProvider services) : IRequestSender
    {
        public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default)
            where T : notnull
        {
            object response = request switch
            {
                ListWorkflowDispatches list => await new ListWorkflowDispatchesRequestHandler(
                        services.GetRequiredService<IWorkflowDispatchQueryStore>(),
                        services.GetRequiredService<IPersistenceAccessContextAccessor>())
                    .Handle(list, cancellationToken),
                GetWorkflowDispatch get => await new GetWorkflowDispatchRequestHandler(
                        services.GetRequiredService<IWorkflowDispatchStore>(),
                        services.GetRequiredService<IPersistenceAccessContextAccessor>())
                    .Handle(get, cancellationToken),
                RedriveWorkflowDispatch redrive => await new RedriveWorkflowDispatchRequestHandler(
                        services.GetRequiredService<IWorkflowDispatchRedriveStore>(),
                        services.GetRequiredService<TimeProvider>())
                    .Handle(redrive, cancellationToken),
                _ => throw new NotSupportedException($"Unexpected dispatch inspection request '{request.GetType().FullName}'.")
            };
            return (T)response;
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "DispatchApiTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(PermissionHeader, out var permission) || string.IsNullOrWhiteSpace(permission))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "operator-1"), new Claim(PermissionClaimType, permission.ToString())],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private static WorkflowDispatchRecord Pending(string parentId, string activityId, string tenantId)
    {
        var identity = new WorkflowDispatchIdentity(parentId, activityId);
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parentId,
            activityId,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            new WorkflowExecutableSourceProvenance(
                "source-1", "WorkflowDefinitionVersion", "version-1", "1.0.0",
                "definition-1", "version-1", "1.0.0", "publication-1", "slot-1"),
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            correlationId: null,
            tenantId,
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(parentId, "initiator-1"),
            [],
            now,
            now);
    }
}
