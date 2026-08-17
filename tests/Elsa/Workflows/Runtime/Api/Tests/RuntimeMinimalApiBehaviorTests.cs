using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>Direct Minimal API regression coverage for the high-risk behavior formerly exercised through endpoint classes.</summary>
public sealed class RuntimeMinimalApiBehaviorTests
{
    private const string ExecutePath = "/runtime/workflows/executables/artifact-1/execute";
    private const string ActivityPath = "/runtime/workflows/instances/wf-1/activity-executions/ae-1";
    private const string DescendantsPath = ActivityPath + "/descendants";
    private const string LayoutPath = ActivityPath + "/layout";
    private const string ValuePayloadPath = ActivityPath + "/value-evidence/value-1/payload";

    [Theory]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Accepted, HttpStatusCode.OK)]
    [InlineData(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted, HttpStatusCode.OK)]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Duplicate, HttpStatusCode.OK)]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Deferred, HttpStatusCode.OK)]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Rejected, HttpStatusCode.Conflict)]
    public async Task Execute_maps_each_dispatch_status_to_its_honest_http_status(
        WorkflowExecutionCommandDispatchStatus dispatchStatus,
        HttpStatusCode expectedStatus)
    {
        await using var host = await StartAsync(_ => DispatchView(dispatchStatus));

        using var response = await host.Client.PostAsJsonAsync(ExecutePath, new { });

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(typeof(WorkflowExecutableNotFoundException), HttpStatusCode.BadRequest)]
    [InlineData(typeof(WorkflowExecutableReferenceRejectedException), HttpStatusCode.Conflict)]
    [InlineData(typeof(ArgumentException), HttpStatusCode.BadRequest)]
    [InlineData(typeof(InvalidOperationException), HttpStatusCode.InternalServerError)]
    public async Task Execute_maps_expected_failures_to_problem_details_without_disclosing_unexpected_errors(
        Type exceptionType,
        HttpStatusCode expectedStatus)
    {
        const string secret = "storage-secret-must-not-cross-the-boundary";
        Exception exception = exceptionType == typeof(WorkflowExecutableNotFoundException)
            ? new WorkflowExecutableNotFoundException("artifact-1")
            : exceptionType == typeof(WorkflowExecutableReferenceRejectedException)
                ? new WorkflowExecutableReferenceRejectedException("artifact-1", WorkflowExecutableReferenceScope.Published, WorkflowExecutableReferenceRejectionReason.SelectionNotFound)
                : exceptionType == typeof(ArgumentException)
                    ? new ArgumentException("invalid execute request", "sourceReferenceId")
                    : new InvalidOperationException(secret);
        await using var host = await StartAsync(_ => throw exception);

        using var response = await host.Client.PostAsJsonAsync(ExecutePath, new { });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.InternalServerError)
            Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        else
            Assert.Contains("runtime-request", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execute_rethrows_cancellation_and_does_not_convert_it_to_a_problem()
    {
        var cancellation = new OperationCanceledException("request canceled");
        await using var host = await StartAsync(_ => throw cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Client.PostAsJsonAsync(ExecutePath, new { }));
    }

    [Fact]
    public async Task Execute_source_reference_validation_preserves_400_and_409_dispositions()
    {
        await using (var invalidHost = await StartAsync(request => request is ExecuteWorkflow { SourceReferenceId: " " }
            ? throw new ArgumentException("Source reference ID cannot be blank when provided.", "sourceReferenceId")
            : DispatchView(WorkflowExecutionCommandDispatchStatus.Accepted)))
        {
            using var response = await invalidHost.Client.PostAsJsonAsync(ExecutePath, new { sourceReferenceId = " " });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        await using var rejectedHost = await StartAsync(_ => throw new WorkflowExecutableReferenceRejectedException(
            "artifact-1",
            WorkflowExecutableReferenceScope.Published,
            WorkflowExecutableReferenceRejectionReason.SelectionNotFound));
        using var rejected = await rejectedHost.Client.PostAsJsonAsync(ExecutePath, new { sourceReferenceId = "missing-ref" });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
    }

    [Fact]
    public async Task Malformed_execute_body_returns_400_without_invoking_the_request_sender()
    {
        var calls = 0;
        await using var host = await StartAsync(_ =>
        {
            calls++;
            return DispatchView(WorkflowExecutionCommandDispatchStatus.Accepted);
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, ExecutePath)
        {
            Content = new StringContent("{malformed", System.Text.Encoding.UTF8, "application/json")
        };

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Missing_activity_execution_uses_the_canonical_404_problem_on_all_inspection_routes()
    {
        await using var host = await StartAsync(request => request switch
        {
            GetActivityExecution => new GetActivityExecutionResponse(null),
            GetActivityExecutionDescendants => new GetActivityExecutionDescendantsResponse(null),
            GetActivityExecutionLayout => new GetActivityExecutionLayoutResponse(null),
            GetActivityExecutionValuePayload => new GetActivityExecutionValuePayloadResponse(ActivityExecutionValuePayloadReadResult.NotFound()),
            _ => throw new InvalidOperationException("unexpected request")
        });

        foreach (var path in new[] { ActivityPath, DescendantsPath, LayoutPath, ValuePayloadPath })
        {
            using var response = await host.Client.GetAsync(path);
            var body = await AssertActivityProblemAsync(response, HttpStatusCode.NotFound, "activity.execution.not-found");
            Assert.Equal(path, body.GetProperty("instance").GetString());
        }
    }

    [Theory]
    [InlineData(ActivityExecutionHierarchyCursorFailure.Invalid, HttpStatusCode.BadRequest, "activity.request.invalid")]
    [InlineData(ActivityExecutionHierarchyCursorFailure.BindingMismatch, HttpStatusCode.Conflict, "activity.cursor.binding-mismatch")]
    [InlineData(ActivityExecutionHierarchyCursorFailure.Expired, HttpStatusCode.Gone, "activity.cursor.expired")]
    public async Task Descendant_cursor_failures_preserve_safe_problem_details(
        ActivityExecutionHierarchyCursorFailure failure,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        var metadata = failure == ActivityExecutionHierarchyCursorFailure.Invalid
            ? null
            : new ActivityExecutionCursorFailureMetadata(
                "activity-execution-hierarchy",
                ActivityExecutionCursorBindingState.Matched,
                ActivityExecutionCursorBindingState.Matched,
                failure == ActivityExecutionHierarchyCursorFailure.BindingMismatch
                    ? ActivityExecutionCursorBindingState.Mismatched
                    : ActivityExecutionCursorBindingState.Matched,
                true,
                "restart-from-first-page");
        await using var host = await StartAsync(_ => throw new ActivityExecutionHierarchyCursorException(
            failure,
            "provider message must not define the wire contract",
            metadata: metadata));

        using var response = await host.Client.GetAsync(DescendantsPath);
        var body = await AssertActivityProblemAsync(
            response,
            expectedStatus,
            expectedCode,
            failure == ActivityExecutionHierarchyCursorFailure.Invalid ? "Invalid activity execution cursor" : null);

        Assert.DoesNotContain("provider message", body.GetRawText(), StringComparison.Ordinal);
        if (metadata is not null)
        {
            var cursor = body.GetProperty("cursor");
            Assert.Equal(metadata.CursorClass, cursor.GetProperty("cursorClass").GetString());
            Assert.True(cursor.GetProperty("recoverable").GetBoolean());
            Assert.Equal(metadata.RecoveryAction, cursor.GetProperty("recoveryAction").GetString());
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ActivityExecutionValuePayloadReadOutcome.Denied, "activity.value-payload.forbidden")]
    [InlineData(HttpStatusCode.Conflict, ActivityExecutionValuePayloadReadOutcome.Unavailable, "activity.value-payload.unavailable")]
    public async Task Value_payload_denial_and_unavailability_preserve_their_distinct_problem_codes(
        HttpStatusCode expectedStatus,
        ActivityExecutionValuePayloadReadOutcome outcome,
        string expectedCode)
    {
        await using var host = await StartAsync(_ => new GetActivityExecutionValuePayloadResponse(new(outcome, null)));

        using var response = await host.Client.GetAsync(ValuePayloadPath);

        await AssertActivityProblemAsync(response, expectedStatus, expectedCode);
    }

    [Fact]
    public async Task Value_payload_resolution_returns_the_captured_json_and_rethrows_cancellation()
    {
        var value = new ActivityExecutionValuePayloadView("value-1", "Payload", JsonSerializer.SerializeToElement(new { answer = 42 }));
        await using (var host = await StartAsync(_ => new GetActivityExecutionValuePayloadResponse(ActivityExecutionValuePayloadReadResult.Resolved(value))))
        {
            using var response = await host.Client.GetAsync(ValuePayloadPath);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(42, body.GetProperty("payload").GetProperty("answer").GetInt32());
        }

        var cancellation = new OperationCanceledException("request canceled");
        await using var canceledHost = await StartAsync(_ => throw cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledHost.Client.GetAsync(ValuePayloadPath));
    }

    [Fact]
    public async Task Activity_inspection_unexpected_failures_are_non_disclosing_and_invalid_requests_are_400()
    {
        await using (var invalidHost = await StartAsync(_ => throw new ArgumentException("invalid inspection request")))
        {
            foreach (var path in new[] { ActivityPath, DescendantsPath, LayoutPath, ValuePayloadPath })
            {
                using var response = await invalidHost.Client.GetAsync(path);
                await AssertActivityProblemAsync(response, HttpStatusCode.BadRequest, "activity.request.invalid");
            }
        }

        const string secret = "inspection-storage-secret";
        await using var unexpectedHost = await StartAsync(_ => throw new InvalidOperationException(secret));
        foreach (var path in new[] { ActivityPath, DescendantsPath, LayoutPath, ValuePayloadPath })
        {
            using var response = await unexpectedHost.Client.GetAsync(path);
            var body = await AssertActivityProblemAsync(response, HttpStatusCode.InternalServerError, "activity.operation.failed");
            Assert.DoesNotContain(secret, body.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Layout_without_a_sidecar_returns_the_automatic_layout_view()
    {
        var view = new ActivityExecutionLayoutView("wf-1", "ae-1", "artifact-1", "", "Automatic", [], "sha256:historical-template", [], [], []);
        await using var host = await StartAsync(_ => new GetActivityExecutionLayoutResponse(view));

        using var response = await host.Client.GetAsync(LayoutPath);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Automatic", body.GetProperty("selection").GetString());
        Assert.Empty(body.GetProperty("nodes").EnumerateArray());
        Assert.Empty(body.GetProperty("connections").EnumerateArray());
    }

    private static async Task<JsonElement> AssertActivityProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        string? expectedTitle = null)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new[] { "type", "title", "status", "detail", "instance", "errorCode", "traceId", "diagnostics", "cursor" },
            body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(code, body.GetProperty("errorCode").GetString());
        Assert.Equal($"https://elsa.dev/problems/{code.Replace('.', '-')}", body.GetProperty("type").GetString());
        Assert.Equal(expectedTitle ?? code switch
        {
            "activity.execution.not-found" => "Activity execution not found",
            "activity.request.invalid" => "Invalid activity execution request",
            "activity.cursor.binding-mismatch" => "Activity execution cursor does not match",
            "activity.cursor.expired" => "Activity execution cursor expired",
            "activity.value-payload.forbidden" => "Activity execution value payload resolution denied",
            "activity.value-payload.unavailable" => "Activity execution value payload unavailable",
            "activity.operation.failed" => "Activity execution inspection failed",
            _ => throw new Xunit.Sdk.XunitException($"Unexpected activity error code '{code}'.")
        }, body.GetProperty("title").GetString());
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.NotEmpty(body.GetProperty("traceId").GetString() ?? string.Empty);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("diagnostics").ValueKind);
        return body;
    }

    private static WorkflowExecutionStartDispatchView DispatchView(WorkflowExecutionCommandDispatchStatus status) =>
        new("wfexec-1", "artifact-1", "1.0.0", "hash-1", status.ToString(), "envelope-1", "agent-1", "in-process", status == WorkflowExecutionCommandDispatchStatus.Accepted ? null : "reason");

    private static async Task<BehaviorHost> StartAsync(Func<object, object> behavior)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("RuntimeBehavior")
            .AddScheme<AuthenticationSchemeOptions, AllowAuthenticationHandler>("RuntimeBehavior", _ => { });
        builder.Services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "RuntimeBehavior" });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IRequestSender>(new RecordingRequestSender(behavior));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(app);
        await app.StartAsync();
        return new(app, app.GetTestClient());
    }

    private sealed class BehaviorHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class RecordingRequestSender(Func<object, object> behavior) : IRequestSender
    {
        public int Calls { get; private set; }

        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            Calls++;
            return Task.FromResult((T)behavior(request));
        }
    }

    private sealed class AllowAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "operator-1"),
                        new Claim(IdentityClaimTypes.Permission, "workflow-runtime.read"),
                        new Claim(IdentityClaimTypes.Permission, "workflow-runtime.execute"),
                        new Claim(IdentityClaimTypes.Normalized, "v1")
                    ],
                    Scheme.Name)),
                Scheme.Name)));
    }
}
