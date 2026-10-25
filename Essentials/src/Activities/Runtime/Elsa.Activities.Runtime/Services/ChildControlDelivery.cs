using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Accepts exact-source control and commits its ordered prefix with one structural consumer decision.</summary>
internal static class ChildControlDelivery
{
    internal static async ValueTask RunAsync(IServiceProvider services, TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem, IActivityCommandPayload payload, RuntimeChildControlTransport incoming,
        string? completionWorkItemId, CancellationToken cancellationToken)
    {
        try { await RunCoreAsync(services, timeProvider, workItem, payload, incoming, completionWorkItemId, cancellationToken); }
        catch (WorkflowControlBoundaryException exception) when (exception.IsUnavailable)
        {
            throw new RuntimeSchedulerWorkUnavailableException("runtime.child-control.source-unavailable",
                "The exact child-control source could not be read; delivery remains owed.", exception);
        }
    }

    private static async ValueTask RunCoreAsync(IServiceProvider services, TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem, IActivityCommandPayload payload, RuntimeChildControlTransport incoming,
        string? completionWorkItemId, CancellationToken cancellationToken)
    {
        var authorizer = services.GetRequiredService<IRuntimeChildControlAuthorizer>();
        await authorizer.ValidateAsync(incoming, cancellationToken);
        var activities = services.GetRequiredService<IActivityExecutionStateStore>();
        var child = await ReadActivityAsync(activities, workItem.WorkflowExecutionId, incoming.CallingActivityExecutionId, incoming, cancellationToken);
        var parent = await ReadActivityAsync(activities, workItem.WorkflowExecutionId, incoming.ConsumerActivityExecutionId, incoming, cancellationToken);
        if (payload is RuntimeChildControlCommandPayload command &&
            (command.ParentActivityExecutionId != parent.ParentActivityExecutionId || command.BranchId != parent.BranchId))
            throw new WorkflowControlBoundaryException(incoming.Batch.ChildWorkflowExecutionId, incoming.Batch.RootActivityExecutionId,
                new InvalidOperationException("Child-control target ancestry differs from its retained invocation."));
        if (payload.ActivityExecutionId != parent.InvocationId || payload.ExecutableNodeId != parent.Execution.ExecutableNodeId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(payload.PinnedExecutable, incoming.ParentExecutable))
            throw new InvalidOperationException("Child-control delivery target differs from its retained consumer invocation.");
        if (parent.Status != ActivityExecutionStatus.Running || child.Status is ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Superseded)
            return;
        if ((completionWorkItemId is not null) != incoming.Batch.IsTerminal)
            throw new InvalidOperationException("Terminal control must enter through its ordinary completed local call.");
        if (incoming.Batch.IsTerminal && (child.Status != ActivityExecutionStatus.Completed ||
            !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(child.ChildControl), JsonSerializer.SerializeToElement(incoming))))
            throw new InvalidOperationException("Terminal control differs from the validated completed call projection.");

        var decision = ChildControlInboxEvaluation.Accept(child, incoming);
        if (decision.AlreadyAccepted && (completionWorkItemId is null || ActivityAttemptActivationClaimer.WasParentCompletionProcessed(child)))
            return;
        var contribution = new StructuralChildControlContribution(decision.CallingChild, completionWorkItemId);
        if (decision.Ready.Count == 0)
        {
            await StructuralParentEvaluationSupport.CommitDeferredParentActivityAsync(
                services.GetRequiredService<RuntimeCheckpointCommitter>(),
                services.GetService<IRuntimeActivityExecutionInspectionAccumulator>(),
                services.GetRequiredService<IRuntimeExecutionIdGenerator>(), timeProvider,
                NewSource(workItem, payload, "ChildControlBuffered"), parent, [], [], [], [], [], cancellationToken,
                childControlContribution: contribution);
            return;
        }

        foreach (var ready in decision.Ready)
            await authorizer.ValidateAsync(ready, cancellationToken);
        IReadOnlyCollection<string> outcomes = [];
        if (decision.Ready[^1].Batch.IsTerminal)
        {
            if (child.Status != ActivityExecutionStatus.Completed || child.Completion is null ||
                !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(child.ChildControl), JsonSerializer.SerializeToElement(decision.Ready[^1])))
                throw new InvalidOperationException("A ready terminal barrier requires its exact committed ordinary leaf completion.");
            outcomes = [child.Completion.OutcomeKey];
        }
        await StructuralCallbackEvaluation.RunAsync(services, timeProvider, workItem, payload,
            new StructuralCallbackEvaluation.Definition
            {
                CommandName = "DeliverChildControl",
                EvaluationName = "child-control",
                SnapshotWorkItemMetadataKey = RuntimeMetadataKeys.SchedulerWorkItemId,
                DeferredCheckpointReason = "ChildControlEvaluation",
                FaultSubStatus = "ChildControlFaulted",
                CancellationDisposalMessage = "Child-control evaluation cancellation and activation disposal both failed.",
                // An admitted declaration cannot silently opt out after publication.
                OptsIn = static _ => true,
                InvokeAsync = (activity, context) => activity is IRuntimeActivityChildControlConsumer consumer
                    ? consumer.OnChildControlAsync(new RuntimeChildControlContext(context, child.InvocationId,
                        child.Execution.ExecutableNodeId, child.IterationId, child.Provenance.Metadata, decision.Ready, outcomes))
                    : ValueTask.FromException<RuntimeStructuralContinuation>(new InvalidOperationException("The pinned child-control consumer does not implement its declared callback.")),
                NewCommitSource = reason => NewSource(workItem, payload, reason),
                NewFaultIncidentRecordRequest = (committer, state, exception, subStatus, snapshots) =>
                    new ActivityFaultIncidentRecordRequest(committer, workItem, state.InvocationId, state.Execution.ExecutableNodeId,
                        state, exception, subStatus, new Dictionary<string, string> { [RuntimeMetadataKeys.CheckpointReason] = "ChildControlEvaluation" },
                        new Dictionary<string, string>(), snapshots),
                ChildControlContribution = contribution
            }, alreadyProcessed: null, cancellationToken);
    }

    private static async ValueTask<ActivityExecutionState> ReadActivityAsync(IActivityExecutionStateStore activities,
        string workflowId, string activityId, RuntimeChildControlTransport incoming, CancellationToken cancellationToken)
    {
        ActivityExecutionState? state;
        try { state = await activities.FindAsync(workflowId, activityId, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (WorkflowControlBoundaryException) { throw; }
        catch (Exception exception) { throw new WorkflowControlBoundaryException(incoming.Batch.ChildWorkflowExecutionId,
            incoming.Batch.RootActivityExecutionId, exception, isUnavailable: exception is not (JsonException or ArgumentException)); }
        return state ?? throw new WorkflowControlBoundaryException(incoming.Batch.ChildWorkflowExecutionId, incoming.Batch.RootActivityExecutionId,
            new InvalidOperationException("Child-control delivery references a missing retained invocation."));
    }

    private static StructuralParentEvaluationSupport.ParentEvaluationCommitSource NewSource(
        RuntimeSchedulerWorkItem workItem, IActivityCommandPayload payload, string reason) =>
        StructuralParentEvaluationSupport.NewCommitSource(workItem, payload, reason, workItem.CommandMetadata);
}
