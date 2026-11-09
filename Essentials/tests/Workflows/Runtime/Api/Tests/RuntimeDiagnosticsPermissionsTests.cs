using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>
/// #3130 (spec 261 T525): the diagnostics settings' <c>permissions.canManage</c> is the caller's own
/// <c>workflows/runtime:manage</c> grant, evaluated by <see cref="IWorkflowRuntimeOperatorAccess"/>'s rule, and fails
/// closed. Before the fix every reader saw <see langword="true"/>.
/// </summary>
[Collection(FastEndpointsHostCollection.Name)]
public sealed class RuntimeDiagnosticsPermissionsTests
{
    private const string Route = "/runtime/workflows/diagnostics/settings";

    [Fact]
    public async Task An_operator_with_manage_sees_canManage_true_on_read_and_on_save()
    {
        await using var host = await DiagnosticsHost.StartAsync([WorkflowRuntimePermissions.WorkflowRuntimeRead, WorkflowRuntimePermissions.WorkflowRuntimeManage]);

        Assert.True((await host.ReadPermissionsAsync()).GetProperty("canManage").GetBoolean());

        using var save = await host.Client.PutAsync(Route, JsonContent("""{"scope":"host-default","defaultLevel":"Metadata","subjectOverrides":{}}"""));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var saved = JsonDocument.Parse(await save.Content.ReadAsStringAsync());
        Assert.True(saved.RootElement.GetProperty("permissions").GetProperty("canManage").GetBoolean());
    }

    [Fact]
    public async Task A_reader_without_manage_sees_canManage_false_and_cannot_save()
    {
        await using var host = await DiagnosticsHost.StartAsync([WorkflowRuntimePermissions.WorkflowRuntimeRead]);

        Assert.False((await host.ReadPermissionsAsync()).GetProperty("canManage").GetBoolean());

        using var save = await host.Client.PutAsync(Route, JsonContent("""{"scope":"host-default","defaultLevel":"Metadata","subjectOverrides":{}}"""));
        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
    }

    [Fact]
    public async Task A_manage_grant_denied_for_the_runtime_reads_false()
    {
        await using var host = await DiagnosticsHost.StartAsync(
            [WorkflowRuntimePermissions.WorkflowRuntimeRead, WorkflowRuntimePermissions.WorkflowRuntimeManage], denyRuntimeManage: true);

        Assert.False((await host.ReadPermissionsAsync()).GetProperty("canManage").GetBoolean());
    }

    [Fact]
    public async Task An_anonymous_or_untrusted_caller_cannot_manage()
    {
        await using var host = await DiagnosticsHost.StartAsync([WorkflowRuntimePermissions.WorkflowRuntimeRead]);
        using var scope = host.Services.CreateScope();
        var access = new HttpContextWorkflowRuntimeOperatorAccess(
            new Microsoft.AspNetCore.Http.HttpContextAccessor(),
            scope.ServiceProvider.GetRequiredService<IPermissionAuthorizationService>(),
            scope.ServiceProvider.GetRequiredService<NormalizedPrincipalValidator>());

        Assert.False(await access.CanManageRuntimeAsync());
    }

    [Fact]
    public async Task Without_an_operator_access_rule_the_service_fails_closed()
    {
        var service = new RuntimeDiagnosticsSettingsService(new InMemoryRuntimeDiagnosticsSettingsStore());

        var view = await service.GetAsync(new GetRuntimeDiagnosticsSettings(), CancellationToken.None);

        Assert.False(view.Permissions.CanManage);
        Assert.False(RuntimeDiagnosticsPermissions.Default.CanManage);
    }

    [Fact]
    public async Task A_custom_operator_access_that_does_not_answer_the_runtime_question_fails_closed()
    {
        var service = new RuntimeDiagnosticsSettingsService(new InMemoryRuntimeDiagnosticsSettingsStore(), new RunOnlyAccess());

        var view = await service.GetAsync(new GetRuntimeDiagnosticsSettings(), CancellationToken.None);

        Assert.False(view.Permissions.CanManage);
    }

    private static StringContent JsonContent(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    // Answers only the per-run question, as an implementation written before #3130 does.
    private sealed class RunOnlyAccess : IWorkflowRuntimeOperatorAccess
    {
        public ValueTask<bool> CanManageAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class DiagnosticsHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public IServiceProvider Services => app.Services;

        public static async Task<DiagnosticsHost> StartAsync(IReadOnlyCollection<string> permissions, bool denyRuntimeManage = false)
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
            if (denyRuntimeManage)
                builder.Services.AddSingleton<IPermissionResourceHandler, DenyRuntimeManage>();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.RequestServices.GetRequiredService<IPersistenceAccessContextBinder>()
                    .Bind(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
                await next();
            });
            app.UseAuthentication();
            app.UseAuthorization();
            WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(app);
            await app.StartAsync();
            var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add("X-Permission", string.Join(",", permissions));
            return new DiagnosticsHost(app, client);
        }

        public async Task<JsonElement> ReadPermissionsAsync()
        {
            using var response = await Client.GetAsync(Route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("permissions").Clone();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    // Denies manage when it is asked for the runtime as a whole (no resource), as a host policy might.
    private sealed class DenyRuntimeManage : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PermissionEvaluationResult?>(
                context.Resource is null && context.Permission == WorkflowRuntimePermissions.WorkflowRuntimeManage
                    ? PermissionEvaluationResult.Denied()
                    : null);
    }

    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "DiagnosticsPermissionsTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Permission", out var header) || string.IsNullOrWhiteSpace(header))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = header.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(permission => new Claim(IdentityClaimTypes.Permission, permission))
                .Concat([
                    new Claim(ClaimTypes.NameIdentifier, "operator"),
                    new Claim(IdentityClaimTypes.Normalized, "v1"),
                    new Claim(IdentityClaimTypes.TenantId, "tenant-a")
                ])
                .ToArray();
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }
}
