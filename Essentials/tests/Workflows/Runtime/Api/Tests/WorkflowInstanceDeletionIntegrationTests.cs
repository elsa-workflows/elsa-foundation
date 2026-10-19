using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
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
public sealed class WorkflowInstanceDeletionIntegrationTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(WorkflowRuntimePermissions.WorkflowRuntimeRead, HttpStatusCode.Forbidden)]
    public async Task Permission_denial_never_stops_or_deletes_a_run(string? permission, HttpStatusCode expected)
    {
        await using var host = await DeletionHost.StartAsync(permission);
        using var response = await host.DeleteAsync();
        Assert.Equal(expected, response.StatusCode);
        host.AssertUnchanged();
    }

    [Theory]
    [InlineData("tenant-b", false)]
    [InlineData("tenant-a", true)]
    public async Task Foreign_tenant_and_resource_denial_are_indistinguishable_from_missing(string tenant, bool denyResource)
    {
        await using var host = await DeletionHost.StartAsync(tenant: tenant, denyResource: denyResource);
        using var response = await host.DeleteAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("tenant-a", await response.Content.ReadAsStringAsync());
        host.AssertUnchanged();
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Faulted)]
    [InlineData(WorkflowExecutionStatus.Cancelled)]
    public async Task Terminal_run_deletion_returns_exact_receipt_and_repeat_is_missing(WorkflowExecutionStatus status)
    {
        await using var host = await DeletionHost.StartAsync(status: status);
        using var response = await host.DeleteAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<WorkflowInstanceDeletionView>();
        Assert.Equal(new WorkflowInstanceDeletionView("run-a"), receipt);
        Assert.Equal(0, host.Actor.Calls);
        Assert.Null(host.Store.Candidate);
        using var repeated = await host.DeleteAsync();
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Pending)]
    [InlineData(WorkflowExecutionStatus.Running)]
    [InlineData(WorkflowExecutionStatus.Suspended)]
    public async Task Active_run_is_cancelled_and_reauthorized_before_terminal_purge(WorkflowExecutionStatus status)
    {
        await using var host = await DeletionHost.StartAsync(status: status);
        host.Actor.OnEnqueue = () => host.Store.Candidate = host.Store.Candidate! with
        {
            State = host.Store.Candidate.State with { Status = WorkflowExecutionStatus.Cancelled }, ConcurrencyToken = "cancelled"
        };
        using var response = await host.DeleteAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, host.Actor.Calls);
        Assert.Equal(WorkflowExecutionCommandKind.Cancel, host.Actor.Envelope!.Command.Kind);
        Assert.Equal("tenant-a", host.Actor.Envelope.Partition.Value);
        Assert.Equal("cancelled", host.Store.DeletedToken);
        Assert.True(host.ResourceAuthorization.Calls >= 2);
    }

    [Theory]
    [InlineData(WorkflowExecutionDeletionResult.Changed)]
    [InlineData(WorkflowExecutionDeletionResult.NotTerminal)]
    public async Task Concurrent_reactivation_does_not_purge_the_run(WorkflowExecutionDeletionResult result)
    {
        await using var host = await DeletionHost.StartAsync();
        host.Store.BeforeDelete = () => host.Store.Candidate = host.Store.Candidate! with
        {
            State = host.Store.Candidate.State with { Status = WorkflowExecutionStatus.Running }, ConcurrencyToken = "reactivated"
        };
        host.Store.Result = result;
        using var response = await host.DeleteAsync();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(host.Store.Candidate);
        Assert.Null(host.Store.DeletedToken);
        Assert.Equal(0, host.Actor.Calls);
    }

    [Fact]
    public async Task Durably_forwarded_cancellation_waits_for_the_terminal_snapshot()
    {
        await using var host = await DeletionHost.StartAsync(status: WorkflowExecutionStatus.Running);
        host.Actor.Status = WorkflowExecutionCommandDispatchStatus.Deferred;
        host.Actor.OnEnqueue = () => host.Store.Candidate = host.Store.Candidate! with
        {
            State = host.Store.Candidate.State with { Status = WorkflowExecutionStatus.Cancelled }, ConcurrencyToken = "forwarded-cancelled"
        };
        using var response = await host.DeleteAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("forwarded-cancelled", host.Store.DeletedToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Partition_mismatch_and_unattributed_legacy_state_fail_closed_before_cancellation(bool legacy)
    {
        await using var host = await DeletionHost.StartAsync(status: WorkflowExecutionStatus.Running);
        host.Store.Candidate = host.Store.Candidate! with { State = host.Store.Candidate.State with
        {
            TenantId = null,
            Partition = legacy ? null : new WorkflowExecutionPartition("tenant-b")
        } };
        using var response = await host.DeleteAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        host.AssertUnchanged();
    }

    [Fact]
    public async Task Authorization_is_rechecked_after_cancellation_before_purge()
    {
        await using var host = await DeletionHost.StartAsync(status: WorkflowExecutionStatus.Running);
        host.Actor.OnEnqueue = () =>
        {
            host.ResourceAuthorization.Deny = true;
            host.Store.Candidate = host.Store.Candidate! with { State = host.Store.Candidate.State with { Status = WorkflowExecutionStatus.Cancelled } };
        };
        using var response = await host.DeleteAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(host.Store.Candidate);
        Assert.Equal(0, host.Store.DeleteCalls);
    }

    [Fact]
    public async Task Rejected_stop_is_actionable_and_keeps_history()
    {
        await using var host = await DeletionHost.StartAsync(status: WorkflowExecutionStatus.Running);
        host.Actor.Status = WorkflowExecutionCommandDispatchStatus.Rejected;
        using var response = await host.DeleteAsync();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("retry deletion", body);
        Assert.DoesNotContain("private worker failure", body);
        Assert.Equal(0, host.Store.DeleteCalls);
    }

    [Fact]
    public async Task Accepted_cancellation_without_a_terminal_checkpoint_keeps_the_run_for_retry()
    {
        await using var host = await DeletionHost.StartAsync(status: WorkflowExecutionStatus.Suspended);
        using var response = await host.DeleteAsync();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("still stopping", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, host.Actor.Calls);
        Assert.Equal(0, host.Store.DeleteCalls);
        Assert.NotNull(host.Store.Candidate);
    }

    [Fact]
    public async Task Unsupported_provider_never_advertises_or_cancels()
    {
        await using var host = await DeletionHost.StartAsync(status: WorkflowExecutionStatus.Running);
        host.Store.IsSupported = false;
        using var scope = host.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<RuntimeRunDeletionCapabilitySource>().GetCapabilitiesAsync());
        using var response = await host.DeleteAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        host.AssertUnchanged();
    }

    [Fact]
    public async Task Supported_provider_advertises_the_exact_delete_template()
    {
        await using var host = await DeletionHost.StartAsync();
        using var scope = host.Services.CreateScope();
        var declaration = Assert.Single(await scope.ServiceProvider.GetRequiredService<RuntimeRunDeletionCapabilitySource>().GetCapabilitiesAsync());
        var link = Assert.Single(declaration.Links);
        Assert.Equal("workflow-instance-delete", link.Rel);
        Assert.Equal("runtime/workflows/instances/{workflowExecutionId}", link.Href);
    }

    private sealed class DeletionHost(WebApplication app, HttpClient client, DeletionStore store, CancellationActor actor, ResourceAuthorization resourceAuthorization) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public DeletionStore Store { get; } = store;
        public CancellationActor Actor { get; } = actor;
        public ResourceAuthorization ResourceAuthorization { get; } = resourceAuthorization;
        public IServiceProvider Services => app.Services;

        public static async Task<DeletionHost> StartAsync(
            string? permission = WorkflowRuntimePermissions.WorkflowRuntimeManage,
            string tenant = "tenant-a", bool denyResource = false, WorkflowExecutionStatus status = WorkflowExecutionStatus.Completed)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddElsaEndpoints();
            builder.Services.AddAuthentication(Authentication.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, Authentication>(Authentication.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { Authentication.SchemeName });
            new WorkflowsRuntimeApiFeature().ConfigureServices(builder.Services);
            var store = new DeletionStore(status);
            var actor = new CancellationActor();
            var resourceAuthorization = new ResourceAuthorization { Deny = denyResource };
            builder.Services.AddSingleton<IWorkflowExecutionDeletionStore>(store);
            builder.Services.AddSingleton<IWorkflowExecutionActorProvider>(actor);
            builder.Services.AddSingleton<IPermissionResourceHandler>(resourceAuthorization);
            builder.Services.AddScoped<RuntimeRunDeletionCapabilitySource>();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.RequestServices.GetRequiredService<IPersistenceAccessContextBinder>()
                    .Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
                await next();
            });
            app.UseAuthentication();
            app.UseAuthorization();
            WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(app);
            await app.StartAsync();
            var client = app.GetTestClient();
            if (permission is not null) client.DefaultRequestHeaders.Add("X-Permission", permission);
            client.DefaultRequestHeaders.Add("X-Tenant", tenant);
            return new DeletionHost(app, client, store, actor, resourceAuthorization);
        }

        public Task<HttpResponseMessage> DeleteAsync() => Client.DeleteAsync("/runtime/workflows/instances/run-a");
        public void AssertUnchanged()
        {
            Assert.NotNull(Store.Candidate);
            Assert.Equal(0, Store.DeleteCalls);
            Assert.Equal(0, Actor.Calls);
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
    }

    private sealed class DeletionStore(WorkflowExecutionStatus status) : IWorkflowExecutionDeletionStore
    {
        public bool IsSupported { get; set; } = true;
        public WorkflowExecutionDeletionCandidate? Candidate { get; set; } = new(new WorkflowExecutionState("run-a",
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "hash"), status,
            null, DateTimeOffset.UtcNow, null, null, null, null, null, "tenant-a", new Dictionary<string, string>())
            { Partition = new WorkflowExecutionPartition("tenant-a") }, "initial");
        public int DeleteCalls { get; private set; }
        public string? DeletedToken { get; private set; }
        public WorkflowExecutionDeletionResult Result { get; set; } = WorkflowExecutionDeletionResult.Deleted;
        public Action? BeforeDelete { get; set; }
        public ValueTask<WorkflowExecutionDeletionCandidate?> ReadAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Candidate);
        public ValueTask<WorkflowExecutionDeletionResult> TryDeleteTerminalAsync(WorkflowExecutionDeletionCandidate candidate, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            BeforeDelete?.Invoke();
            if (Result == WorkflowExecutionDeletionResult.Deleted) { DeletedToken = candidate.ConcurrencyToken; Candidate = null; }
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class ResourceAuthorization : IPermissionResourceHandler
    {
        public bool Deny { get; set; }
        public int Calls { get; private set; }
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (context.Resource is not WorkflowExecutionState) return ValueTask.FromResult<PermissionEvaluationResult?>(null);
            Calls++;
            return ValueTask.FromResult<PermissionEvaluationResult?>(Deny ? PermissionEvaluationResult.Denied() : PermissionEvaluationResult.Success);
        }
    }

    private sealed class CancellationActor : IWorkflowExecutionActorProvider, IWorkflowExecutionActor
    {
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.None;
        public WorkflowExecutionActorDescriptor Descriptor => throw new NotSupportedException();
        public int Calls { get; private set; }
        public Action? OnEnqueue { get; set; }
        public WorkflowExecutionCommandDispatchStatus Status { get; set; } = WorkflowExecutionCommandDispatchStatus.Accepted;
        public WorkflowExecutionCommandEnvelope? Envelope { get; private set; }
        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult<IWorkflowExecutionActor>(this);
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Calls++; Envelope = envelope; OnEnqueue?.Invoke();
            return ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(envelope.EnvelopeId, envelope.WorkflowExecutionId,
                Status, DateTimeOffset.UtcNow, Status == WorkflowExecutionCommandDispatchStatus.Accepted ? null : "private worker failure"));
        }
    }

    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "RunDeletionTest";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Permission", out var permission)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "operator"), new Claim(IdentityClaimTypes.Permission, permission.ToString()),
                new Claim(IdentityClaimTypes.Normalized, "v1"), new Claim(IdentityClaimTypes.TenantId, Request.Headers["X-Tenant"].ToString()) };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }
}
