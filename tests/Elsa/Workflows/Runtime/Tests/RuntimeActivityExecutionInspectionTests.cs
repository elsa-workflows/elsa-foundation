using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeActivityExecutionInspectionTests
{
    [Fact]
    public async Task InMemoryStore_Lists_Projection_In_Deterministic_Execution_Order()
    {
        var store = new InMemoryActivityExecutionInspectionStore();
        await store.SaveAsync(Projection("wf-1", "ae-2", "authored-a", sequence: 2));
        await store.SaveAsync(Projection("wf-1", "ae-1", "authored-a", sequence: 1));

        var result = await store.ListByAuthoredActivityIdAsync("wf-1", "authored-a");

        Assert.Collection(
            result,
            projection => Assert.Equal("ae-1", projection.ActivityExecutionId),
            projection => Assert.Equal("ae-2", projection.ActivityExecutionId));
    }

    [Fact]
    public async Task CheckpointWriter_Projects_ActivityExecutionInspection_Lane()
    {
        var store = new InMemoryActivityExecutionInspectionStore();
        var writer = new InMemoryRuntimeCheckpointWriter(null, null, null, null, null, null, null, store);
        var projection = Projection("wf-1", "ae-1", "authored-a", sequence: 1);
        var commit = new RuntimeCheckpointCommit(
            CommitId: "commit-1",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint-1",
                Name: "ActivityStarted",
                WorkflowExecutionId: "wf-1",
                OccurredAt: DateTimeOffset.UnixEpoch,
                ActivityExecutionIds: ["ae-1"],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: "ae-1",
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: projection,
                        Metadata: new Dictionary<string, string>())
                ]),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

        await writer.WriteAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.NotNull(await store.FindAsync("wf-1", "ae-1"));
    }

    [Fact]
    public async Task GetActivityExecutionRequestHandler_Returns_Committed_Projection()
    {
        var store = new InMemoryActivityExecutionInspectionStore();
        await store.SaveAsync(Projection("wf-1", "ae-1", "authored-a", sequence: 1));
        IRequestHandler<GetActivityExecution, GetActivityExecutionResponse> handler = new GetActivityExecutionRequestHandler(store);

        var response = await handler.Handle(new GetActivityExecution("wf-1", "ae-1"), CancellationToken.None);

        Assert.NotNull(response.ActivityExecution);
        Assert.Equal("ae-1", response.ActivityExecution.ActivityExecutionId);
    }

    [Fact]
    public async Task GetActivityExecutionRequestHandler_Returns_Null_When_Projection_Is_Missing()
    {
        IRequestHandler<GetActivityExecution, GetActivityExecutionResponse> handler = new GetActivityExecutionRequestHandler(new InMemoryActivityExecutionInspectionStore());

        var response = await handler.Handle(new GetActivityExecution("wf-1", "ae-missing"), CancellationToken.None);

        Assert.Null(response.ActivityExecution);
    }

    private static ActivityExecutionInspectionProjection Projection(
        string workflowExecutionId,
        string activityExecutionId,
        string authoredActivityId,
        long sequence) =>
        new(
            ActivityExecutionId: activityExecutionId,
            WorkflowExecutionId: workflowExecutionId,
            ExecutableNodeId: $"node-{activityExecutionId}",
            AuthoredActivityId: authoredActivityId,
            ActivityType: "Elsa.Test",
            ActivityTypeVersion: "1.0.0",
            Status: ActivityExecutionStatus.Completed,
            SubStatus: null,
            ExecutionSequence: sequence,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: DateTimeOffset.UnixEpoch,
            FirstCheckpointId: "checkpoint-first",
            LastCheckpointId: "checkpoint-last",
            LastCommittedAt: DateTimeOffset.UnixEpoch,
            Provenance: ActivitySchedulingProvenance.From(
                workflowExecutionId,
                parentActivityExecutionId: null,
                schedulingActivityExecutionId: null,
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            OutcomeNames: ["Done"],
            Bookmarks: [],
            Incidents: [],
            ValueSnapshots: [],
            Metadata: new Dictionary<string, string>());
}
