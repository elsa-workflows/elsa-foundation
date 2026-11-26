using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class RescheduleActivityAlterationHandlerTests
{
    [Theory]
    [InlineData(ActivityExecutionStatus.Running)]
    [InlineData(ActivityExecutionStatus.Completed)]
    [InlineData(ActivityExecutionStatus.Cancelled)]
    [InlineData(ActivityExecutionStatus.Recovered)]
    public async Task PreflightAsync_RejectsIneligibleSourceStatuses(ActivityExecutionStatus status)
    {
        var source = Source(status);
        var handler = Handler(new InMemoryWorkflowExecutableStore());
        var staging = new Staging();

        var result = await handler.PreflightAsync(Context(source, staging));

        Assert.False(result.IsAccepted);
        Assert.Equal("SourceActivityNotReschedulable", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Theory]
    [InlineData(ActivityExecutionStatus.Scheduled)]
    [InlineData(ActivityExecutionStatus.Waiting)]
    [InlineData(ActivityExecutionStatus.Suspended)]
    [InlineData(ActivityExecutionStatus.Faulted)]
    public async Task PreflightAsync_AcceptsEachEligibleSourceStatus(ActivityExecutionStatus status)
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(Executable());
        var staging = new Staging();

        var result = await Handler(executableStore).PreflightAsync(Context(Source(status), staging));

        Assert.True(result.IsAccepted);
        var change = Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges));
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(Workflow());
        change.Apply(builder);
        Assert.Contains(builder.ActivityExecutions, candidate => candidate.State.Status == ActivityExecutionStatus.Superseded);
        Assert.Contains(builder.ActivityExecutions, candidate => candidate.State.Status == ActivityExecutionStatus.Scheduled);
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, Assert.Single(builder.PostCommitIntents).MaterializedSchedulerWorkItem!.CommandKind);
    }

    [Fact]
    public async Task PreflightAsync_SupersedesSource_CleansSubtree_AndPreservesPinnedInputs()
    {
        var input = new ActivityInputSnapshot("source", "contract", "binding", new Dictionary<string, ValueEnvelope>(), DateTimeOffset.UnixEpoch);
        var source = Source(ActivityExecutionStatus.Faulted) with { InputSnapshot = input };
        var descendant = Source(ActivityExecutionStatus.Waiting, "descendant") with
        {
            ParentActivityExecutionId = source.Execution.ActivityExecutionId,
            ExecutionScopeId = "derived-descendant-scope"
        };
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(Executable());
        var handler = Handler(executableStore);
        var staging = new Staging();

        var result = await handler.PreflightAsync(Context(source, staging, descendant));

        Assert.True(result.IsAccepted);
        var change = Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges));
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(Workflow());
        change.Apply(builder);

        var superseded = Assert.Single(builder.ActivityExecutions, change => change.StateId == source.Execution.ActivityExecutionId).State;
        var successor = Assert.Single(builder.ActivityExecutions, change => change.StateId != source.Execution.ActivityExecutionId && change.State.Status == ActivityExecutionStatus.Running).State;
        var cancelledDescendant = Assert.Single(builder.ActivityExecutions, change => change.StateId == descendant.Execution.ActivityExecutionId).State;
        Assert.Equal(ActivityExecutionStatus.Superseded, superseded.Status);
        Assert.Equal(successor.Execution.ActivityExecutionId, superseded.SupersededByActivityExecutionId);
        Assert.NotNull(superseded.SupersededAt);
        Assert.Equal(source.Execution.ActivityExecutionId, successor.Metadata["runtime.supersedesActivityExecutionId"]);
        Assert.NotSame(input, successor.InputSnapshot);
        Assert.Equal(successor.InvocationId, successor.InputSnapshot!.InvocationId);
        Assert.Equal("source", input.InvocationId);
        Assert.Equal(input.ContractFingerprint, successor.InputSnapshot.ContractFingerprint);
        Assert.Equal(input.BindingFingerprint, successor.InputSnapshot.BindingFingerprint);
        Assert.Equal(input.MaterializedAt, successor.InputSnapshot.MaterializedAt);
        Assert.Equal(input.Values, successor.InputSnapshot.Values);
        Assert.Equal(ActivityExecutionStatus.Cancelled, cancelledDescendant.Status);
        Assert.Equal("SupersededAncestor", cancelledDescendant.SubStatus);
        var cleanup = Assert.Single(builder.ActivityScopeCleanups);
        Assert.Equal(source.Execution.ActivityExecutionId, cleanup.ExecutionScopeId);
        Assert.Contains("derived-descendant-scope", cleanup.ActivityExecutionIds);
        Assert.Equal(WorkflowExecutionCommandKind.StartActivity, Assert.Single(builder.PostCommitIntents).MaterializedSchedulerWorkItem!.CommandKind);
    }

    [Fact]
    public async Task PreflightAsync_SuccessorDropsTheSourcesPerInvocationMetadata()
    {
        // Named explicitly rather than read from the list under test, so a key dropped from that list fails here.
        string[] perInvocationKeys =
        [
            RuntimeMetadataKeys.ActivityAttemptActivationClaim,
            RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId,
            RuntimeMetadataKeys.ActivityActivationCompletedWorkItemId,
            RuntimeMetadataKeys.ActivityAttemptOrdinalHighWatermark,
            RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId,
            RuntimeMetadataKeys.ParentOutcomeWorkItemId,
            RuntimeMetadataKeys.DescendantFaultProcessedWorkItemId,
            RuntimeMetadataKeys.IncidentId,
            RuntimeMetadataKeys.FaultSubStatus,
            RuntimeMetadataKeys.FaultType,
            RuntimeMetadataKeys.FaultMessage,
            RuntimeMetadataKeys.FaultStackTrace,
            RuntimeMetadataKeys.FaultInnerType,
            RuntimeMetadataKeys.FaultInnerMessage,
            RuntimeMetadataKeys.CausalIncidentId,
            RuntimeMetadataKeys.CausalActivityExecutionId,
            RuntimeMetadataKeys.CausalExecutableNodeId,
            RuntimeMetadataKeys.CausationKind,
            RuntimeMetadataKeys.BackgroundActivityWorkId
        ];
        // A source an ancestor already received the fault of is refused outright, so that marker is left out here.
        var metadata = perInvocationKeys
            .Where(key => key != RuntimeMetadataKeys.DescendantFaultProcessedWorkItemId)
            .ToDictionary(key => key, _ => "source-value", StringComparer.Ordinal);
        metadata["custom.kept"] = "kept";
        // The parent already evaluated the source's fault, so it must be running and accept a rescheduled child.
        var source = Source(ActivityExecutionStatus.Faulted) with { Metadata = metadata, ParentActivityExecutionId = "parent" };
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(ExecutableWithParent(acceptsRescheduledFaultedChild: true));
        var staging = new Staging();

        var result = await Handler(executableStore).PreflightAsync(Context(source, staging, Parent(ActivityExecutionStatus.Running)));

        Assert.True(result.IsAccepted);
        var builder = Apply(Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges)));
        var successor = Assert.Single(builder.ActivityExecutions, change => change.StateId != source.Execution.ActivityExecutionId).State;
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["custom.kept"] = "kept",
                [RuntimeMetadataKeys.SupersedesActivityExecutionId] = source.Execution.ActivityExecutionId
            },
            successor.Metadata);
        // The superseded source keeps its own record of what happened to it.
        var superseded = Assert.Single(builder.ActivityExecutions, change => change.StateId == source.Execution.ActivityExecutionId).State;
        Assert.Equal("source-value", superseded.Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId]);
    }

    [Fact]
    public async Task PreflightAsync_SuccessorCommandCarriesTheSchedulingMetadataItsParentGaveTheSource()
    {
        var source = Source(ActivityExecutionStatus.Waiting) with
        {
            ParentActivityExecutionId = "parent",
            Provenance = ActivitySchedulingProvenance.From("workflow", "parent", "parent", null, null, null, "source", "test",
                new Dictionary<string, string> { ["test.route"] = "step-1" })
        };
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(Executable());
        var staging = new Staging();

        var result = await Handler(executableStore).PreflightAsync(Context(source, staging));

        Assert.True(result.IsAccepted);
        var builder = Apply(Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges)));
        var successor = Assert.Single(builder.ActivityExecutions, change => change.StateId != source.Execution.ActivityExecutionId).State;
        Assert.Equal("step-1", successor.Provenance.Metadata["test.route"]);
        var commandMetadata = Assert.Single(builder.PostCommitIntents).MaterializedSchedulerWorkItem!.CommandMetadata;
        Assert.Equal("step-1", commandMetadata["test.route"]);
        Assert.Equal("parent", commandMetadata[RuntimeMetadataKeys.ParentActivityExecutionId]);
        Assert.Equal(source.Execution.ExecutableNodeId, commandMetadata[RuntimeMetadataKeys.ChildExecutableNodeId]);
        Assert.Equal("RescheduleActivity", commandMetadata["runtime.alteration.kind"]);
    }

    [Theory]
    [InlineData(true, ActivityExecutionStatus.Running, null)]
    [InlineData(false, ActivityExecutionStatus.Running, "ParentDoesNotAcceptRescheduledChild")]
    [InlineData(true, ActivityExecutionStatus.Cancelled, "SourceParentNotRunning")]
    public async Task PreflightAsync_ASourceWhoseFaultItsParentEvaluated_NeedsARunningParentThatAcceptsIt(
        bool acceptsRescheduledFaultedChild,
        ActivityExecutionStatus parentStatus,
        string? expectedCode)
    {
        var source = Source(ActivityExecutionStatus.Faulted) with
        {
            ParentActivityExecutionId = "parent",
            Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId] = "work-1" }
        };
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(ExecutableWithParent(acceptsRescheduledFaultedChild));
        var staging = new Staging();

        var result = await Handler(executableStore).PreflightAsync(Context(source, staging, Parent(parentStatus)));

        Assert.Equal(expectedCode is null, result.IsAccepted);
        Assert.Equal(expectedCode, result.Failure?.Code);
        if (expectedCode is not null)
        {
            Assert.Empty(staging.StagedChanges);
            return;
        }

        // Accepted: the successor carries no parent-completion marker, so its outcome reaches the parent.
        var builder = Apply(Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges)));
        var successor = Assert.Single(builder.ActivityExecutions,
            change => change.StateId != source.Execution.ActivityExecutionId && change.StateId != "parent").State;
        Assert.DoesNotContain(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId, successor.Metadata.Keys);
    }

    [Fact]
    public async Task PreflightAsync_RefusesASourceWhoseFaultAnAncestorAlreadyReceived()
    {
        // The direct parent is running and accepts it, but an ancestor past it already decided on the fault.
        var source = Source(ActivityExecutionStatus.Faulted) with
        {
            ParentActivityExecutionId = "parent",
            Metadata = new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId] = "work-1",
                [RuntimeMetadataKeys.DescendantFaultProcessedWorkItemId] = "work-2"
            }
        };
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(ExecutableWithParent(acceptsRescheduledFaultedChild: true));
        var staging = new Staging();

        var result = await Handler(executableStore).PreflightAsync(Context(source, staging, Parent(ActivityExecutionStatus.Running)));

        Assert.False(result.IsAccepted);
        Assert.Equal("SourceFaultHandledByAncestor", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RefusesAFaultedSourceWhoseParentFaultedOnItsFault()
    {
        // A parent whose fault evaluation faulted it writes no processed marker on the child.
        var source = Source(ActivityExecutionStatus.Faulted) with { ParentActivityExecutionId = "parent" };
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(ExecutableWithParent(acceptsRescheduledFaultedChild: true));
        var staging = new Staging();

        var result = await Handler(executableStore).PreflightAsync(Context(source, staging, Parent(ActivityExecutionStatus.Faulted)));

        Assert.False(result.IsAccepted);
        Assert.Equal("SourceParentNotRunning", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RefusesAFaultedSourceUnderAnAncestorThatIsNoLongerRunning()
    {
        // The parent is running and accepts it, but the scope above it faulted on the fault without cancelling it.
        var source = Source(ActivityExecutionStatus.Faulted) with
        {
            ParentActivityExecutionId = "parent",
            Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId] = "work-1" }
        };
        var parent = Parent(ActivityExecutionStatus.Running) with { ParentActivityExecutionId = "grandparent" };
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(ExecutableWithParent(acceptsRescheduledFaultedChild: true));
        var staging = new Staging();

        var result = await Handler(executableStore).PreflightAsync(
            Context(source, staging, parent, Source(ActivityExecutionStatus.Faulted, "grandparent")));

        Assert.False(result.IsAccepted);
        Assert.Equal("SourceAncestorNotRunning", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_AcceptsAFaultedSourceWhenItsParentAndEveryAncestorAreRunning()
    {
        var source = Source(ActivityExecutionStatus.Faulted) with
        {
            ParentActivityExecutionId = "parent",
            Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId] = "work-1" }
        };
        var parent = Parent(ActivityExecutionStatus.Running) with { ParentActivityExecutionId = "grandparent" };
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(ExecutableWithParent(acceptsRescheduledFaultedChild: true));
        var staging = new Staging();

        var result = await Handler(executableStore).PreflightAsync(
            Context(source, staging, parent, Source(ActivityExecutionStatus.Running, "grandparent")));

        Assert.True(result.IsAccepted);
        Assert.Single(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_KeepsIntrinsicSuccessorScheduledEvenWhenInputsArePinned()
    {
        var source = Source(ActivityExecutionStatus.Faulted) with
        {
            InputSnapshot = new ActivityInputSnapshot("source", "contract", "binding", new Dictionary<string, ValueEnvelope>(), DateTimeOffset.UnixEpoch)
        };
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(Executable(intrinsic: true));
        var staging = new Staging();

        var result = await Handler(executableStore).PreflightAsync(Context(source, staging));

        Assert.True(result.IsAccepted);
        var change = Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges));
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(Workflow());
        change.Apply(builder);
        var successor = Assert.Single(builder.ActivityExecutions, state => state.StateId != source.Execution.ActivityExecutionId && state.State.Status != ActivityExecutionStatus.Superseded).State;
        Assert.Equal(ActivityExecutionStatus.Scheduled, successor.Status);
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, Assert.Single(builder.PostCommitIntents).MaterializedSchedulerWorkItem!.CommandKind);
    }

    [Fact]
    public async Task PreflightAsync_ReplayDerivesTheSameSuccessorAndContinuationIdentity()
    {
        var source = Source(ActivityExecutionStatus.Scheduled);
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(Executable());
        var handler = Handler(executableStore);
        var firstStaging = new Staging();
        var replayStaging = new Staging();

        Assert.True((await handler.PreflightAsync(Context(source, firstStaging))).IsAccepted);
        Assert.True((await handler.PreflightAsync(Context(source, replayStaging))).IsAccepted);
        var first = Apply(Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(firstStaging.StagedChanges)));
        var replay = Apply(Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(replayStaging.StagedChanges)));

        Assert.Equal(
            Assert.Single(first.ActivityExecutions, state => state.State.Status == ActivityExecutionStatus.Scheduled).StateId,
            Assert.Single(replay.ActivityExecutions, state => state.State.Status == ActivityExecutionStatus.Scheduled).StateId);
        Assert.Equal(Assert.Single(first.PostCommitIntents).IntentId, Assert.Single(replay.PostCommitIntents).IntentId);
    }

    [Fact]
    public async Task PreflightAsync_RejectsSourceWithActiveClaimBeforeSupersession()
    {
        var source = Source(ActivityExecutionStatus.Scheduled);
        var now = DateTimeOffset.UtcNow;
        var claimedWork = new RuntimeSchedulerWorkClaim(
            new RuntimeSchedulerWorkItem("work", "workflow", "command", WorkflowExecutionCommandKind.ScheduleActivity, "envelope", "key", now, now, executionScopeId: source.Execution.ActivityExecutionId),
            "worker", 1, 1, now, now.AddMinutes(1));
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = Handler(new InMemoryWorkflowExecutableStore(), queue);
        var staging = new Staging();
        var context = new WorkflowAlterationPreflightContext(
            Job(),
            new WorkflowAlterationEnvelope("RescheduleActivity", 1, JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = source.Execution.ActivityExecutionId })),
            0,
            new WorkflowAlterationProjectedState([Workflow(), new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([source], [claimedWork])]),
            staging);

        var result = await handler.PreflightAsync(context);

        Assert.False(result.IsAccepted);
        Assert.Equal("SourceWorkClaimed", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsClaimInDescendantScopeBeforeSubtreeCleanup()
    {
        var source = Source(ActivityExecutionStatus.Scheduled);
        var descendant = Source(ActivityExecutionStatus.Waiting, "descendant") with { ParentActivityExecutionId = source.Execution.ActivityExecutionId };
        var now = DateTimeOffset.UtcNow;
        var claim = new RuntimeSchedulerWorkClaim(
            new RuntimeSchedulerWorkItem("work", "workflow", "command", WorkflowExecutionCommandKind.ScheduleActivity, "envelope", "key", now, now, executionScopeId: descendant.ExecutionScopeId),
            "worker", 1, 1, now, now.AddMinutes(1));
        var staging = new Staging();
        var context = new WorkflowAlterationPreflightContext(
            Job(),
            new WorkflowAlterationEnvelope("RescheduleActivity", 1, JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = source.Execution.ActivityExecutionId })),
            0,
            new WorkflowAlterationProjectedState([Workflow(), new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([source, descendant], [claim])]),
            staging);

        var result = await Handler(new InMemoryWorkflowExecutableStore()).PreflightAsync(context);

        Assert.False(result.IsAccepted);
        Assert.Equal("SourceWorkClaimed", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsBlockingIncidentInDescendantBeforeSupersession()
    {
        var source = Source(ActivityExecutionStatus.Scheduled);
        var descendant = Source(ActivityExecutionStatus.Waiting, "descendant") with { ParentActivityExecutionId = source.Execution.ActivityExecutionId };
        var incidents = new InMemoryIncidentStateStore();
        await incidents.SaveAsync(new IncidentState(
            "incident", "workflow", descendant.Execution.ActivityExecutionId, descendant.Execution.ExecutableNodeId,
            IncidentSeverity.Error, IncidentStatus.Blocking, null, "TestFailure", "A descendant is blocked.", DateTimeOffset.UnixEpoch, null));
        var staging = new Staging();

        var result = await Handler(new InMemoryWorkflowExecutableStore(), incidentStore: incidents).PreflightAsync(Context(source, staging, descendant));

        Assert.False(result.IsAccepted);
        Assert.Equal("BlockingIncident", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsSourceChangedAfterCapture()
    {
        var source = Source(ActivityExecutionStatus.Scheduled);
        var executableStore = new InMemoryWorkflowExecutableStore();
        await executableStore.SaveAsync(Executable());
        var job = new WorkflowAlterationJobState("job", "plan", "workflow", "tenant", 0, WorkflowAlterationJobStatus.Running,
            new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0,
            new WorkflowAlterationCapturedConcurrency(null, null, Workflow().PinnedExecutable, new Dictionary<string, string> { [source.Execution.ActivityExecutionId] = "stale" }));
        var context = new WorkflowAlterationPreflightContext(
            job,
            new WorkflowAlterationEnvelope("RescheduleActivity", 1, JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = source.Execution.ActivityExecutionId })),
            0,
            new WorkflowAlterationProjectedState([Workflow(), new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([source])]),
            new Staging());

        var result = await Handler(executableStore).PreflightAsync(context);

        Assert.False(result.IsAccepted);
        Assert.Equal("ActivityStateRevisionConflict", result.Failure!.Code);
    }

    private static RescheduleActivityAlterationHandler Handler(
        IWorkflowExecutableStore executableStore,
        InMemoryWorkflowSchedulerWorkQueue? queue = null,
        IIncidentStateStore? incidentStore = null)
    {
        queue ??= new InMemoryWorkflowSchedulerWorkQueue();
        return new RescheduleActivityAlterationHandler(
            executableStore,
            incidentStore ?? new InMemoryIncidentStateStore(),
            new ActivityExecutionSupersessionPlanner(new ActivityScopeCleanupStore(new InMemoryBookmarkStateStore(), new InMemoryDurableTimerStore(), queue)),
            TimeProvider.System);
    }

    private static WorkflowAlterationPreflightContext Context(ActivityExecutionState source, Staging staging, params ActivityExecutionState[] descendants) => new(
        Job(),
        new WorkflowAlterationEnvelope("RescheduleActivity", 1, JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = source.Execution.ActivityExecutionId })),
        0,
        new WorkflowAlterationProjectedState([Workflow(), new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([source, .. descendants])]),
        staging);

    private static WorkflowAlterationRuntimeCheckpointStateBuilder Apply(IWorkflowAlterationRuntimeCheckpointStagedChange change)
    {
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(Workflow());
        change.Apply(builder);
        return builder;
    }

    private static WorkflowAlterationJobState Job() => new("job", "plan", "workflow", "tenant", 0, WorkflowAlterationJobStatus.Running,
        new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0);

    private static WorkflowExecutionState Workflow() => new("workflow", new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
        null, DateTimeOffset.UnixEpoch, null, null, null, null, null, "tenant", new Dictionary<string, string>());

    private static WorkflowExecutable Executable(bool intrinsic = false)
    {
        var bindings = intrinsic
            ? new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Value] = new(
                    WorkflowIntrinsicInputKeys.Value,
                    new ValueTypeDescriptor("String"),
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("Done"), ValueProtectionPolicy.InstanceInline))
            }
            : new Dictionary<string, RuntimeInputBinding>();
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), bindings, new Dictionary<string, string>(), intrinsicKind: intrinsic ? WorkflowIntrinsicKind.Return : null);
        return new WorkflowExecutable(Workflow().PinnedExecutable, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutable ExecutableWithParent(bool acceptsRescheduledFaultedChild)
    {
        var child = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var parentMetadata = acceptsRescheduledFaultedChild
            ? new Dictionary<string, string> { [RuntimeMetadataKeys.AcceptsRescheduledFaultedChild] = "true" }
            : new Dictionary<string, string>();
        var parent = new ExecutableNode("parent-node", "parent-node", "test-parent", "1", "consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), parentMetadata, childSlots: [new ExecutableChildSlot("children", [child])]);
        return new WorkflowExecutable(Workflow().PinnedExecutable, parent, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static ActivityExecutionState Parent(ActivityExecutionStatus status)
    {
        var parent = Source(status, "parent");
        return parent with { Execution = parent.Execution with { ExecutableNodeId = "parent-node" } };
    }

    private static ActivityExecutionState Source(ActivityExecutionStatus status, string id = "source") => new(
        new ActivityExecution(id, "workflow", "node", "node", "test", "1"),
        status, null, 1, DateTimeOffset.UnixEpoch, null, null, null, null, null, null,
        ActivitySchedulingProvenance.From("workflow", null, null, null, null, null, id, "test"), null, [], [], 0, 0, new Dictionary<string, string>())
    {
        ExecutionScopeId = id
    };

    private sealed class Staging : IWorkflowAlterationStagingWorkspace
    {
        public List<IWorkflowAlterationStagedChange> StagedChanges { get; } = [];
        IReadOnlyList<IWorkflowAlterationStagedChange> IWorkflowAlterationStagingWorkspace.StagedChanges => StagedChanges;
        public void Stage(IWorkflowAlterationStagedChange change) => StagedChanges.Add(change);
    }
}
