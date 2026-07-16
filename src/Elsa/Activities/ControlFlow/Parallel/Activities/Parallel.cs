using Elsa.Activities.ControlFlow.Results;
using Elsa.Activities.Parallel.Exceptions;
using Elsa.Activities.Parallel.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Parallel.Activities;

/// <summary>
/// Fork/join composite. On execution it <b>forks</b> by scheduling every (non-empty) branch at once, each
/// in its own named child slot (<see cref="BranchSlotName"/>) and under a distinct engine
/// <see cref="ActivitySchedulingProvenance.BranchId"/>, the way the flowchart parallel fork/join policies
/// use the engine branch scope. It then <b>joins</b>: each branch completion is recorded, and the
/// composite completes with <see cref="ActivityOutcomes.Done"/> only once the join condition is met —
/// by default all branches must finish, or a configured subset/threshold when fewer are required.
/// </summary>
/// <remarks>
/// <para>
/// <b>Single-threaded fork (D11).</b> True parallel OS threading is deferred: the scheduler is
/// single-threaded. "Concurrent" here means all branch <c>ActivityExecution</c>s are scheduled together
/// at fork time and progress independently through the scheduler, not that they run on separate threads.
/// </para>
/// <para>
/// <b>Stateless join.</b> The child-completion callback is re-constructed per completion with no mutable
/// activity state carried across calls. Rather than persist a running counter, the join recovers how many
/// branches have finished by querying the durable activity-execution store for this composite's completed
/// branch children (the runtime persists each completing child as <c>Completed</c> before it enqueues the
/// parent-completion evaluation). When that count reaches the effective threshold the composite completes;
/// otherwise it defers. The engine flips this composite to <c>Completed</c> on the first satisfying
/// completion and short-circuits any later sibling evaluations, so the join never double-completes.
/// </para>
/// <para>
/// <b>No cross-branch output collision.</b> Each branch is forked under a distinct <c>BranchId</c> and is
/// a distinct executable node in its own slot, so branch outputs are recorded against distinct executions
/// and never overwrite each other.
/// </para>
/// <para>
/// <b>Fault-aware join (#308).</b> Branch faults are propagated to this composite via
/// <see cref="IActivityChildFaultHandler"/> (the engine rides a child-fault parent-evaluation work item on the
/// branch's fault incident), so the join resolves deterministically instead of hanging. The join counts each
/// branch by terminal disposition: it completes with <c>Done</c> once enough branches reach <c>Completed</c>
/// to meet the threshold; it <b>faults the composite</b> (surfacing a composite incident) once too many
/// branches reach a terminal non-success state (<c>Faulted</c>/<c>Cancelled</c>) for the remaining branches to
/// ever reach the threshold; otherwise it defers. With the default (all-branches) threshold a single faulted
/// branch therefore faults the composite; a configured threshold low enough to be met by the non-faulted
/// branches still completes. The faulted branch keeps its own blocking incident regardless. The flowchart
/// fork/join (<c>Flowchart</c> / <c>ParallelJoinFlowchartPolicy</c>) is fault-aware too: a faulted inbound
/// branch faults the flowchart deterministically rather than hanging the join.
/// </para>
/// <para>
/// The runtime activity class references only the runtime contract surface; the design-side
/// <c>ParallelStructureHandler</c> references <c>Elsa.Workflows.Design.Core</c> (Elsa §E2.2).
/// </para>
/// </remarks>
[ActivityStructure("elsa.parallel.structure", "1.0.0")]
[ActivityChildSlot(
    "Parallel.Branch",
    "branches",
    "Branches",
    ActivityChildSlotCardinalities.Single,
    CollectionProperty = "branches",
    ChildProperty = "activity",
    LabelProperty = "name",
    SlotNameTemplate = "Parallel.Branch[{name}]")]
public sealed class Parallel(IActivityExecutionStateStore activityExecutionStateStore) : ActivityBase, IActivityResult<ActivityUnit>, IActivityChildCompletionHandler, IActivityChildFaultHandler
{
    public const string BranchSlotPrefix = "Parallel.Branch[";
    public const string BranchSlotSuffix = "]";
    public const string StructureKind = "elsa.parallel.structure";
    public const string StructureSchemaVersion = "1.0.0";

    /// <summary>Builds the named child slot for a branch from its stable branch name.</summary>
    public static string BranchSlotName(string name) => $"{BranchSlotPrefix}{name}{BranchSlotSuffix}";

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var navigator = ParallelNavigator.From(runtimeContext.ExecutableNode);
        var branches = navigator.RunnableBranches;

