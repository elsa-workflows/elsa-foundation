using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ActivityExecutionInspectionHttpContractTests
{
    private const string DetailPath = "/runtime/workflows/instances/wf-1/activity-executions/ae-1";
    private const string DescendantsPath = DetailPath + "/descendants";
    private const string LayoutPath = DetailPath + "/layout";
    private const string ValuePayloadPath = DetailPath + "/value-evidence/value-1/payload";

    [Fact]
    public void Routes_and_verbs_match_the_reviewed_runtime_inspection_contract()
    {
        var detail = DetailEndpoint(new Sender(_ => new GetActivityExecutionResponse(null)));
        var descendants = DescendantsEndpoint(new Sender(_ => new GetActivityExecutionDescendantsResponse(null)));
        var layout = LayoutEndpoint(new Sender(_ => new GetActivityExecutionLayoutResponse(null)));
        var valuePayload = ValuePayloadEndpoint(new Sender(_ => new GetActivityExecutionValuePayloadResponse(
            ActivityExecutionValuePayloadReadResult.NotFound())));

        detail.Configure();
        descendants.Configure();
        layout.Configure();

        AssertEndpoint(detail, "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}");
        AssertEndpoint(descendants, "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants");
        AssertEndpoint(layout, "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout");
        AssertEndpoint(valuePayload, "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/value-evidence/{evidenceId}/payload");
    }

    [Fact]
    public async Task Missing_execution_uses_the_same_canonical_404_shape_on_all_four_routes()
    {
        var detail = DetailEndpoint(new Sender(_ => new GetActivityExecutionResponse(null)));
        var descendants = DescendantsEndpoint(new Sender(_ => new GetActivityExecutionDescendantsResponse(null)));
        var layout = LayoutEndpoint(new Sender(_ => new GetActivityExecutionLayoutResponse(null)));
        var valuePayload = ValuePayloadEndpoint(new Sender(_ => new GetActivityExecutionValuePayloadResponse(
            ActivityExecutionValuePayloadReadResult.NotFound())));

        await detail.HandleAsync(new("wf-1", "ae-1"), default);
        await descendants.HandleAsync(new("wf-1", "ae-1"), default);
        await layout.HandleAsync(new("wf-1", "ae-1"), default);
        await valuePayload.HandleAsync(new("wf-1", "ae-1", "value-1"), default);

        await AssertProblemAsync(detail, 404, "activity.execution.not-found", "Activity execution not found", DetailPath);
        await AssertProblemAsync(descendants, 404, "activity.execution.not-found", "Activity execution not found", DescendantsPath);
        await AssertProblemAsync(layout, 404, "activity.execution.not-found", "Activity execution not found", LayoutPath);
        await AssertProblemAsync(valuePayload, 404, "activity.execution.not-found", "Activity execution not found", ValuePayloadPath);
    }

    [Theory]
    [InlineData(ActivityExecutionValuePayloadReadOutcome.Denied, 403, "activity.value-payload.forbidden", "Activity execution value payload resolution denied")]
    [InlineData(ActivityExecutionValuePayloadReadOutcome.Unavailable, 409, "activity.value-payload.unavailable", "Activity execution value payload unavailable")]
    public async Task Value_payload_terminal_outcomes_map_to_canonical_problem_details(
        ActivityExecutionValuePayloadReadOutcome outcome,
        int status,
        string errorCode,
        string title)
    {
        var endpoint = ValuePayloadEndpoint(new Sender(_ => new GetActivityExecutionValuePayloadResponse(
            new(outcome, null))));

        await endpoint.HandleAsync(new("wf-1", "ae-1", "value-1"), default);

        await AssertProblemAsync(endpoint, status, errorCode, title, ValuePayloadPath);
    }

    [Fact]
    public async Task Resolved_value_payload_returns_the_exact_captured_json()
    {
        var endpoint = ValuePayloadEndpoint(new Sender(_ => new GetActivityExecutionValuePayloadResponse(
            ActivityExecutionValuePayloadReadResult.Resolved(new(
                "value-1",
                "Payload",
                JsonSerializer.SerializeToElement(new { answer = 42 }))))));

        await endpoint.HandleAsync(new("wf-1", "ae-1", "value-1"), default);

        Assert.Equal(StatusCodes.Status200OK, endpoint.HttpContext.Response.StatusCode);
        using var json = JsonDocument.Parse(await ResponseBodyAsync(endpoint));
        Assert.Equal("value-1", Property(json.RootElement, "EvidenceId").GetString());
        Assert.Equal(42, Property(json.RootElement, "Payload").GetProperty("answer").GetInt32());
    }

    [Theory]
    [InlineData(ActivityExecutionHierarchyCursorFailure.Invalid, 400, "activity.request.invalid", "Invalid activity execution cursor")]
    [InlineData(ActivityExecutionHierarchyCursorFailure.BindingMismatch, 409, "activity.cursor.binding-mismatch", "Activity execution cursor does not match")]
    [InlineData(ActivityExecutionHierarchyCursorFailure.Expired, 410, "activity.cursor.expired", "Activity execution cursor expired")]
    public async Task Descendant_cursor_failures_map_deterministically(
        ActivityExecutionHierarchyCursorFailure failure,
        int status,
        string errorCode,
        string title)
    {
        var endpoint = DescendantsEndpoint(new Sender(_ =>
            throw new ActivityExecutionHierarchyCursorException(
                failure,
                "provider message must not define the wire contract",
                metadata: failure == ActivityExecutionHierarchyCursorFailure.Invalid
                    ? null
                    : new(
                        "activity-execution-hierarchy",
                        ActivityExecutionCursorBindingState.Matched,
                        ActivityExecutionCursorBindingState.Matched,
                        failure == ActivityExecutionHierarchyCursorFailure.BindingMismatch
                            ? ActivityExecutionCursorBindingState.Mismatched
                            : ActivityExecutionCursorBindingState.Matched,
                        true,
                        "restart-from-first-page"))));

        await endpoint.HandleAsync(new("wf-1", "ae-1", "opaque"), default);

        var body = await AssertProblemAsync(endpoint, status, errorCode, title, DescendantsPath);
        Assert.DoesNotContain("provider message", body, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        if (failure == ActivityExecutionHierarchyCursorFailure.Invalid)
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("cursor").ValueKind);
        else
        {
            var cursor = json.RootElement.GetProperty("cursor");
            Assert.Equal("activity-execution-hierarchy", cursor.GetProperty("cursorClass").GetString());
            Assert.True(cursor.GetProperty("recoverable").GetBoolean());
            Assert.Equal("restart-from-first-page", cursor.GetProperty("recoveryAction").GetString());
        }
    }

    [Fact]
    public async Task Invalid_requests_use_canonical_400_on_all_four_routes()
    {
        var sender = new Sender(_ => throw new ArgumentException("The inspection request is invalid."));
        var detail = DetailEndpoint(sender);
        var descendants = DescendantsEndpoint(sender);
        var layout = LayoutEndpoint(sender);
        var valuePayload = ValuePayloadEndpoint(sender);

        await detail.HandleAsync(new("", "ae-1"), default);
        await descendants.HandleAsync(new("", "ae-1"), default);
        await layout.HandleAsync(new("", "ae-1"), default);
        await valuePayload.HandleAsync(new("", "ae-1", "value-1"), default);

        await AssertProblemAsync(detail, 400, "activity.request.invalid", "Invalid activity execution request", DetailPath);
        await AssertProblemAsync(descendants, 400, "activity.request.invalid", "Invalid activity execution request", DescendantsPath);
        await AssertProblemAsync(layout, 400, "activity.request.invalid", "Invalid activity execution request", LayoutPath);
        await AssertProblemAsync(valuePayload, 400, "activity.request.invalid", "Invalid activity execution request", ValuePayloadPath);
    }

    [Fact]
    public async Task Unexpected_failures_use_non_disclosing_canonical_500_on_all_four_routes()
    {
        var sender = new Sender(_ => throw new InvalidOperationException("storage secret"));
        var detail = DetailEndpoint(sender);
        var descendants = DescendantsEndpoint(sender);
        var layout = LayoutEndpoint(sender);
        var valuePayload = ValuePayloadEndpoint(sender);

        await detail.HandleAsync(new("wf-1", "ae-1"), default);
        await descendants.HandleAsync(new("wf-1", "ae-1"), default);
        await layout.HandleAsync(new("wf-1", "ae-1"), default);
        await valuePayload.HandleAsync(new("wf-1", "ae-1", "value-1"), default);

        var bodies = new[]
        {
            await AssertProblemAsync(detail, 500, "activity.operation.failed", "Activity execution inspection failed", DetailPath),
            await AssertProblemAsync(descendants, 500, "activity.operation.failed", "Activity execution inspection failed", DescendantsPath),
            await AssertProblemAsync(layout, 500, "activity.operation.failed", "Activity execution inspection failed", LayoutPath),
            await AssertProblemAsync(valuePayload, 500, "activity.operation.failed", "Activity execution inspection failed", ValuePayloadPath)
        };
        Assert.All(bodies, body => Assert.DoesNotContain("storage secret", body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Value_payload_endpoint_propagates_cancellation()
    {
        var cancellation = new OperationCanceledException("request canceled");
        var endpoint = ValuePayloadEndpoint(new Sender(_ => throw cancellation));

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            endpoint.HandleAsync(new("wf-1", "ae-1", "value-1"), default));

        Assert.Same(cancellation, exception);
    }

    [Fact]
    public async Task Historical_execution_without_a_layout_sidecar_returns_automatic_layout_not_404()
    {
        var view = new ActivityExecutionLayoutView(
            "wf-1",
            "ae-1",
            "artifact-1",
            "",
            "Automatic",
            [],
            "sha256:historical-template",
            [],
            [],
            []);
        var endpoint = LayoutEndpoint(new Sender(_ => new GetActivityExecutionLayoutResponse(view)));

        await endpoint.HandleAsync(new("wf-1", "ae-1"), default);

        Assert.Equal(StatusCodes.Status200OK, endpoint.HttpContext.Response.StatusCode);
        using var json = JsonDocument.Parse(await ResponseBodyAsync(endpoint));
        Assert.Equal("Automatic", Property(json.RootElement, "Selection").GetString());
        Assert.Empty(Property(json.RootElement, "Nodes").EnumerateArray());
        Assert.Empty(Property(json.RootElement, "Connections").EnumerateArray());
    }

    private static void AssertEndpoint(BaseEndpoint endpoint, string route)
    {
        Assert.Equal("GET", Assert.Single(endpoint.Definition.Verbs));
        Assert.Equal(route, Assert.Single(endpoint.Definition.Routes));
    }

    private static async Task<string> AssertProblemAsync(
        BaseEndpoint endpoint,
        int status,
        string errorCode,
        string title,
        string instance)
    {
        Assert.Equal(status, endpoint.HttpContext.Response.StatusCode);
        Assert.Equal("application/problem+json", endpoint.HttpContext.Response.ContentType);
        var body = await ResponseBodyAsync(endpoint);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        string[] expectedProperties = ["type", "title", "status", "detail", "instance", "errorCode", "traceId", "diagnostics", "cursor"];
        Assert.Equal(expectedProperties, root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal($"https://elsa.dev/problems/{errorCode.Replace('.', '-')}", root.GetProperty("type").GetString());
        Assert.Equal(title, root.GetProperty("title").GetString());
        Assert.Equal(status, root.GetProperty("status").GetInt32());
        Assert.Equal(instance, root.GetProperty("instance").GetString());
        Assert.Equal(errorCode, root.GetProperty("errorCode").GetString());
        Assert.Equal("trace-runtime-inspection", root.GetProperty("traceId").GetString());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
        return body;
    }

    private static GetActivityExecutionEndpoint DetailEndpoint(IRequestSender sender) =>
        Factory.Create<GetActivityExecutionEndpoint>(context => ConfigureContext(context, DetailPath), sender, NullLogger<GetActivityExecutionEndpoint>.Instance);

    private static GetActivityExecutionDescendantsEndpoint DescendantsEndpoint(IRequestSender sender) =>
        Factory.Create<GetActivityExecutionDescendantsEndpoint>(context => ConfigureContext(context, DescendantsPath), sender, NullLogger<GetActivityExecutionDescendantsEndpoint>.Instance);

    private static GetActivityExecutionLayoutEndpoint LayoutEndpoint(IRequestSender sender) =>
        Factory.Create<GetActivityExecutionLayoutEndpoint>(context => ConfigureContext(context, LayoutPath), sender, NullLogger<GetActivityExecutionLayoutEndpoint>.Instance);

    private static GetActivityExecutionValuePayloadEndpoint ValuePayloadEndpoint(IRequestSender sender) =>
        Factory.Create<GetActivityExecutionValuePayloadEndpoint>(
            context => ConfigureContext(context, ValuePayloadPath),
            sender,
            NullLogger<GetActivityExecutionValuePayloadEndpoint>.Instance);

    private static void ConfigureContext(DefaultHttpContext context, string path)
    {
        context.Request.Path = path;
        context.TraceIdentifier = "trace-runtime-inspection";
        context.Response.Body = new MemoryStream();
    }

    private static async Task<string> ResponseBodyAsync(BaseEndpoint endpoint)
    {
        endpoint.HttpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(endpoint.HttpContext.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.EnumerateObject().Single(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private sealed class Sender(Func<object, object> send) : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default)
            where T : notnull =>
            Task.FromResult((T)send(request));
    }
}
