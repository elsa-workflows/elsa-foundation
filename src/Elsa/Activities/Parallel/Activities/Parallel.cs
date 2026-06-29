using Elsa.Activities.Parallel.Exceptions;
using Elsa.Activities.Parallel.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
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
/// <b>Faulted branches are not counted (known limitation, #308).</b> The join counts only branch children
/// that reach <c>Completed</c>; a branch that <b>faults</b> is never counted. With the default (all-branches)
/// threshold, one faulted branch leaves the join unsatisfied, so the <c>Parallel</c> composite stays
/// <c>Running</c> indefinitely (there is no composite incident or timeout). This mirrors the existing
/// flowchart fork/join contract and is a documented limitation, not a bug; fault-aware join is tracked in
/// #308. A configured threshold low enough to be met by the non-faulted branches still completes.
/// </para>
/// <para>
/// The runtime activity class references only the runtime contract surface; the design-side
/// <c>ParallelStructureHandler</c> references <c>Elsa.Workflows.Design.Core</c> (Elsa §E2.2).
/// </para>
/// </remarks>
public sealed class Parallel : ActivityBase, IActivityChildCompletionHandler
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

        var compositeExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var completedBranchCount = await CountCompletedBranchesAsync(runtimeContext, navigator, compositeExecutionId);

        // Join: complete once the join condition is met (default = all branches), otherwise wait for more.
        if (completedBranchCount >= navigator.EffectiveThreshold)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return;
        }

        runtimeContext.DeferCompositeCompletion();
    }

    private static void ScheduleBranch(
        IRuntimeActivityExecutionContext runtimeContext,
        string compositeExecutionId,
        ExecutableNode branch)
    {
        var branchId = $"{compositeExecutionId}:parallel-branch:{branch.ExecutableNodeId}";

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
    /// Counts the distinct branch nodes of this composite that have finished, by reading the durable
    /// activity-execution store. Distinct-by-node guards against any branch contributing more than once.
    /// </summary>
    private static async ValueTask<int> CountCompletedBranchesAsync(
        IRuntimeActivityExecutionContext runtimeContext,
        ParallelNavigator navigator,
        string compositeExecutionId)
    {
        var store = runtimeContext.GetRequiredService<IActivityExecutionStateStore>();
        var states = await store.ListAsync(runtimeContext.WorkflowExecutionId, runtimeContext.CancellationToken);

        return states
            .Where(state =>
                state.Status == ActivityExecutionStatus.Completed &&
                StringComparer.Ordinal.Equals(state.ParentActivityExecutionId, compositeExecutionId) &&
                navigator.IsBranch(state.Execution.ExecutableNodeId))
            .Select(state => state.Execution.ExecutableNodeId)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new ParallelExecutionException("Parallel requires an Elsa runtime activity execution context.");
    }
}
