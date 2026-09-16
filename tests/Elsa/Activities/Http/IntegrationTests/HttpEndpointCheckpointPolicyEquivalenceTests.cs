using System.Net;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// A synchronous HTTP endpoint workflow over an isolated EF Core SQLite database returns the same live response,
/// completes, and records the same durable <c>HttpResponseInstruction</c> artifact under every checkpoint persistence
/// policy. Split from the spec 090 write-amplification acceptance when performance measurement was retired by owner
/// decision (#1668, ADR 0073): physical checkpoint-commit counts and their comparison are no longer asserted. The
/// mandatory durability boundaries that bounded those counts are covered by <c>RuntimeCheckpointCoalescingPolicyTests</c>.
/// </summary>
public sealed class HttpEndpointCheckpointPolicyEquivalenceTests
{
    private const string BasePath = "/workflows/http/";
    private const string ResponseBody = "Hello World!";
    private const string ContentType = "text/plain";

    private static readonly ExecutionResult Expected = new(
        HttpStatusCode.OK,
        ResponseBody,
        ContentType,
        WorkflowExecutionStatus.Completed,
        (int)HttpStatusCode.OK,
        ResponseBody,
        ContentType);

    [Theory]
    [InlineData(CheckpointPersistenceMode.Immediate)]
    [InlineData(CheckpointPersistenceMode.Coalesced)]
    public async Task Sync_endpoint_returns_completes_and_persists_the_same_result_under_each_checkpoint_policy(
        CheckpointPersistenceMode mode)
    {
        await using var fixture = await HttpEndpointHostFixture.StartDurableSqliteAsync(
            checkpointPersistenceMode: mode,
            maxSegmentCheckpoints: 50);
        var policy = mode.ToString().ToLowerInvariant();
        var path = $"checkpoint-policy/{policy}";
        await fixture.PublishSyncEndpointWithWriteResponseWorkflowAsync(
            artifactId: $"artifact-checkpoint-policy-{policy}",
            path: path,
            method: "POST",
            resultValueId: "request",
            statusCode: (int)HttpStatusCode.OK,
            body: ResponseBody,
            contentType: ContentType);

        using var requestContent = new StringContent($$"""{"checkpointPolicy":"{{policy}}"}""", System.Text.Encoding.UTF8, "application/json");
        var response = await fixture.Client.PostAsync($"{BasePath}{path}", requestContent);
        var workflow = await fixture.SingleWorkflowExecutionAsync();
        var artifact = await fixture.ReadHttpResponseArtifactAsync(workflow.WorkflowExecutionId);

        Assert.Equal(Expected, new ExecutionResult(
            response.StatusCode,
            await response.Content.ReadAsStringAsync(),
            response.Content.Headers.ContentType?.MediaType,
            workflow.Status,
            artifact.GetProperty("statusCode").GetInt32(),
            artifact.GetProperty("body").GetString(),
            artifact.GetProperty("contentType").GetString()));
    }

    private sealed record ExecutionResult(
        HttpStatusCode StatusCode,
        string Body,
        string? ContentType,
        WorkflowExecutionStatus WorkflowStatus,
        int ArtifactStatusCode,
        string? ArtifactBody,
        string? ArtifactContentType);
}