        // No runnable branch short-circuits straight to Done without forking. (When there are no runnable
        // branches the effective threshold is 0; with any runnable branch it is at least 1.)
        if (branches.Count == 0)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return;
        }

        var compositeExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;

        // Fork: schedule ALL branches at once, each under a distinct BranchId so their executions and
        // outputs stay isolated (mirrors ParallelForkFlowchartPolicy's "schedule all outbound").
        foreach (var branch in branches)
            ScheduleBranch(runtimeContext, compositeExecutionId, branch);

        // The branches now drive the run; the join completes the composite once enough of them finish.
        runtimeContext.DeferCompositeCompletion();
    }

    public async ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = ParallelNavigator.From(runtimeContext.ExecutableNode);

        if (!navigator.IsBranch(context.CompletedChildExecutableNodeId))
            throw new ParallelExecutionException($"Completed child executable node '{context.CompletedChildExecutableNodeId}' is not a Parallel branch.");

        await ApplyJoinDecisionAsync(runtimeContext, navigator);
    }

    public async ValueTask OnChildFaultedAsync(ActivityChildFaultedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = ParallelNavigator.From(runtimeContext.ExecutableNode);

        if (!navigator.IsBranch(context.FaultedChildExecutableNodeId))
            throw new ParallelExecutionException($"Faulted child executable node '{context.FaultedChildExecutableNodeId}' is not a Parallel branch.");

        await ApplyJoinDecisionAsync(runtimeContext, navigator);
    }

    /// <summary>
    /// Evaluates the fault-aware join from the durable branch states and applies the decision: complete with
    /// <see cref="ActivityOutcomes.Done"/> once enough branches have <c>Completed</c>, fault the composite
    /// when too many branches reached a terminal non-success state for the success threshold to ever be met
    /// (#308), or defer while branches are still running. Both the completion and fault callbacks funnel
    /// through here so the decision is identical regardless of which branch event triggered it.
    /// </summary>
    private async ValueTask ApplyJoinDecisionAsync(IRuntimeActivityExecutionContext runtimeContext, ParallelNavigator navigator)
    {
        var compositeExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var (completed, terminalNonSuccess) = await CountBranchesAsync(runtimeContext, navigator, compositeExecutionId);
        var runnable = navigator.RunnableBranches.Count;
        var threshold = navigator.EffectiveThreshold;

        // Enough branches have succeeded: the join is satisfied.
        if (completed >= threshold)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return;
        }

        // The remaining branches can no longer carry the join to its success threshold, so it can never
        // complete: fault the composite (the throw is recorded by the engine as a composite incident) rather
        // than leaving it Running forever (#308).
        if (runnable - terminalNonSuccess < threshold)
            throw new ParallelExecutionException(
                $"Parallel join can no longer be satisfied: {terminalNonSuccess} of {runnable} branch(es) reached a terminal non-success state, leaving fewer than the required {threshold} successful branch(es).");

        // More branches are still running; wait for them.
        runtimeContext.DeferCompositeCompletion();
    }

    private static void ScheduleBranch(
        IRuntimeActivityExecutionContext runtimeContext,
        string compositeExecutionId,
        ExecutableNode branch)
    {
        var branchId = StructuredExecutionIdentity.Branch(compositeExecutionId, branch.ExecutableNodeId);

        runtimeContext.ScheduleChildActivity(
            branch.ExecutableNodeId,
            compositeExecutionId,
            new Dictionary<string, string>
            {
                ["parallel.parentActivityExecutionId"] = compositeExecutionId,
                ["parallel.targetNodeId"] = branch.ExecutableNodeId,
                ["parallel.branchId"] = branchId
            },
            ActivitySchedulingProvenance.From(
                runtimeContext.WorkflowExecutionId,
                parentActivityExecutionId: compositeExecutionId,
                schedulingActivityExecutionId: compositeExecutionId,
                branchId: branchId,
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "parallel.fork"));
    }

    /// <summary>
    /// Counts the distinct branch nodes of this composite by terminal disposition, by reading the durable
    /// activity-execution store: how many <c>Completed</c> (success) and how many reached a terminal
    /// non-success state (<c>Faulted</c>/<c>Cancelled</c>). Grouping by node guards against any branch
    /// contributing more than once; a branch that has any <c>Completed</c> state counts as a success.
    /// </summary>
    private async ValueTask<(int Completed, int TerminalNonSuccess)> CountBranchesAsync(
        IRuntimeActivityExecutionContext runtimeContext,
        ParallelNavigator navigator,
        string compositeExecutionId)
    {
        // Read only this composite's direct children rather than every activity-execution state in the workflow: the join
        // fires once per branch-completion/fault event, so a whole-workflow list here made the join O(branches × workflow
        // states) — effectively quadratic on wide/large workflows (#514/#413 item 3). The parent-scoped read returns exactly
        // the ParentActivityExecutionId == compositeExecutionId subset; the branch/grouping filters stay here.
        var states = await activityExecutionStateStore.ListByParentAsync(runtimeContext.WorkflowExecutionId, compositeExecutionId, runtimeContext.CancellationToken);

        var branchGroups = states
            .Where(state => navigator.IsBranch(state.Execution.ExecutableNodeId))
            .GroupBy(state => state.Execution.ExecutableNodeId, StringComparer.Ordinal);

        var completed = 0;
        var terminalNonSuccess = 0;
        foreach (var branchGroup in branchGroups)
        {
            var statuses = branchGroup.Select(state => state.Status).ToArray();
            if (statuses.Contains(ActivityExecutionStatus.Completed))
                completed++;
            else if (statuses.Any(IsTerminalNonSuccess))
                terminalNonSuccess++;
        }

        return (completed, terminalNonSuccess);
    }

    private static bool IsTerminalNonSuccess(ActivityExecutionStatus status) =>
        status is ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled;

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new ParallelExecutionException("Parallel requires an Elsa runtime activity execution context.");
    }
}
