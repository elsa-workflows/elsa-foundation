using Elsa.Api.AspNetCore;
using System.Net;
using System.Net.Http.Json;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Security.Claims;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>Caller-facing regression coverage for live dispatch admission control.</summary>
public sealed class RuntimeAdmissionShedResponseTests
{
    [Fact]
    public async Task Execute_returns_429_with_retry_after_when_the_start_was_shed()
    {
        await using var host = await RuntimeApiHost.StartAsync(ShedView(4));
        using var response = await host.Client.PostAsJsonAsync("/runtime/workflows/executables/artifact-1/execute", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(4, response.Headers.RetryAfter?.Delta?.TotalSeconds);
    }

    [Fact]
    public async Task Execute_never_returns_a_zero_retry_after()
    {
        await using var host = await RuntimeApiHost.StartAsync(ShedView(null));
        using var response = await host.Client.PostAsJsonAsync("/runtime/workflows/executables/artifact-1/execute", new { });
        Assert.Equal(1, response.Headers.RetryAfter?.Delta?.TotalSeconds);
    }

    [Fact]
    public async Task Execute_still_returns_200_for_a_deferred_dispatch_that_was_not_shed()
    {
        await using var host = await RuntimeApiHost.StartAsync(View(WorkflowExecutionCommandDispatchStatus.Deferred, shed: false));
        using var response = await host.Client.PostAsJsonAsync("/runtime/workflows/executables/artifact-1/execute", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Retry-After"));
    }

    [Fact]
    public void View_lifts_the_shed_marker_out_of_the_dispatch_metadata()
    {
        var result = NewDispatchResult(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.DispatchShed] = "true",
            [RuntimeMetadataKeys.DispatchRetryAfterSeconds] = "9"
        });
        var view = WorkflowExecutionStartDispatchView.From(result);
        Assert.True(view.Shed);
        Assert.Equal(9, view.RetryAfterSeconds);
    }

    [Fact]
    public void View_reports_an_unmarked_dispatch_as_not_shed()
    {
        var view = WorkflowExecutionStartDispatchView.From(NewDispatchResult(new Dictionary<string, string>(StringComparer.Ordinal)));
        Assert.False(view.Shed);
        Assert.Null(view.RetryAfterSeconds);
    }

    private static WorkflowExecutionStartDispatchView ShedView(int? retryAfterSeconds) =>
        View(WorkflowExecutionCommandDispatchStatus.Deferred, shed: true, retryAfterSeconds);

    private static WorkflowExecutionStartDispatchView View(WorkflowExecutionCommandDispatchStatus status, bool shed, int? retryAfterSeconds = null) =>
        new("wfexec-1", "artifact-1", "1.0.0", "sha256:test", status.ToString(), "envelope-1", "agent-1", "in-process", shed ? "at capacity" : null, Shed: shed, RetryAfterSeconds: retryAfterSeconds);

    private static WorkflowExecutionStartDispatchResult NewDispatchResult(IReadOnlyDictionary<string, string> metadata)
    {
        var identity = new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
        return new("wfexec-1", identity,
            new WorkflowExecutionCommandDispatchResult("envelope-1", "wfexec-1", WorkflowExecutionCommandDispatchStatus.Deferred, DateTimeOffset.UnixEpoch, "at capacity", metadata),
            new WorkflowExecutionActorDescriptor("wfexec-1", "agent-1", "in-process", WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.InProcessMailbox, DateTimeOffset.UnixEpoch));
    }

    private sealed class RuntimeApiHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<RuntimeApiHost> StartAsync(WorkflowExecutionStartDispatchView view)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddElsaEndpoints();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthentication("RuntimeApiTest")
                .AddScheme<AuthenticationSchemeOptions, AllowAuthenticationHandler>("RuntimeApiTest", _ => { });
            builder.Services.AddFoundationIdentityAbstractions(options =>
                options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "RuntimeApiTest" });
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton<IWorkflowExecutionStartService>(new StubStartService(view));
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(IdentityClaimTypes.Permission, "workflow-runtime.execute"), new Claim(IdentityClaimTypes.Normalized, "v1")],
                    "RuntimeApiTest"));
                await next();
            });
            app.UseAuthentication();
            app.UseAuthorization();
            Elsa.Workflows.Runtime.Api.WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(app);
            await app.StartAsync();
            return new(app, app.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class StubStartService(WorkflowExecutionStartDispatchView view) : IWorkflowExecutionStartService
    {
        public Task<WorkflowExecutionStartDispatchView> ExecuteAsync(ExecuteWorkflow request, CancellationToken cancellationToken) =>
            Task.FromResult(view);
    }

    private sealed class AllowAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "operator-1"), new System.Security.Claims.Claim(IdentityClaimTypes.Permission, "workflow-runtime.execute"), new System.Security.Claims.Claim(IdentityClaimTypes.Normalized, "v1")],
                    Scheme.Name)),
                Scheme.Name)));
    }
}
