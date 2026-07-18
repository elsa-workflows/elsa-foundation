using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutionStateStorePagingContractTests
{
    private readonly IWorkflowExecutionStateStore _store = new InMemoryWorkflowExecutionStateStore();
    private readonly DateTimeOffset _timestamp = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task QueryPageAsync_keeps_keyset_pages_stable_when_newer_executions_are_inserted()
    {
        foreach (var id in new[] { "wf-d", "wf-b", "wf-a", "wf-c" })
            await _store.SaveAsync(State(id, _timestamp));

        var first = await _store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 2));
        await _store.SaveAsync(State("wf-0", _timestamp));
        await _store.SaveAsync(State("wf-new", _timestamp.AddMinutes(1)));
        var second = await _store.QueryPageAsync(new WorkflowExecutionStatePageQuery(PageSize: 2, Cursor: first.NextCursor));

        Assert.Equal(["wf-a", "wf-b"], first.Items.Select(x => x.WorkflowExecutionId));
        Assert.Equal(["wf-c", "wf-d"], second.Items.Select(x => x.WorkflowExecutionId));
        Assert.True(first.HasNext);
        Assert.False(second.HasNext);
        Assert.Null(second.NextCursor);
        Assert.Equal(6, second.TotalCount);
    }

    [Fact]
    public async Task QueryPageAsync_applies_the_complete_public_filter_contract()
    {
        var matching = State("wf-match", _timestamp) with
        {
            CorrelationId = "correlation-1",
            RunKind = WorkflowRunKind.PublishedRun
        };
        await _store.SaveAsync(matching);
        await _store.SaveAsync(State("wf-other", _timestamp.AddHours(-1)));

        var page = await _store.QueryPageAsync(new WorkflowExecutionStatePageQuery(
            PageSize: 10,
            TenantId: "tenant-1",
            DefinitionId: "definition-1",
            Status: WorkflowExecutionStatus.Completed,
            RunKind: WorkflowRunKind.PublishedRun,
            From: _timestamp.AddSeconds(-1),
            To: _timestamp.AddSeconds(1),
            CorrelationId: "correlation-1",
            WorkflowExecutionId: "wf-match",
            ArtifactId: "artifact-1"));

        Assert.Equal("wf-match", Assert.Single(page.Items).WorkflowExecutionId);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task QueryPageAsync_rejects_a_cursor_reused_with_different_filters()
    {
        await _store.SaveAsync(State("wf-1", _timestamp));
        await _store.SaveAsync(State("wf-2", _timestamp.AddMinutes(-1)));
        var firstQuery = new WorkflowExecutionStatePageQuery(PageSize: 1, TenantId: "tenant-1");
        var cursor = (await _store.QueryPageAsync(firstQuery)).NextCursor;

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _store.QueryPageAsync(new WorkflowExecutionStatePageQuery(
                PageSize: 1,
                TenantId: "tenant-2",
                Cursor: cursor)));

        Assert.Equal("cursor", exception.ParamName);
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
}
