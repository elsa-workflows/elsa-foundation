using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Testing;

internal static class WorkflowExecutionHistoryProviderConformance
{
    public static async Task VerifyAllFiltersAsync(
        IWorkflowExecutionStateStore store,
        DateTimeOffset timestamp)
    {
        var match = State("wf-match", timestamp) with
        {
            CorrelationId = "correlation-1",
            RunKind = WorkflowRunKind.PublishedRun
        };
        var nearMatches = new[]
        {
            match with { WorkflowExecutionId = "wf-no-explicit-tenant", TenantId = null },
            match with
            {
                WorkflowExecutionId = "wf-other-definition",
                PinnedExecutable = match.PinnedExecutable with { DefinitionId = "definition-2" }
            },
            match with { WorkflowExecutionId = "wf-other-status", Status = WorkflowExecutionStatus.Running },
            match with { WorkflowExecutionId = "wf-other-run-kind", RunKind = WorkflowRunKind.TestRun },
            WithTimestamp(match with { WorkflowExecutionId = "wf-before-window" }, timestamp.AddSeconds(-2)),
            WithTimestamp(match with { WorkflowExecutionId = "wf-after-window" }, timestamp.AddSeconds(2)),
            match with { WorkflowExecutionId = "wf-other-correlation", CorrelationId = "correlation-2" },
            match with { WorkflowExecutionId = "wf-other-id" },
            match with
            {
                WorkflowExecutionId = "wf-other-artifact",
                PinnedExecutable = match.PinnedExecutable with { ArtifactId = "artifact-2" }
            }
        };
        await store.SaveAsync(match);
        foreach (var state in nearMatches)
            await store.SaveAsync(state);

        var cases = new (string RejectedId, WorkflowExecutionStatePageQuery Query)[]
        {
            ("wf-no-explicit-tenant", new(PageSize: 20, TenantId: "tenant-1")),
            ("wf-other-definition", new(PageSize: 20, DefinitionId: "definition-1")),
            ("wf-other-status", new(PageSize: 20, Status: WorkflowExecutionStatus.Completed)),
            ("wf-other-run-kind", new(PageSize: 20, RunKind: WorkflowRunKind.PublishedRun)),
            ("wf-before-window", new(PageSize: 20, From: timestamp.AddSeconds(-1))),
            ("wf-after-window", new(PageSize: 20, To: timestamp.AddSeconds(1))),
            ("wf-other-correlation", new(PageSize: 20, CorrelationId: "correlation-1")),
            ("wf-other-id", new(PageSize: 20, WorkflowExecutionId: "wf-match")),
            ("wf-other-artifact", new(PageSize: 20, ArtifactId: "artifact-1"))
        };

        foreach (var (rejectedId, query) in cases)
        {
            var page = await store.QueryPageAsync(query);
            Assert.Contains(page.Items, item => item.WorkflowExecutionId == "wf-match");
            Assert.DoesNotContain(page.Items, item => item.WorkflowExecutionId == rejectedId);
        }
    }

    private static WorkflowExecutionState State(string id, DateTimeOffset timestamp) => new(
        id,
        new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "hash-1"),
        WorkflowExecutionStatus.Completed,
        null,
        timestamp.AddMinutes(-1),
        timestamp.AddMinutes(-1),
        timestamp,
        timestamp,
        null,
        null,
        "tenant-1",
        new Dictionary<string, string>());

    private static WorkflowExecutionState WithTimestamp(WorkflowExecutionState state, DateTimeOffset timestamp) => state with
    {
        CreatedAt = timestamp.AddMinutes(-1),
        StartedAt = timestamp.AddMinutes(-1),
        CompletedAt = timestamp,
        UpdatedAt = timestamp
    };
}
