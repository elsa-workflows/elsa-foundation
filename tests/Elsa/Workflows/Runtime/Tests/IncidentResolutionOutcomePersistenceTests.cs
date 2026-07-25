using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class IncidentResolutionOutcomePersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Direct_save_preserves_a_committed_outcome_and_allows_only_an_identical_replay()
    {
        var store = new InMemoryIncidentStateStore();
        var committed = Incident(Outcome(IncidentResolutionActionKinds.FaultWorkflow));

        await store.SaveAsync(committed);
        await store.SaveAsync(Incident(Outcome(IncidentResolutionActionKinds.FaultWorkflow)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(Incident(outcome: null)).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Incident(Outcome("Contoso.ReplacedOutcome"))).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Incident(Outcome(IncidentResolutionActionKinds.FaultWorkflow), IncidentStatus.Open)).AsTask());

        var persisted = await store.FindAsync("wf-1", "incident-1");
        Assert.NotNull(persisted);
        Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, persisted.ResolutionOutcome!.ActionKind);
        Assert.Equal(IncidentStatus.Blocking, persisted.Status);
    }

    [Fact]
    public async Task Direct_save_cannot_change_the_resolution_time_of_a_committed_outcome()
    {
        var store = new InMemoryIncidentStateStore();
        var outcome = Outcome("Contoso.Resolve");
        await store.SaveAsync(Incident(outcome, IncidentStatus.Resolved, Now.AddMinutes(2)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Incident(outcome, IncidentStatus.Resolved, Now.AddMinutes(3))).AsTask());

        Assert.Equal(Now.AddMinutes(2), (await store.FindAsync("wf-1", "incident-1"))!.ResolvedAt);
    }

    [Fact]
    public async Task Checkpoint_write_cannot_mutate_a_committed_outcome_or_its_lifecycle_effect()
    {
        var store = new InMemoryIncidentStateStore();
        await store.SaveAsync(Incident(Outcome(IncidentResolutionActionKinds.FaultWorkflow)));
        var writer = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: null,
            activityExecutionStateStore: null,
            bookmarkStateStore: null,
            durableValueStateStore: null,
            incidentStateStore: store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.CommitAsync(Commit("commit-clear", Incident(outcome: null)), Decision).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.CommitAsync(
                Commit("commit-open", Incident(Outcome(IncidentResolutionActionKinds.FaultWorkflow), IncidentStatus.Open)),
                Decision).AsTask());

        Assert.Empty(writer.ListCommits());
        Assert.Equal(
            IncidentResolutionActionKinds.FaultWorkflow,
            (await store.FindAsync("wf-1", "incident-1"))!.ResolutionOutcome!.ActionKind);
    }

    private static readonly RuntimeCheckpointPersistenceDecision Decision =
        new(RuntimeCheckpointPersistenceMode.Immediate);

    private static RuntimeCheckpointCommit Commit(string commitId, IncidentState incident) =>
        new(
            commitId,
            new RuntimeCheckpoint(
                $"checkpoint:{commitId}",
                RuntimeCheckpointNames.IncidentResolutionBatchApplied,
                incident.WorkflowExecutionId,
                Now,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents:
                [
                    new RuntimeStateChange<IncidentState>(
                        incident.IncidentId,
                        RuntimeStateChangeOperation.Upsert,
                        incident,
                        new Dictionary<string, string>())
                ],
                operational: []),
            [],
            new Dictionary<string, string>());

    private static IncidentState Incident(
        IncidentResolutionOutcome? outcome,
        IncidentStatus status = IncidentStatus.Blocking,
        DateTimeOffset? resolvedAt = null) =>
        new(
            incidentId: "incident-1",
            workflowExecutionId: "wf-1",
            activityExecutionId: "activity-1",
            executableNodeId: "node-1",
            severity: IncidentSeverity.Error,
            status: status,
            resolutionOutcome: outcome,
            failureType: "ActivityFaulted",
            message: "Activity failed.",
            createdAt: Now,
            resolvedAt: resolvedAt,
            metadata: new Dictionary<string, string>());

    private static IncidentResolutionOutcome Outcome(string actionKind) =>
        new(
            actionKind,
            Now.AddMinutes(1),
            new IncidentStrategyReference("Fault", "1"),
            systemSource: null,
            metadata: new Dictionary<string, string> { ["phase"] = "Resolve" });
}
