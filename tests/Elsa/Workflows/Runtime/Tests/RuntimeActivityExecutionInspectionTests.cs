using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Endpoints;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeActivityExecutionInspectionTests
{
    [Fact]
    public async Task InMemoryStore_Lists_Summaries_In_Deterministic_Execution_Order()
    {
        var store = new InMemoryActivityExecutionInspectionStore();
        await store.SaveAsync(Projection("wf-1", "ae-2", "authored-a", sequence: 2));
        await store.SaveAsync(Projection("wf-1", "ae-1", "authored-a", sequence: 1));

        var result = await store.ListSummariesAsync("wf-1");

        Assert.Collection(
            result,
            projection => Assert.Equal("ae-1", projection.ActivityExecutionId),
            projection => Assert.Equal("ae-2", projection.ActivityExecutionId));
    }

    [Fact]
    public async Task InMemoryStore_Lists_Lightweight_Summaries_With_Evidence_Counts()
    {
        var store = new InMemoryActivityExecutionInspectionStore();
        await store.SaveAsync(Projection("wf-1", "ae-1", "authored-a", sequence: 1) with
        {
            ValueSnapshots =
            [
                new ActivityExecutionInspectionValueSnapshot(
                    "Input",
                    ActivityExecutionInspectionValueSubject.ActivityInput,
                    RuntimePayloadCaptureMode.Payload,
                    Type: null,
                    DateTimeOffset.UnixEpoch,
                    Payload: JsonSerializer.SerializeToElement("large-payload"),
                    "test",
                    IsSensitive: false,
                    Metadata: new Dictionary<string, string>())
            ]
        });

        var summary = Assert.Single(await store.ListSummariesAsync("wf-1"));

        Assert.Equal("ae-1", summary.ActivityExecutionId);
        Assert.Equal(1, summary.ValueSnapshotCount);
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

    [Theory]
    [InlineData(ActivityExecutionStatus.Cancelled, RuntimeCheckpointNames.ActivityCancelled)]
    [InlineData(ActivityExecutionStatus.Recovered, RuntimeCheckpointNames.ActivityRecovered)]
    public async Task CheckpointHandler_Projects_Terminal_ActivityExecutionInspection(
        ActivityExecutionStatus status,
        string checkpointName)
    {
        var stateStore = new InMemoryActivityExecutionStateStore();
        var inspectionStore = new InMemoryActivityExecutionInspectionStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointWriter(null, stateStore, null, null, null, null, null, inspectionStore);
        var terminalState = NewStateForStatus(status);
        await stateStore.SaveAsync(terminalState);
        var handler = new WorkflowCheckpointSchedulerWorkHandler(
            stateStore,
            new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                checkpointWriter,
                new NoopRuntimePostCommitIntentDispatcher()),
            new RuntimeActivityExecutionInspectionAccumulator(inspectionStore),
            TimeProvider.System);
        var workItem = NewCheckpointWorkItem(terminalState.Execution.ActivityExecutionId, checkpointName);

        await handler.HandleAsync(workItem);

        var projection = await inspectionStore.FindAsync("wf-1", terminalState.Execution.ActivityExecutionId);
        Assert.NotNull(projection);
        Assert.Equal(status, projection.Status);
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

    [Fact]
    public async Task Accumulator_Merges_Existing_Projection()
    {
        var store = new InMemoryActivityExecutionInspectionStore();
        var accumulator = new RuntimeActivityExecutionInspectionAccumulator(store);
        var initialState = NewStateForStatus(ActivityExecutionStatus.Scheduled);
        var runningState = initialState with
        {
            Status = ActivityExecutionStatus.Running,
            StartedAt = DateTimeOffset.UnixEpoch.AddMinutes(1)
        };
        await store.SaveAsync(ActivityExecutionInspectionProjection.FromState(
            initialState,
            checkpointId: "checkpoint-scheduled",
            committedAt: DateTimeOffset.UnixEpoch,
            metadata: new Dictionary<string, string> { ["initial"] = "kept" }));

        var projection = await accumulator.BuildProjectionAsync(
            runningState,
            checkpointId: "checkpoint-running",
            committedAt: DateTimeOffset.UnixEpoch.AddMinutes(2),
            metadata: new Dictionary<string, string> { ["updated"] = "merged" });

        Assert.Equal("checkpoint-scheduled", projection.FirstCheckpointId);
        Assert.Equal("checkpoint-running", projection.LastCheckpointId);
        Assert.Equal(ActivityExecutionStatus.Running, projection.Status);
        Assert.Equal("kept", projection.Metadata["initial"]);
        Assert.Equal("merged", projection.Metadata["updated"]);
    }

    [Theory]
    [InlineData(ActivityExecutionStatus.Cancelled)]
    [InlineData(ActivityExecutionStatus.Recovered)]
    public void Projection_Represents_Terminal_NonIncident_Statuses_When_Committed(ActivityExecutionStatus status)
    {
        var state = NewStateForStatus(status);

        var projection = ActivityExecutionInspectionProjection.FromState(state, "checkpoint-1", DateTimeOffset.UnixEpoch);
        var view = ActivityExecutionInspectionView.From(projection);

        Assert.Equal(status, projection.Status);
        Assert.Equal(status.ToString(), view.Status);
    }

    [Fact]
    public async Task GetActivityExecutionEndpoint_Returns_Ok_When_Projection_Exists()
    {
        var view = ActivityExecutionInspectionView.From(Projection("wf-1", "ae-1", "authored-a", sequence: 1));
        var endpoint = CreateEndpoint(new StubRequestSender((_, _) => Task.FromResult(new GetActivityExecutionResponse(view))));

        await endpoint.HandleAsync(new GetActivityExecution("wf-1", "ae-1"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, endpoint.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task GetActivityExecutionEndpoint_Returns_NotFound_When_Projection_Is_Missing()
    {
        var endpoint = CreateEndpoint(new StubRequestSender((_, _) => Task.FromResult(new GetActivityExecutionResponse(null))));

        await endpoint.HandleAsync(new GetActivityExecution("wf-1", "ae-missing"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, endpoint.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task GetActivityExecutionEndpoint_Throws_BadRequest_For_Invalid_Request()
    {
        var endpoint = CreateEndpoint(new StubRequestSender((_, _) => throw new ArgumentException("Invalid activity execution lookup.")));

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(
            () => endpoint.HandleAsync(new GetActivityExecution("", "ae-1"), CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task GetActivityExecutionEndpoint_Throws_InternalServerError_For_Unexpected_Failure()
    {
        var endpoint = CreateEndpoint(new StubRequestSender((_, _) => throw new InvalidOperationException("Storage failure.")));

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(
            () => endpoint.HandleAsync(new GetActivityExecution("wf-1", "ae-1"), CancellationToken.None));

        Assert.Equal(StatusCodes.Status500InternalServerError, exception.StatusCode);
    }

    [Fact]
    public async Task GetActivityExecutionEndpoint_Rethrows_Cancellation()
    {
        var endpoint = CreateEndpoint(new StubRequestSender((_, _) => throw new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(() => endpoint.HandleAsync(new GetActivityExecution("wf-1", "ae-1"), CancellationToken.None));
    }

    private static GetActivityExecutionEndpoint CreateEndpoint(IRequestSender requestSender) =>
        Factory.Create<GetActivityExecutionEndpoint>(
            context => context.Response.Body = new MemoryStream(),
            requestSender,
            NullLogger<GetActivityExecutionEndpoint>.Instance);

    private sealed class StubRequestSender(Func<GetActivityExecution, CancellationToken, Task<GetActivityExecutionResponse>> send) : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default)
            where T : notnull
        {
            if (request is GetActivityExecution getActivityExecution && typeof(T) == typeof(GetActivityExecutionResponse))
                return (Task<T>)(object)send(getActivityExecution, cancellationToken);

            throw new InvalidOperationException($"Unexpected request type '{request.GetType()}'.");
        }
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

    private static ActivityExecutionState NewStateForStatus(ActivityExecutionStatus status) =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: $"actexec-{status.ToString().ToLowerInvariant()}",
                WorkflowExecutionId: "wf-1",
                ExecutableNodeId: "node-a",
                AuthoredActivityId: "authored-a",
                ActivityType: "Elsa.Test",
                ActivityTypeVersion: "1.0.0"),
            Status: status,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: DateTimeOffset.UnixEpoch,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.From(
                "wf-1",
                parentActivityExecutionId: null,
                schedulingActivityExecutionId: null,
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static RuntimeSchedulerWorkItem NewCheckpointWorkItem(
        string activityExecutionId,
        string checkpointName = RuntimeCheckpointNames.ActivityRecovered) =>
        new(
            workItemId: "checkpoint-work",
            workflowExecutionId: "wf-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            envelopeId: "envelope-1",
            idempotencyKey: "wf-1:checkpoint:activity-recovered",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 10,
            payload: JsonSerializer.SerializeToElement(new RuntimeCheckpointCommandPayload(
                NewIdentity(),
                checkpointName,
                [activityExecutionId],
                RuntimeCheckpointCommandPayload.ActivityCompletionPropagationReason)),
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
}
