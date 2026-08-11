using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>
/// The caller-facing half of live dispatch admission control (RB1, #1235). A start has no execution id to hand back,
/// so a refusal has to be a status code the caller already knows how to retry rather than a body it would have to
/// correlate against. A dispatch into a running instance already names an execution, so it keeps its existing
/// <c>Deferred</c> shape and is not touched here.
/// </summary>
public sealed class RuntimeAdmissionShedResponseTests
{
    [Fact]
    public async Task Execute_returns_429_with_retry_after_when_the_start_was_shed()
    {
        var endpoint = NewExecuteEndpoint(ShedView(retryAfterSeconds: 4));

        await HandleAsync(endpoint);

        Assert.Equal(StatusCodes.Status429TooManyRequests, endpoint.HttpContext.Response.StatusCode);
        Assert.Equal("4", endpoint.HttpContext.Response.Headers.RetryAfter);
    }

    [Fact]
    public async Task Execute_never_returns_a_zero_retry_after()
    {
        // A Retry-After of 0 invites an immediate retry, which is the one thing a host at capacity cannot absorb.
        var endpoint = NewExecuteEndpoint(ShedView(retryAfterSeconds: null));

        await HandleAsync(endpoint);

        Assert.Equal("1", endpoint.HttpContext.Response.Headers.RetryAfter);
    }

    [Fact]
    public async Task Execute_still_returns_200_for_a_deferred_dispatch_that_was_not_shed()
    {
        // The distributed leaf defers legitimately ("forwarded to the owning node"), so Deferred on its own must not
        // be read as backpressure. Only the shed marker turns it into a 429.
        var endpoint = NewExecuteEndpoint(View(WorkflowExecutionCommandDispatchStatus.Deferred, shed: false));

        await HandleAsync(endpoint);

        Assert.Equal(StatusCodes.Status200OK, endpoint.HttpContext.Response.StatusCode);
        Assert.False(endpoint.HttpContext.Response.Headers.ContainsKey("Retry-After"));
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

    // The endpoint is internal, so it is created and driven by reflection the same way the rest of this suite does.
    private static BaseEndpoint NewExecuteEndpoint(WorkflowExecutionStartDispatchView view)
    {
        var type = RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.Endpoints.Execute")!;
        return RuntimeApiEndpointTestFactory.Create(type, new StubRequestSender(view));
    }

    private static Task HandleAsync(BaseEndpoint endpoint) =>
        (Task)endpoint.GetType()
            .GetMethod("HandleAsync", [typeof(ExecuteWorkflow), typeof(CancellationToken)])!
            .Invoke(endpoint, [new ExecuteWorkflow("artifact-1"), CancellationToken.None])!;

    private static WorkflowExecutionStartDispatchView ShedView(int? retryAfterSeconds) =>
        View(WorkflowExecutionCommandDispatchStatus.Deferred, shed: true, retryAfterSeconds);

    private static WorkflowExecutionStartDispatchView View(
        WorkflowExecutionCommandDispatchStatus status,
        bool shed,
        int? retryAfterSeconds = null) =>
        new(
            "wfexec-1",
            "artifact-1",
            "1.0.0",
            "sha256:test",
            status.ToString(),
            "envelope-1",
            "agent-1",
            "in-process",
            shed ? "at capacity" : null,
            Shed: shed,
            RetryAfterSeconds: retryAfterSeconds);

    private static WorkflowExecutionStartDispatchResult NewDispatchResult(IReadOnlyDictionary<string, string> metadata)
    {
        var identity = new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
        return new(
            workflowExecutionId: "wfexec-1",
            pinnedExecutable: identity,
            commandDispatch: new WorkflowExecutionCommandDispatchResult(
                envelopeId: "envelope-1",
                workflowExecutionId: "wfexec-1",
                status: WorkflowExecutionCommandDispatchStatus.Deferred,
                recordedAt: DateTimeOffset.UnixEpoch,
                reason: "at capacity",
                metadata: metadata),
            agent: new WorkflowExecutionActorDescriptor(
                "wfexec-1",
                "agent-1",
                "in-process",
                WorkflowExecutionActorStatus.Active,
                WorkflowExecutionActorCapabilities.InProcessMailbox,
                DateTimeOffset.UnixEpoch));
    }

    private sealed class StubRequestSender(WorkflowExecutionStartDispatchView view) : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            Task.FromResult((T)(object)view);
    }
}
