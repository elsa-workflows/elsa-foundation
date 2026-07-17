using System.Net;
using Elsa.Activities.Http.Models;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Deterministic write-amplification acceptance for spec 090. Each policy runs against its own Groundwork SQLite
/// database so persisted checkpoint-marker deltas cannot be contaminated by another test or policy lane.
/// Wall-clock latency is intentionally covered by the opt-in performance command rather than this CI test.
/// </summary>
public sealed class HttpEndpointRuntimePerformanceTests
{
    private const string BasePath = "/workflows/http/";
    private const string ResponseBody = "Hello World!";

    [Fact]
    public async Task CoalescedPolicy_FoldsToMandatoryValueFlowBoundaries_WithoutChangingTheResult()
    {
        var immediate = await ExecuteAsync(CheckpointPersistenceMode.Immediate);
        var coalesced = await ExecuteAsync(CheckpointPersistenceMode.Coalesced);

        Assert.Equal(HttpStatusCode.OK, immediate.StatusCode);
        Assert.Equal(ResponseBody, immediate.Body);
        Assert.Equal("text/plain", immediate.ContentType);
        Assert.Equal(WorkflowExecutionStatus.Completed, immediate.WorkflowStatus);
        Assert.Equal((int)HttpStatusCode.OK, immediate.ArtifactStatusCode);
        Assert.Equal(ResponseBody, immediate.ArtifactBody);
        Assert.Equal("text/plain", immediate.ArtifactContentType);

        Assert.Equal(immediate.StatusCode, coalesced.StatusCode);
        Assert.Equal(immediate.Body, coalesced.Body);
        Assert.Equal(immediate.ContentType, coalesced.ContentType);
        Assert.Equal(immediate.WorkflowStatus, coalesced.WorkflowStatus);
        Assert.Equal(immediate.ArtifactStatusCode, coalesced.ArtifactStatusCode);
        Assert.Equal(immediate.ArtifactBody, coalesced.ArtifactBody);
        Assert.Equal(immediate.ArtifactContentType, coalesced.ArtifactContentType);

        Assert.True(immediate.PhysicalCommitCount > coalesced.PhysicalCommitCount,
            $"Expected Immediate to persist more checkpoint commits, but observed {immediate.PhysicalCommitCount} and {coalesced.PhysicalCommitCount}.");
        // Spec 095 adds one non-negotiable pre-activation durability boundary per transient CLR activity. This
        // workflow activates five activities and then commits the terminal workflow state, so six physical writes are
        // the safe minimum; all replayable checkpoints between those boundaries must still fold away.
        Assert.Equal(6, coalesced.PhysicalCommitCount);
        Assert.True(coalesced.PhysicalCommitCount * 3 <= immediate.PhysicalCommitCount,
            $"Expected at least a two-thirds reduction after mandatory value-flow boundaries, but Immediate persisted {immediate.PhysicalCommitCount} commits and Coalesced persisted {coalesced.PhysicalCommitCount}.");
    }

    private static async Task<ExecutionResult> ExecuteAsync(CheckpointPersistenceMode mode)
    {
        await using var fixture = await HttpEndpointHostFixture.StartGroundworkSqliteAsync(
            checkpointPersistenceMode: mode,
            maxSegmentCheckpoints: 50);

        var path = $"performance/{mode.ToString().ToLowerInvariant()}";
        await fixture.PublishSyncEndpointWithWriteResponseWorkflowAsync(
            artifactId: $"artifact-performance-{mode.ToString().ToLowerInvariant()}",
            path: path,
            method: "POST",
            resultValueId: "request",
            statusCode: (int)HttpStatusCode.OK,
            body: ResponseBody,
            contentType: "text/plain");

        var commitsBeforeRequest = await fixture.CountPhysicalCheckpointCommitsAsync();
        using var requestContent = new StringContent("{\"measurement\":true}", System.Text.Encoding.UTF8, "application/json");
        var response = await fixture.Client.PostAsync($"{BasePath}{path}", requestContent);
        var workflow = await fixture.SingleWorkflowExecutionAsync();
        var artifact = await fixture.ReadHttpResponseArtifactAsync(workflow.WorkflowExecutionId);
        var commitsAfterRequest = await fixture.CountPhysicalCheckpointCommitsAsync();

        return new ExecutionResult(
            response.StatusCode,
            await response.Content.ReadAsStringAsync(),
            response.Content.Headers.ContentType?.MediaType,
            workflow.Status,
            artifact.GetProperty("statusCode").GetInt32(),
            artifact.GetProperty("body").GetString(),
            artifact.GetProperty("contentType").GetString(),
            commitsAfterRequest - commitsBeforeRequest);
    }

    private sealed record ExecutionResult(
        HttpStatusCode StatusCode,
        string Body,
        string? ContentType,
        WorkflowExecutionStatus WorkflowStatus,
        int ArtifactStatusCode,
        string? ArtifactBody,
        string? ArtifactContentType,
        int PhysicalCommitCount);
}
