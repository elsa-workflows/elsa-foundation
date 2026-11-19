using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Endpoints.Instances.VariableLifecycle;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests.VariableLifecycle;

/// <summary>
/// Spec 1229 contract (inspection-api.md): the routes answer a refused comparison with a 409 problem whose code is the
/// reason, a missing instance or entry with 404, and a payload read without the payload permission with 403.
/// </summary>
public sealed class VariableLifecycleEndpointTests
{
    [Fact]
    public async Task A_refused_comparison_is_a_409_problem_with_the_reason_as_its_code()
    {
        var endpoint = new CompareEndpoint(new Fixed(compare: new InstanceComparisonResult(InstanceComparisonOutcome.DifferentExecutable,
            new InstanceComparisonView("a", "b", "x", false, "differentExecutable", null, [], false))));

        var signal = await Assert.ThrowsAnyAsync<Exception>(() => endpoint.HandleAsync(new CompareWorkflowInstances("a", "b"), default));
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        Assert.True(await new WorkflowsRuntimeFaultRenderer().TryWriteAsync(context, signal));

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("differentExecutable", problem.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(VariableWritePayloadOutcome.Denied, StatusCodes.Status403Forbidden)]
    [InlineData(VariableWritePayloadOutcome.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(VariableWritePayloadOutcome.Unavailable, StatusCodes.Status409Conflict)]
    public async Task A_payload_read_that_releases_nothing_answers_with_its_problem(VariableWritePayloadOutcome outcome, int status)
    {
        var endpoint = new GetVariableWritePayloadEndpoint(new Fixed(payload: new VariableWritePayloadResult(outcome, null)));

        var signal = await Assert.ThrowsAnyAsync<Exception>(() => endpoint.HandleAsync(new GetVariableWritePayload("a", "entry"), default));
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        Assert.True(await new WorkflowsRuntimeFaultRenderer().TryWriteAsync(context, signal));

        Assert.Equal(status, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_missing_instance_is_a_404()
    {
        var endpoint = new ListVariableWritesEndpoint(new Fixed());

        var signal = await Assert.ThrowsAnyAsync<Exception>(() => endpoint.HandleAsync(new ListVariableWrites("missing"), default));
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        Assert.True(await new WorkflowsRuntimeFaultRenderer().TryWriteAsync(context, signal));

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    private sealed class Fixed(InstanceComparisonResult? compare = null, VariableWritePayloadResult? payload = null) : IVariableLifecycleInspectionService
    {
        public Task<IReadOnlyList<VariableSummaryView>?> ListVariablesAsync(string workflowExecutionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VariableSummaryView>?>(null);

        public Task<VariableWriteHistoryPageView?> ListWritesAsync(VariableWriteHistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<VariableWriteHistoryPageView?>(null);

        public Task<JournalPageView?> ListJournalAsync(string workflowExecutionId, string? cursor, int? take, CancellationToken cancellationToken) =>
            Task.FromResult<JournalPageView?>(null);

        public Task<InstanceComparisonResult> CompareAsync(string workflowExecutionId, string otherWorkflowExecutionId, CancellationToken cancellationToken) =>
            Task.FromResult(compare ?? InstanceComparisonResult.NotFound);

        public Task<VariableWritePayloadResult> ReadPayloadAsync(string workflowExecutionId, string entryId, string? value, CancellationToken cancellationToken) =>
            Task.FromResult(payload ?? VariableWritePayloadResult.NotFound);
    }
}
