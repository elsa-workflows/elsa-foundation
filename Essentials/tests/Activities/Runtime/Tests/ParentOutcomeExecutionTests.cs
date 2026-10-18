using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ParentOutcomeExecutionTests
{
    [Fact]
    public async Task FaultWithNotifications_UsesOneCombinedCallbackAndAbsorbsTheExactIncident()
    {
        var recorder = new ParentOutcomeRecorder();
        await using var harness = NewHarness(recorder);

        var run = await harness.RunAsync(NewGraph(typeof(CombinedOutcomeParentActivity)));

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        var child = run.State("node-child");
        Assert.Equal(ActivityExecutionStatus.Faulted, child.Status);
        Assert.Equal("test.parent-outcome.child", child.Fault!.Code);

        var outcome = Assert.Single(recorder.Outcomes);
        Assert.Equal("actexec-child", outcome.FaultedChildActivityExecutionId);
        Assert.Equal("node-child", outcome.FaultedChildExecutableNodeId);
        Assert.Equal("test.parent-outcome.child", outcome.Fault.Code);
        Assert.Equal("intentional child fault", outcome.Fault.Message);
        Assert.Equal(["first", "second"], outcome.Notifications.Select(notification => notification.Code));
        Assert.All(outcome.Notifications, notification =>
        {
            Assert.Equal("actexec-child", notification.NotifyingChildActivityExecutionId);
            Assert.Equal("node-child", notification.NotifyingChildExecutableNodeId);
        });
        Assert.Equal(0, recorder.LegacyNotificationCalls);
        Assert.Equal(0, recorder.LegacyFaultCalls);

        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(outcome.IncidentId, incident.IncidentId);
        Assert.Equal("actexec-child", incident.ActivityExecutionId);
        Assert.Equal("node-child", incident.ExecutableNodeId);
        Assert.Equal("actexec-parent", incident.Metadata[RuntimeMetadataKeys.FaultAbsorbedBy]);

        var workItemId = child.Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId];
        Assert.Equal(workItemId, incident.Metadata[RuntimeMetadataKeys.ParentOutcomeWorkItemId]);
        Assert.NotEmpty(workItemId);

        var childFaultCommit = Assert.Single(
            harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.IncidentRecorded &&
                     write.Commit.Checkpoint.ActivityExecutionIds.Contains("actexec-child", StringComparer.Ordinal)).Commit;
        var outcomeWorkItem = Assert.Single(childFaultCommit.PostCommitIntents)
            .Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, outcomeWorkItem.CommandKind);
        var outcomePayload = outcomeWorkItem.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!;
        Assert.Equal(SchedulerCompletionKind.ParentOutcomeEvaluation, outcomePayload.CompletionKind);
        Assert.Equal(["first", "second"], outcomePayload.ParentOutcome!.Notifications.Select(notification => notification.Code));

        var completedParentCommit = Assert.Single(
            harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted &&
                     write.Commit.Checkpoint.ActivityExecutionIds.Contains("actexec-parent", StringComparer.Ordinal)).Commit;
        Assert.Contains("actexec-child", completedParentCommit.Checkpoint.ActivityExecutionIds);
        var completionWorkItem = Assert.Single(completedParentCommit.PostCommitIntents)
            .Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.DoesNotContain(RuntimeMetadataKeys.ChildFaulted, completionWorkItem.CommandMetadata.Keys);
        Assert.DoesNotContain(RuntimeMetadataKeys.IncidentId, completionWorkItem.CommandMetadata.Keys);
        Assert.DoesNotContain(RuntimeMetadataKeys.CompletedChildActivityExecutionId, completionWorkItem.CommandMetadata.Keys);
    }

    [Fact]
    public async Task CombinedOutcomeCallback_CanBubbleItsFaultAndOrderedNotificationsToGrandparent()
    {
        var recorder = new ParentOutcomeRecorder();
        await using var harness = NewHarness(recorder, "actexec-grandparent", "actexec-parent", "actexec-child", "actexec-leaf");

        var run = await harness.RunAsync(NewBubblingGraph());

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-grandparent");
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-parent").Status);
        Assert.Equal("test.parent-outcome.bubbled", run.State("node-parent").Fault!.Code);
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-child").Status);
        Assert.Equal("test.parent-outcome.child", run.State("node-child").Fault!.Code);

        Assert.Equal(2, recorder.Outcomes.Count);
        var innerOutcome = recorder.Outcomes[0];
        Assert.Equal("actexec-child", innerOutcome.FaultedChildActivityExecutionId);
        Assert.Equal("node-child", innerOutcome.FaultedChildExecutableNodeId);
        Assert.Equal("test.parent-outcome.child", innerOutcome.Fault.Code);
        Assert.Equal(["first", "second"], innerOutcome.Notifications.Select(notification => notification.Code));

        var bubbledOutcome = recorder.Outcomes[1];
        Assert.Equal("actexec-parent", bubbledOutcome.FaultedChildActivityExecutionId);
        Assert.Equal("node-parent", bubbledOutcome.FaultedChildExecutableNodeId);
        Assert.Equal("test.parent-outcome.bubbled", bubbledOutcome.Fault.Code);
        Assert.NotEqual(innerOutcome.IncidentId, bubbledOutcome.IncidentId);
        Assert.Equal(["bubble-first", "bubble-second"], bubbledOutcome.Notifications.Select(notification => notification.Code));
        Assert.All(bubbledOutcome.Notifications, notification =>
        {
            Assert.Equal("actexec-parent", notification.NotifyingChildActivityExecutionId);
            Assert.Equal("node-parent", notification.NotifyingChildExecutableNodeId);
        });
        Assert.Equal(0, recorder.LegacyFaultCalls);
        Assert.Equal(0, recorder.LegacyNotificationCalls);

        var incidents = await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        var originalIncident = Assert.Single(incidents, incident => incident.IncidentId == innerOutcome.IncidentId);
        Assert.Equal("actexec-child", originalIncident.ActivityExecutionId);
        Assert.Equal(IncidentStatus.Suppressed, originalIncident.Status);
        var bubbledIncident = Assert.Single(incidents, incident => incident.IncidentId == bubbledOutcome.IncidentId);
        Assert.Equal("actexec-parent", bubbledIncident.ActivityExecutionId);
        Assert.Equal(IncidentStatus.Resolved, bubbledIncident.Status);
        Assert.Equal("actexec-grandparent", bubbledIncident.Metadata[RuntimeMetadataKeys.FaultAbsorbedBy]);
    }

    [Fact]
    public async Task CombinedOutcome_DeferCanAbsorbAndScheduleTheNextChild()
    {
        var recorder = new ParentOutcomeRecorder { OutcomeBehavior = OutcomeBehavior.DeferAndScheduleFollowup };
        await using var harness = NewHarness(recorder);

        var run = await harness.RunAsync(NewGraph(typeof(CombinedOutcomeParentActivity), includeFollowup: true));

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        run.AssertCompleted("node-followup");
        Assert.Single(recorder.Outcomes);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal("actexec-parent", incident.Metadata[RuntimeMetadataKeys.FaultAbsorbedBy]);
        var child = run.State("node-child");
        Assert.Equal(incident.Metadata[RuntimeMetadataKeys.ParentOutcomeWorkItemId],
            child.Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId]);
    }

    [Fact]
    public async Task FaultOnlyParent_UsesItsLegacyFaultCallbackForCombinedChildOutcome()
    {
        var recorder = new ParentOutcomeRecorder();
        await using var harness = NewHarness(recorder);

        var run = await harness.RunAsync(NewGraph(typeof(FaultOnlyParentActivity)));

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        Assert.Equal(1, recorder.LegacyFaultCalls);
        Assert.Equal(0, recorder.LegacyNotificationCalls);
        Assert.Empty(recorder.Outcomes);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal("actexec-parent", incident.Metadata[RuntimeMetadataKeys.FaultAbsorbedBy]);
    }

    [Fact]
    public async Task ParentWithNeitherLegacyCapability_PreservesFaultOnlyBypassAndBlockingIncident()
    {
        var recorder = new ParentOutcomeRecorder();
        await using var harness = NewHarness(recorder);

        var run = await harness.RunAsync(NewGraph(typeof(CompletionOnlyParentActivity)), allowPendingWorkOnTerminalCompletion: true);

        Assert.Equal(ActivityExecutionStatus.Running, run.State("node-parent").Status);
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-child").Status);
        Assert.Empty(recorder.Outcomes);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        Assert.Equal(incident.Metadata[RuntimeMetadataKeys.ParentOutcomeWorkItemId],
            run.State("node-child").Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId]);
    }

    [Fact]
    public async Task ParentWithBothLegacyCapabilities_RefusesCombinedOutcomeWithoutCallingEitherCallback()
    {
        var recorder = new ParentOutcomeRecorder();
        await using var harness = NewHarness(recorder);

        var run = await harness.RunAsync(NewGraph(typeof(LegacyBothParentActivity)), allowPendingWorkOnTerminalCompletion: true);

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Equal("runtime.parent-outcome.handler-required", parent.Fault!.Code);
        Assert.Equal(0, recorder.LegacyFaultCalls);
        Assert.Equal(0, recorder.LegacyNotificationCalls);
        Assert.Empty(recorder.Outcomes);
        var child = run.State("node-child");
        Assert.Equal(ActivityExecutionStatus.Faulted, child.Status);
        var incidents = await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.Equal(2, incidents.Count);
        var childIncident = Assert.Single(incidents, incident => incident.ActivityExecutionId == "actexec-child");
        var parentIncident = Assert.Single(incidents, incident => incident.ActivityExecutionId == "actexec-parent");
        Assert.Equal(IncidentStatus.Blocking, childIncident.Status);
        Assert.Equal(IncidentStatus.Blocking, parentIncident.Status);
        Assert.Contains(childIncident.IncidentId, child.IncidentIds);
    }

    [Fact]
    public async Task CombinedCallbackFaultWithoutAbsorption_RecordsOneParentFaultAndPreservesChildIncident()
    {
        var recorder = new ParentOutcomeRecorder { OutcomeBehavior = OutcomeBehavior.FaultWithoutAbsorption };
        await using var harness = NewHarness(recorder);

        var run = await harness.RunAsync(NewGraph(typeof(CombinedOutcomeParentActivity)), allowPendingWorkOnTerminalCompletion: true);

        Assert.Single(recorder.Outcomes);
        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Equal("test.parent-outcome.parent", parent.Fault!.Code);
        var incidents = await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.Equal(2, incidents.Count);
        var childIncident = Assert.Single(incidents, incident => incident.ActivityExecutionId == "actexec-child");
        var parentIncident = Assert.Single(incidents, incident => incident.ActivityExecutionId == "actexec-parent");
        Assert.Equal(IncidentStatus.Blocking, childIncident.Status);
        Assert.Equal(IncidentStatus.Blocking, parentIncident.Status);
        Assert.DoesNotContain(RuntimeMetadataKeys.FaultAbsorbedBy, childIncident.Metadata.Keys);
    }

    [Theory]
    [InlineData(OutcomeBehavior.FaultWithAbsorption)]
    [InlineData(OutcomeBehavior.CancelWithAbsorption)]
    public async Task CombinedFaultOrCancelContinuation_CannotAbsorbChildIncident(OutcomeBehavior behavior)
    {
        var recorder = new ParentOutcomeRecorder { OutcomeBehavior = behavior };
        await using var harness = NewHarness(recorder);

        var run = await harness.RunAsync(NewGraph(typeof(CombinedOutcomeParentActivity)), allowPendingWorkOnTerminalCompletion: true);

        Assert.Single(recorder.Outcomes);
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-parent").Status);
        var child = run.State("node-child");
        Assert.Equal(ActivityExecutionStatus.Faulted, child.Status);
        var childIncident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId), incident => incident.ActivityExecutionId == "actexec-child");
        Assert.Equal(IncidentStatus.Blocking, childIncident.Status);
        Assert.DoesNotContain(RuntimeMetadataKeys.FaultAbsorbedBy, childIncident.Metadata.Keys);
    }

    [Theory]
    [InlineData(ChildCallbackBehavior.ThrowAfterStaging)]
    [InlineData(ChildCallbackBehavior.DisposeThrowsAfterStaging)]
    [InlineData(ChildCallbackBehavior.CancelAfterStaging)]
    public async Task FailedOrCancelledChildCallback_DiscardsStagedNotifications(ChildCallbackBehavior behavior)
    {
        var recorder = new ParentOutcomeRecorder { ChildBehavior = behavior };
        await using var harness = NewHarness(recorder);

        var run = await harness.RunAsync(NewGraph(typeof(FaultOnlyParentActivity)));

        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-child").Status);
        Assert.Equal(1, recorder.LegacyFaultCalls);
        Assert.Empty(recorder.Outcomes);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.DoesNotContain(RuntimeMetadataKeys.ParentOutcomeWorkItemId, incident.Metadata.Keys);
        var childFaultCommit = Assert.Single(
            harness.Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits(),
            write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.IncidentRecorded &&
                     write.Commit.Checkpoint.ActivityExecutionIds.Contains("actexec-child", StringComparer.Ordinal)).Commit;
        var legacyWorkItem = Assert.Single(childFaultCommit.PostCommitIntents)
            .Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(SchedulerCompletionKind.ParentCompletionEvaluation,
            legacyWorkItem.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>()!.CompletionKind);
    }

    private sealed class ParentOutcomeRecorder
    {
        public ChildCallbackBehavior ChildBehavior { get; init; } = ChildCallbackBehavior.ReturnFaultWithSignals;
        public OutcomeBehavior OutcomeBehavior { get; init; } = OutcomeBehavior.CompleteWithAbsorption;
        public List<ActivityChildOutcomeContext> Outcomes { get; } = [];
        public int LegacyNotificationCalls { get; set; }
        public int LegacyFaultCalls { get; set; }
    }

    public enum ChildCallbackBehavior
    {
        ReturnFaultWithSignals,
        ThrowAfterStaging,
        DisposeThrowsAfterStaging,
        CancelAfterStaging
    }

    public enum OutcomeBehavior
    {
        CompleteWithAbsorption,
        DeferAndScheduleFollowup,
        FaultWithAbsorption,
        FaultWithoutAbsorption,
        CancelWithAbsorption
    }

    private static WorkflowExecutionHarness NewHarness(ParentOutcomeRecorder recorder, params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .ConfigureServices(services => services.AddSingleton(recorder))
            .Build(activityExecutionIds.Length == 0
                ? new[] { "actexec-parent", "actexec-child", "actexec-leaf", "actexec-followup" }
                : activityExecutionIds);

    private static WorkflowExecutable NewGraph(Type parentType, bool includeFollowup = false)
    {
        var children = new List<ExecutableNode>
        {
            StructuralExecutionTestSupport.NewStructuralNode(
                "node-child",
                typeof(FaultingNotifyingChildActivity),
                WorkflowExecutionHarness.NewProbeNode("node-leaf"))
        };
        if (includeFollowup)
            children.Add(WorkflowExecutionHarness.NewProbeNode("node-followup"));

        return StructuralExecutionTestSupport.NewExecutable(
            StructuralExecutionTestSupport.NewStructuralNode("node-parent", parentType, children.ToArray()));
    }

    private static WorkflowExecutable NewBubblingGraph() => StructuralExecutionTestSupport.NewExecutable(
        StructuralExecutionTestSupport.NewStructuralNode("node-grandparent", typeof(BubblingOutcomeParentActivity),
            StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(BubblingOutcomeParentActivity),
                StructuralExecutionTestSupport.NewStructuralNode("node-child", typeof(FaultingNotifyingChildActivity),
                    WorkflowExecutionHarness.NewProbeNode("node-leaf")))));

    private sealed class FaultingNotifyingChildActivity(ParentOutcomeRecorder recorder) : StructuralActivity, IRuntimeStructuralActivity, IDisposable
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            context.RequestParentNotification("first");
            context.RequestParentNotification("second");
            return recorder.ChildBehavior switch
            {
                ChildCallbackBehavior.ThrowAfterStaging => throw new InvalidOperationException("child callback failed after staging"),
                ChildCallbackBehavior.CancelAfterStaging => ValueTask.FromResult(RuntimeStructuralContinuation.Cancel("child cancelled after staging")),
                _ => ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(
                    new ActivityFault("test.parent-outcome.child", "intentional child fault")))
            };
        }

        public void Dispose()
        {
            if (recorder.ChildBehavior == ChildCallbackBehavior.DisposeThrowsAfterStaging)
                throw new InvalidOperationException("child disposal failed after staging");
        }
    }

    private abstract class ChildParentActivityBase : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = context.ExecutableNode.ChildSlots.Single().Activities.First();
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }

    private sealed class CombinedOutcomeParentActivity(ParentOutcomeRecorder recorder) : ChildParentActivityBase,
        IRuntimeActivityChildFaultHandler,
        IRuntimeActivityChildNotificationHandler,
        IRuntimeActivityChildOutcomeHandler
    {
        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            recorder.LegacyFaultCalls++;
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(
            IRuntimeActivityExecutionContext context,
            ActivityChildNotifiedContext notification)
        {
            recorder.LegacyNotificationCalls++;
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildOutcomeAsync(ActivityChildOutcomeContext context)
        {
            recorder.Outcomes.Add(context);
            var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
            if (recorder.OutcomeBehavior != OutcomeBehavior.FaultWithoutAbsorption)
                runtimeContext.RequestChildFaultAbsorption(context.IncidentId, "consumed combined child outcome");
            if (recorder.OutcomeBehavior == OutcomeBehavior.DeferAndScheduleFollowup)
            {
                var followup = Assert.Single(runtimeContext.ExecutableNode.ChildSlots.Single().Activities,
                    child => child.ExecutableNodeId == "node-followup");
                runtimeContext.ScheduleChildActivity(followup.ExecutableNodeId, runtimeContext.ActivityExecutionState.InvocationId);
                return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
            }

            return ValueTask.FromResult(recorder.OutcomeBehavior switch
            {
                OutcomeBehavior.FaultWithAbsorption => RuntimeStructuralContinuation.Faulted(new ActivityFault("test.parent-outcome.invalid-fault", "fault cannot absorb")),
                OutcomeBehavior.FaultWithoutAbsorption => RuntimeStructuralContinuation.Faulted(new ActivityFault("test.parent-outcome.parent", "parent declined child outcome")),
                OutcomeBehavior.CancelWithAbsorption => RuntimeStructuralContinuation.Cancel("cancel cannot absorb"),
                _ => RuntimeStructuralContinuation.Complete()
            });
        }
    }

    private sealed class BubblingOutcomeParentActivity(ParentOutcomeRecorder recorder) : ChildParentActivityBase,
        IRuntimeActivityChildFaultHandler,
        IRuntimeActivityChildNotificationHandler,
        IRuntimeActivityChildOutcomeHandler
    {
        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            recorder.LegacyFaultCalls++;
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(
            IRuntimeActivityExecutionContext context,
            ActivityChildNotifiedContext notification)
        {
            recorder.LegacyNotificationCalls++;
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildOutcomeAsync(ActivityChildOutcomeContext context)
        {
            recorder.Outcomes.Add(context);
            var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
            if (context.FaultedChildExecutableNodeId == "node-child")
            {
                runtimeContext.RequestParentNotification("bubble-first");
                runtimeContext.RequestParentNotification("bubble-second");
                return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(
                    new ActivityFault("test.parent-outcome.bubbled", "parent bubbled the child outcome")));
            }

            runtimeContext.RequestChildFaultAbsorption(context.IncidentId, "grandparent absorbed the bubbled child outcome");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    private sealed class FaultOnlyParentActivity(ParentOutcomeRecorder recorder) : ChildParentActivityBase,
        IRuntimeActivityChildFaultHandler
    {
        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            recorder.LegacyFaultCalls++;
            ((IRuntimeActivityExecutionContext)context.ParentContext)
                .RequestChildFaultAbsorption(context.IncidentId!, "legacy fault-only handling");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    private sealed class CompletionOnlyParentActivity : ChildParentActivityBase
    {
    }

    private sealed class LegacyBothParentActivity(ParentOutcomeRecorder recorder) : ChildParentActivityBase,
        IRuntimeActivityChildFaultHandler,
        IRuntimeActivityChildNotificationHandler
    {
        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            recorder.LegacyFaultCalls++;
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(
            IRuntimeActivityExecutionContext context,
            ActivityChildNotifiedContext notification)
        {
            recorder.LegacyNotificationCalls++;
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }
    }

}
