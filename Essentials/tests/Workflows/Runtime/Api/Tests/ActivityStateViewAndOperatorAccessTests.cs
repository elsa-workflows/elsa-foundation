using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Services;
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
using InspectionContext = Elsa.Workflows.Runtime.Api.Services.HttpContextActivityExecutionInspectionAuthorizationContext;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>
/// Spec 260 R-6, the activity state view endpoint, and FR-416, the per-run permission signal on the run details: the
/// grants and tenant rules, the unavailable reasons, the capability link, and that the signal fails closed.
/// </summary>
[Collection(FastEndpointsHostCollection.Name)]
public sealed class ActivityStateViewAndOperatorAccessTests
{
    private const string StatePath = "/runtime/workflows/instances/run-a/activity-executions/ae-scope/state-view";
    private static readonly string[] Reader = [WorkflowRuntimePermissions.WorkflowRuntimeRead, InspectionContext.StructurePermission];

    [Fact]
    public async Task A_reader_gets_the_projected_view_without_values()
    {
        await using var host = await StateViewHost.StartAsync(Reader);

        using var response = await host.Client.GetAsync(StatePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("run-a", root.GetProperty("workflowExecutionId").GetString());
        Assert.Equal("ae-scope", root.GetProperty("activityExecutionId").GetString());
        Assert.Equal("node-scope", root.GetProperty("executableNodeId").GetString());
        Assert.Equal("Running", root.GetProperty("activityStatus").GetString());
        Assert.True(root.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("unavailableReason").ValueKind);
        Assert.Equal(TestProjector.Kind, root.GetProperty("viewKind").GetString());
        Assert.Equal(1, root.GetProperty("viewVersion").GetInt32());
        Assert.False(root.GetProperty("canInspectValues").GetBoolean());
        var view = root.GetProperty("view");
        Assert.Equal("node-scope", view.GetProperty("node").GetString());
        Assert.Equal(1, view.GetProperty("incidents").GetInt32());
        Assert.False(view.GetProperty("values").GetBoolean());
    }

    [Fact]
    public async Task The_projector_is_told_when_the_caller_may_inspect_values()
    {
        await using var host = await StateViewHost.StartAsync([.. Reader, InspectionContext.SensitiveValuesPermission]);

        using var document = JsonDocument.Parse(await host.Client.GetStringAsync(StatePath));

        Assert.True(document.RootElement.GetProperty("canInspectValues").GetBoolean());
        Assert.True(document.RootElement.GetProperty("view").GetProperty("values").GetBoolean());
    }

    public static TheoryData<string> NotVisible() => ["unknown-run", "unknown-activity", "no-inspect", "other-tenant"];

    [Theory]
    [MemberData(nameof(NotVisible))]
    public async Task An_unknown_unauthorized_or_foreign_run_or_activity_is_404(string shape)
    {
        var permissions = shape == "no-inspect" ? [WorkflowRuntimePermissions.WorkflowRuntimeRead] : Reader;
        await using var host = await StateViewHost.StartAsync(permissions, tenant: shape == "other-tenant" ? "tenant-b" : "tenant-a");
        var path = shape switch
        {
            "unknown-run" => "/runtime/workflows/instances/run-missing/activity-executions/ae-scope/state-view",
            "unknown-activity" => "/runtime/workflows/instances/run-a/activity-executions/ae-missing/state-view",
            _ => StatePath
        };

        using var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("tenant-a", body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestProjector.Kind, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_requires_the_runtime_read_permission()
    {
        await using var host = await StateViewHost.StartAsync([InspectionContext.StructurePermission]);

        using var response = await host.Client.GetAsync(StatePath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_activity_whose_private_state_was_cleared_says_so()
    {
        await using var host = await StateViewHost.StartAsync(Reader, privateState: false);

        using var document = JsonDocument.Parse(await host.Client.GetStringAsync(StatePath));

        Assert.False(document.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal("StateCleared", document.RootElement.GetProperty("unavailableReason").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("view").ValueKind);
    }

    [Fact]
    public async Task A_module_can_project_a_retained_reference_after_private_state_is_cleared()
    {
        await using var host = await StateViewHost.StartAsync(Reader, privateState: false, clearedProjector: true);
        using var document = JsonDocument.Parse(await host.Client.GetStringAsync(StatePath));
        Assert.True(document.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal("test.retained-reference", document.RootElement.GetProperty("viewKind").GetString());
        Assert.Equal("node-scope", document.RootElement.GetProperty("view").GetProperty("node").GetString());
    }

    [Fact]
    public async Task An_opted_in_projector_can_report_that_no_retained_reference_remains()
    {
        await using var host = await StateViewHost.StartAsync(Reader, privateState: false, clearedProjector: true, clearedUnavailable: true);
        using var document = JsonDocument.Parse(await host.Client.GetStringAsync(StatePath));
        Assert.False(document.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal("StateCleared", document.RootElement.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public async Task A_cleared_activity_without_its_pinned_executable_keeps_the_state_cleared_answer()
    {
        await using var host = await StateViewHost.StartAsync(Reader, privateState: false, clearedProjector: true, seedExecutable: false);
        using var response = await host.Client.GetAsync(StatePath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal("StateCleared", document.RootElement.GetProperty("unavailableReason").GetString());
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("structure")]
    [InlineData("runtime")]
    public async Task Cleared_state_projection_does_not_run_before_authorization(string change)
    {
        var permissions = change == "structure" ? [WorkflowRuntimePermissions.WorkflowRuntimeRead]
            : change == "runtime" ? [InspectionContext.StructurePermission] : Reader;
        await using var host = await StateViewHost.StartAsync(permissions, privateState: false, clearedProjector: true,
            tenant: change == "tenant" ? "other" : "tenant-a");
        using var response = await host.Client.GetAsync(StatePath);
        Assert.Equal(change == "runtime" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, Assert.Single(host.Services.GetServices<IActivityStateViewProjector>().OfType<ClearedProjector>()).Calls);
    }

    [Fact]
    public async Task An_existing_private_state_projector_keeps_precedence()
    {
        await using var host = await StateViewHost.StartAsync(Reader, clearedProjector: true);
        using var document = JsonDocument.Parse(await host.Client.GetStringAsync(StatePath));
        Assert.Equal(TestProjector.Kind, document.RootElement.GetProperty("viewKind").GetString());
        Assert.Equal(0, Assert.Single(host.Services.GetServices<IActivityStateViewProjector>().OfType<ClearedProjector>()).Calls);
    }

    [Fact]
    public void Duplicate_cleared_activity_selectors_are_rejected_even_with_different_state_aliases()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ActivityStateViewProjectorRegistry([
            new ClearedProjector(alias: "one"), new ClearedProjector(alias: "two")]));
        Assert.Contains("Test.Scope", error.Message);
    }

    [Fact]
    public async Task Existing_projectors_default_to_refusing_cleared_state()
    {
        IActivityStateViewProjector projector = new TestProjector();
        Assert.Null(projector.ActivityTypeWithoutState);
        Assert.Null(await projector.ProjectWithoutStateAsync(null!));
    }

    [Fact]
    public async Task A_private_state_without_a_projector_says_so()
    {
        await using var host = await StateViewHost.StartAsync(Reader, stateAlias: "Contoso.Unprojected.State");

        using var document = JsonDocument.Parse(await host.Client.GetStringAsync(StatePath));

        Assert.False(document.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal("NoProjector", document.RootElement.GetProperty("unavailableReason").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_state_view_link_is_advertised_only_with_a_projector(bool withProjector)
    {
        await using var host = await StateViewHost.StartAsync(Reader, registerProjector: withProjector);
        using var scope = host.Services.CreateScope();

        var declarations = await new RuntimeActivityStateViewCapabilitySource(scope.ServiceProvider).GetCapabilitiesAsync();

        if (!withProjector)
        {
            Assert.Empty(declarations);
            return;
        }

        var link = Assert.Single(Assert.Single(declarations).Links);
        Assert.Equal("activity-execution-state-view", link.Rel);
        Assert.Equal("runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/state-view", link.Href);
        Assert.True(link.Templated);
    }

    [Fact]
    public void Two_projectors_for_one_alias_fail_the_registry()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new ActivityStateViewProjectorRegistry([new TestProjector(), new TestProjector()]));
        Assert.Contains(TestProjector.Alias, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_duplicate_projector_fails_startup()
    {
        var thrown = await Record.ExceptionAsync(async () =>
        {
            await using var host = await StateViewHost.StartAsync(Reader, duplicateProjector: true);
            using var scope = host.Services.CreateScope();
            foreach (var task in scope.ServiceProvider.GetServices<Elsa.Tasks.Core.IStartupTask>())
                await task.ExecuteAsync(CancellationToken.None);
        });

        // Dependency injection may wrap the registry's own failure; the cause must be the duplicate alias.
        var exception = Assert.IsType<InvalidOperationException>(thrown?.GetBaseException());
        Assert.Contains(TestProjector.Alias, exception.Message, StringComparison.Ordinal);
    }

    // --- FR-416: the permission signal on the run details ---

    [Fact]
    public async Task An_operator_with_manage_sees_canManage_true()
    {
        await using var host = await StateViewHost.StartAsync([.. Reader, WorkflowRuntimePermissions.WorkflowRuntimeManage]);

        var permissions = await host.ReadPermissionsAsync();

        Assert.True(permissions.GetProperty("canManage").GetBoolean());
        Assert.False(permissions.GetProperty("canInspectValues").GetBoolean());
    }

    [Fact]
    public async Task A_reader_without_manage_sees_canManage_false_and_canInspectValues_as_granted()
    {
        await using var host = await StateViewHost.StartAsync([.. Reader, InspectionContext.SensitiveValuesPermission]);

        var permissions = await host.ReadPermissionsAsync();

        Assert.False(permissions.GetProperty("canManage").GetBoolean());
        Assert.True(permissions.GetProperty("canInspectValues").GetBoolean());
    }

    [Fact]
    public async Task A_manage_grant_denied_for_the_run_as_resource_reads_false()
    {
        await using var host = await StateViewHost.StartAsync([.. Reader, WorkflowRuntimePermissions.WorkflowRuntimeManage], denyManageResource: true);

        var permissions = await host.ReadPermissionsAsync();

        Assert.False(permissions.GetProperty("canManage").GetBoolean());
    }

    [Fact]
    public async Task Another_tenant_still_gets_404_for_the_run_details()
    {
        await using var host = await StateViewHost.StartAsync([.. Reader, WorkflowRuntimePermissions.WorkflowRuntimeManage], tenant: "tenant-b");

        using var response = await host.Client.GetAsync("/runtime/workflows/instances/run-a");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_or_untrusted_principal_cannot_manage()
    {
        await using var host = await StateViewHost.StartAsync(Reader);
        using var scope = host.Services.CreateScope();
        var access = new HttpContextWorkflowRuntimeOperatorAccess(
            new Microsoft.AspNetCore.Http.HttpContextAccessor(),
            scope.ServiceProvider.GetRequiredService<IPermissionAuthorizationService>(),
            scope.ServiceProvider.GetRequiredService<NormalizedPrincipalValidator>());

        Assert.False(await access.CanManageAsync(StateViewHost.Run()));
    }

    private sealed class StateViewHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public IServiceProvider Services => app.Services;

        public static async Task<StateViewHost> StartAsync(
            IReadOnlyCollection<string> permissions,
            string tenant = "tenant-a",
            bool privateState = true,
            string stateAlias = TestProjector.Alias,
            bool registerProjector = true,
            bool duplicateProjector = false,
            bool denyManageResource = false,
            bool clearedProjector = false,
            bool clearedUnavailable = false,
            bool seedExecutable = true)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddElsaEndpoints();
            builder.Services.AddAuthentication(Authentication.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, Authentication>(Authentication.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { Authentication.SchemeName });
            var feature = new WorkflowsRuntimeApiFeature();
            feature.ConfigureServices(builder.Services);
            feature.PostConfigureServices(builder.Services);
            if (registerProjector)
                builder.Services.AddSingleton<IActivityStateViewProjector, TestProjector>();
            if (clearedProjector)
                builder.Services.AddSingleton<IActivityStateViewProjector>(new ClearedProjector(unavailable: clearedUnavailable));
            if (duplicateProjector)
                builder.Services.AddSingleton<IActivityStateViewProjector, TestProjector>();
            if (denyManageResource)
                builder.Services.AddSingleton<IPermissionResourceHandler, DenyManageForRuns>();
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
            await SeedAsync(app.Services, privateState, stateAlias, seedExecutable);
            var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-Permission", string.Join(",", permissions));
            client.DefaultRequestHeaders.Add("X-Tenant", tenant);
            return new StateViewHost(app, client);
        }

        public async Task<JsonElement> ReadPermissionsAsync()
        {
            using var response = await Client.GetAsync("/runtime/workflows/instances/run-a");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("permissions").Clone();
        }

        public static WorkflowExecutionState Run() =>
            new("run-a", Identity, WorkflowExecutionStatus.Running, null, DateTimeOffset.UtcNow, null, null, null, null, null, "tenant-a", new Dictionary<string, string>())
            {
                Partition = new WorkflowExecutionPartition("tenant-a")
            };

        private static readonly WorkflowExecutableIdentity Identity = new("artifact-a", "definition-a", "version-a", "1.0.0", "sha256:a");

        private static async Task SeedAsync(IServiceProvider services, bool privateState, string stateAlias, bool seedExecutable)
        {
            using var scope = services.CreateScope();
            var provider = scope.ServiceProvider;
            provider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            await provider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(Run());
            var node = new ExecutableNode("node-scope", "authored-scope", "Test.Scope", "1.0.0", "test", JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
            if (seedExecutable)
                await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new WorkflowExecutable(
                    Identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch,
                    new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
            var activity = new ActivityExecutionState(
                new ActivityExecution("ae-scope", "run-a", "node-scope", "authored-scope", "Test.Scope", "1.0.0"),
                ActivityExecutionStatus.Running, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null,
                null, null, null, null, 0, [], [], 0, 0, new Dictionary<string, string>());
            if (privateState)
            {
                activity = activity with
                {
                    PrivateState = new ActivityPrivateState("ae-scope", 1,
                        ValueEnvelope.Inline(new ValueTypeDescriptor(stateAlias, schemaVersion: 1), JsonSerializer.SerializeToElement(new { secret = "4111" }), ValueProtectionPolicy.InstanceInline),
                        "attempt-1", DateTimeOffset.UnixEpoch)
                };
            }
            await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(activity);
            await provider.GetRequiredService<IIncidentStateStore>().SaveAsync(new IncidentState(
                "inc-1", "run-a", "ae-child", "node-child", IncidentSeverity.Error, IncidentStatus.Blocking, null,
                "ActivityReturnedFault", "Card 4111 declined.", DateTimeOffset.UnixEpoch, null));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    /// <summary>A projector that reports what it was given, so the endpoint's wiring is visible.</summary>
    private sealed class TestProjector : IActivityStateViewProjector
    {
        public const string Alias = "Test.Scope.State";
        public const string Kind = "test.view";

        public string StateTypeAlias => Alias;
        public string ViewKind => Kind;
        public int ViewVersion => 1;

        public ValueTask<JsonElement> ProjectAsync(ActivityStateViewContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(JsonSerializer.SerializeToElement(new
            {
                node = context.ExecutableNode.ExecutableNodeId,
                incidents = context.Incidents.Count,
                values = context.CanInspectSensitiveValues
            }));
    }

    private sealed class ClearedProjector(string alias = "Test.Retained.State", bool unavailable = false) : IActivityStateViewProjector
    {
        public string StateTypeAlias => alias;
        public int Calls { get; private set; }
        public string? ActivityTypeWithoutState => "Test.Scope";
        public string ViewKind => "test.retained-reference";
        public int ViewVersion => 1;
        public ValueTask<JsonElement> ProjectAsync(ActivityStateViewContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test exercises only the explicit absent-state projection.");
        public ValueTask<JsonElement?> ProjectWithoutStateAsync(ActivityStateViewContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult<JsonElement?>(unavailable ? null : JsonSerializer.SerializeToElement(new { node = context.ExecutableNode.ExecutableNodeId }));
        }
    }

    private sealed class DenyManageForRuns : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PermissionEvaluationResult?>(
                context.Resource is WorkflowExecutionState && context.Permission == WorkflowRuntimePermissions.WorkflowRuntimeManage
                    ? PermissionEvaluationResult.Denied()
                    : null);
    }

    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "StateViewTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Permission", out var header) || string.IsNullOrWhiteSpace(header))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = header.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(permission => new Claim(IdentityClaimTypes.Permission, permission))
                .Concat([
                    new Claim(ClaimTypes.NameIdentifier, "operator"),
                    new Claim(IdentityClaimTypes.Normalized, "v1"),
                    new Claim(IdentityClaimTypes.TenantId, Request.Headers["X-Tenant"].ToString())
                ])
                .ToArray();
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }
}
