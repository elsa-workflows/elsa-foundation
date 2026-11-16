using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class NativeBackgroundActivityFlowchartEfTests
{
    [Theory]
    [InlineData(BpmnParentKind.Sequence, LeftNodeId, PersistenceMode.Durable)]
    [InlineData(BpmnParentKind.Sequence, LeftNodeId, PersistenceMode.Balanced)]
    [InlineData(BpmnParentKind.Sequence, RightNodeId, PersistenceMode.Durable)]
    [InlineData(BpmnParentKind.Sequence, RightNodeId, PersistenceMode.Balanced)]
    [InlineData(BpmnParentKind.Flowchart, LeftNodeId, PersistenceMode.Durable)]
    [InlineData(BpmnParentKind.Flowchart, LeftNodeId, PersistenceMode.Balanced)]
    [InlineData(BpmnParentKind.Flowchart, RightNodeId, PersistenceMode.Durable)]
    [InlineData(BpmnParentKind.Flowchart, RightNodeId, PersistenceMode.Balanced)]
    public async Task Native_bpmn_typed_fault_recovers_before_acceptance_and_terminal_pointer_replay_preserves_original_facts(
        BpmnParentKind parentKind, string faultingNodeId, PersistenceMode persistenceMode)
    {
        var executionId = ExecutionIdPrefix + Guid.NewGuid().ToString("N");
        var barrier = new BackgroundBodyBarrier();
        var finalCounter = new FinalStepCounter();
        IReadOnlyList<ClaimedWorker> workers;
        ClaimedWorker faulting;
        WorkflowExecutableIdentity pinned;
        string originalResult;
        await using (var first = await StartHarnessAsync(executionId, barrier, finalCounter, persistenceMode: persistenceMode, includeBpmnFeature: true))
        {
            var reference = await PublishAsync(first, NewBpmnExecutable(faultingNodeId: faultingNodeId, parentKind: parentKind));
            await CopyPublicationIntoPartitionAsync(first, reference);
            await first.StartPublishedAsync(reference, executionId);
            var initialWorkflow = (await WorkflowStateAsync(first, executionId))!;
            Assert.Equal(persistenceMode.ToString(), initialWorkflow.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            pinned = initialWorkflow.PinnedExecutable;
            workers = await ClaimWorkersAsync(first, executionId, parentKind == BpmnParentKind.Flowchart ? 3 : 2);
            faulting = Assert.Single(workers, item => item.Request.ExecutableNodeId == faultingNodeId);
            await ExecuteBackgroundAsync(first, barrier, faulting);
            var ready = Assert.IsType<BackgroundActivityWorkItem>(await BackgroundWorkAsync(first, executionId, faulting.Request.WorkId));
            Assert.Equal(BackgroundActivityWorkStatus.ResultReady, ready.Status);
            Assert.Equal(BackgroundActivityWorkResultKind.Fault, ready.Result!.Kind);
            Assert.Equal("background.business-fault", ready.Result.Fault!.Code);
            originalResult = JsonSerializer.Serialize(ready.Result);
            Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(first, executionId));
            Assert.Equal(ActivityExecutionStatus.Running, await ActivityStatusAsync(first, executionId, BpmnNodeId));
            Assert.Equal(ActivityExecutionStatus.Running, await ActivityStatusAsync(first, executionId, faultingNodeId));
            Assert.Equal(0, finalCounter.Count);
        }

        JsonElement stable;
        await using (var recovered = await StartHarnessAsync(executionId, barrier, finalCounter,
            persistenceMode: persistenceMode, includeBpmnFeature: true, activityIdNamespace: "fault-recovered"))
        {
            Assert.Equal(pinned, (await WorkflowStateAsync(recovered, executionId))!.PinnedExecutable);
            Assert.Equal(BackgroundActivityWorkStatus.ResultReady,
                (await BackgroundWorkAsync(recovered, executionId, faulting.Request.WorkId))!.Status);
            await DrivePartitionAsync(recovered, executionId);
            Assert.Equal(WorkflowExecutionStatus.Faulted, await WorkflowStatusAsync(recovered, executionId));
            Assert.Equal(ActivityExecutionStatus.Faulted, await ActivityStatusAsync(recovered, executionId, faultingNodeId));
            Assert.Equal(ActivityExecutionStatus.Faulted, await ActivityStatusAsync(recovered, executionId, BpmnNodeId));
            Assert.Equal(parentKind == BpmnParentKind.Sequence ? ActivityExecutionStatus.Running : ActivityExecutionStatus.Faulted,
                await ActivityStatusAsync(recovered, executionId, parentKind == BpmnParentKind.Sequence ? SequenceNodeId : FlowchartNodeId));
            var accepted = Assert.IsType<BackgroundActivityWorkItem>(await BackgroundWorkAsync(recovered, executionId, faulting.Request.WorkId));
            Assert.Equal(BackgroundActivityWorkStatus.Accepted, accepted.Status);
            Assert.Equal(originalResult, JsonSerializer.Serialize(accepted.Result));
            var child = Assert.Single(await ActivityStatesAsync(recovered, executionId), state => state.Execution.ExecutableNodeId == faultingNodeId);
            Assert.Equal("background.business-fault", child.Fault!.Code);
            await using (var scope = await CreatePartitionScopeAsync(recovered))
            {
                var incident = Assert.Single(await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(executionId),
                    candidate => candidate.ActivityExecutionId == child.InvocationId);
                Assert.True(incident.IsBlocking);
                Assert.Equal("ActivityReturnedFault", incident.FailureType);
                Assert.Equal(faultingNodeId, incident.ExecutableNodeId);
                foreach (var sibling in workers.Where(item => item.Request.WorkId != faulting.Request.WorkId))
                    Assert.Equal(BackgroundActivityWorkStatus.Cancelled,
                        (await scope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>().FindAsync(executionId, sibling.Request.WorkId))!.Status);
            }
            Assert.Equal(0, finalCounter.Count);
            stable = await NativeReplayFactsAsync(recovered, executionId, workers);
            await AssertNativeQueueEmptyAsync(recovered, executionId);
        }

        await using (var replayed = await StartHarnessAsync(executionId, barrier, finalCounter,
            persistenceMode: persistenceMode, includeBpmnFeature: true, activityIdNamespace: "fault-replayed"))
        {
            AssertNativeReplayFactsEqual(stable, await NativeReplayFactsAsync(replayed, executionId, workers));
            await EnqueueCoordinatorPointerAsync(replayed, faulting.Request.CoordinatorWorkItem);
            await AssertNativeWorkQueuedAsync(replayed, executionId, faulting.Request.CoordinatorWorkItem);
            await DrivePartitionAsync(replayed, executionId);
            await AssertNativeQueueEmptyAsync(replayed, executionId);
            AssertNativeReplayFactsEqual(stable, await NativeReplayFactsAsync(replayed, executionId, workers));
            Assert.Equal(0, finalCounter.Count);
            Assert.Equal(1, barrier.ExecutionCount(faultingNodeId == LeftNodeId ? "left" : "right"));
            Assert.Equal(0, barrier.ExecutionCount(faultingNodeId == LeftNodeId ? "right" : "left"));
            Assert.Equal(0, barrier.ExecutionCount("outer-sibling"));
        }
    }

    private static async Task<JsonElement> NativeReplayFactsAsync(
        WorkflowExecutionHarness harness, string executionId, IReadOnlyList<ClaimedWorker> workers)
    {
        await using var scope = await CreatePartitionScopeAsync(harness);
        var provider = scope.ServiceProvider;
        var activities = await provider.GetRequiredService<EfActivityExecutionStateStore>().ListAllAsync(executionId);
        var incidents = await provider.GetRequiredService<EfIncidentStateStore>().ListAsync(executionId);
        var values = await provider.GetRequiredService<EfDurableValueStateStore>().ListAllDurableValueStatesAsync(executionId);
        var background = new List<BackgroundActivityWorkItem>();
        foreach (var item in workers.OrderBy(item => item.Request.WorkId, StringComparer.Ordinal))
            background.Add(Assert.IsType<BackgroundActivityWorkItem>(await provider.GetRequiredService<EfBackgroundActivityWorkStore>()
                .FindAsync(executionId, item.Request.WorkId)));
        var bookmarks = provider.GetRequiredService<EfBookmarkStateStore>();
        var observations = new List<BookmarkStateObservation>();
        foreach (var wait in (await bookmarks.ListAllBookmarkStatesAsync(executionId)).OrderBy(wait => wait.BookmarkId, StringComparer.Ordinal))
            observations.Add(Assert.IsType<BookmarkStateObservation>(await bookmarks.ObserveAsync(executionId, wait.BookmarkId)));
        var workflow = Assert.IsType<WorkflowExecutionState>(await provider.GetRequiredService<EfWorkflowExecutionStateStore>().FindAsync(executionId));
        return JsonSerializer.SerializeToElement(new
        {
            // UpdatedAt is operational touch time. Preserve the complete semantic/context/source
            // projection and authoritative inline variable frame rather than treating value rows as it.
            workflow.WorkflowExecutionId, workflow.Status, workflow.SubStatus, workflow.PinnedExecutable,
            workflow.CreatedAt, workflow.StartedAt, workflow.CompletedAt, workflow.CorrelationId,
            workflow.ParentWorkflowExecutionId, workflow.TenantId, workflow.SystemMetadata,
            workflow.RunKind, workflow.PinnedSource, workflow.Partition, workflow.Authority,
            workflow.DispatchNestingDepth, workflow.TestScope, workflow.RootVariableFrame,
            activities = activities.OrderBy(state => state.InvocationId, StringComparer.Ordinal),
            incidents = incidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal),
            values = values.OrderBy(value => value.DurableValueId, StringComparer.Ordinal),
            background, bookmarks = observations
        });
    }

    private static void AssertNativeReplayFactsEqual(JsonElement expected, JsonElement actual) =>
        Assert.True(JsonElement.DeepEquals(expected, actual),
            "Native replay changed durable activity, variable, background receipt, physical bookmark or incident facts.");

    private static async Task AssertNativeWorkQueuedAsync(WorkflowExecutionHarness harness, string executionId, RuntimeSchedulerWorkItem work)
    {
        await using var scope = await CreatePartitionScopeAsync(harness);
        var queued = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAllAsync(new RuntimeSchedulerWorkQuery(executionId));
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(work),
            JsonSerializer.SerializeToElement(Assert.Single(queued, candidate => candidate.WorkItemId == work.WorkItemId))));
    }

    private static async Task AssertNativeQueueEmptyAsync(WorkflowExecutionHarness harness, string executionId)
    {
        await using var scope = await CreatePartitionScopeAsync(harness);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAllAsync(new RuntimeSchedulerWorkQuery(executionId)));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(executionId));
    }

    private sealed class NativeBpmnCallbackCapture
    {
        public ConcurrentQueue<RuntimeSchedulerWorkItem> WorkItems { get; } = new();
        public void Observe(RuntimeSchedulerWorkItem work)
        {
            if (work.CommandKind == WorkflowExecutionCommandKind.CompleteActivity && work.Payload is { } payload &&
                payload.Deserialize<RuntimeCompleteActivityCommandPayload>() is { } completion && completion.ExecutableNodeId == BpmnNodeId)
                WorkItems.Enqueue(work);
        }
    }
    private sealed class NativeCapturedCallbackHandler(IWorkflowSchedulerWorkHandler inner, NativeBpmnCallbackCapture capture) : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
    {
        public string Name => inner.Name;
        public bool CanHandle(RuntimeSchedulerWorkItem work) => inner.CanHandle(work);
        public ValueTask HandleAsync(RuntimeSchedulerWorkItem work, CancellationToken cancellationToken = default)
        { capture.Observe(work); return inner.HandleAsync(work, cancellationToken); }
        public ValueTask HandleAsync(RuntimeSchedulerWorkItem work, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
        { capture.Observe(work); return ((IRuntimePipelineWorkHandler)inner).HandleAsync(work, pipelineContext, cancellationToken); }
    }
}
