using System.Net;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Spec 238 end to end: a published Flowchart with two start-trigger HttpEndpoints is indexed with both bindings, and a
/// request to either path starts the run at that endpoint. The endpoint that did not fire never runs, so it leaves no
/// bookmark and the run completes instead of parking at the Flowchart's start node.
/// </summary>
public sealed class MultiTriggerFlowchartEndToEndTests : IAsyncLifetime
{
    private const string ArtifactId = "artifact-multi-trigger-flowchart";
    private const string FirstPath = "orders/created";
    private const string SecondPath = "invoices/paid";

    private HttpEndpointHostFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = await HttpEndpointHostFixture.StartAsync();
        await _fixture.PublishTwoHttpEndpointFlowchartWorkflowAsync(ArtifactId, FirstPath, SecondPath, "POST");
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task SecondEndpointRequest_StartsAtSecondEndpoint_CompletesWithoutBookmarks()
    {
        Assert.Equal(2, (await _fixture.ListTriggerBindingsAsync(ArtifactId)).Count);

        var workflowExecutionId = await PostAsync(SecondPath);

        await AssertRanOnlyAsync(workflowExecutionId, ran: "node-second-endpoint", didNotRun: "node-first-endpoint");
    }

    [Fact]
    public async Task FirstEndpointRequest_StartsAtFirstEndpoint()
    {
        var workflowExecutionId = await PostAsync(FirstPath);

        await AssertRanOnlyAsync(workflowExecutionId, ran: "node-first-endpoint", didNotRun: "node-second-endpoint");
    }

    private async Task<string> PostAsync(string path)
    {
        var response = await _fixture.Client.PostAsync($"/workflows/http/{path}", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync();
        var payload = await JsonDocument.ParseAsync(stream);
        return Assert.Single(payload.RootElement.GetProperty("started").EnumerateArray()).GetString()!;
    }

    private async Task AssertRanOnlyAsync(string workflowExecutionId, string ran, string didNotRun)
    {
        Assert.Equal(WorkflowExecutionStatus.Completed, (await _fixture.WorkflowExecutionAsync(workflowExecutionId)).Status);

        var nodeIds = (await _fixture.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(workflowExecutionId))
            .Select(state => state.Execution.ExecutableNodeId)
            .ToArray();
        Assert.Contains(ran, nodeIds);
        Assert.DoesNotContain(didNotRun, nodeIds);
        Assert.Empty(await _fixture.ListBookmarksAsync(workflowExecutionId));
    }
}
