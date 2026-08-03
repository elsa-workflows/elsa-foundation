using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Exits the enclosing loop early. Ported from elsa-core's <c>Break</c> activity (#299) and adapted to this
/// repo's model: a transient CLR leaf that returns the <see cref="ActivityOutcomes.Break"/> outcome. The four
/// loop composites (<c>For</c>/<c>ForEach</c>/<c>While</c>/<c>Do</c>) detect a body that completes with that
/// outcome and end the loop instead of scheduling the next pass.
/// </summary>
/// <remarks>
/// <para>
/// The loops recognize <c>Break</c> by outcome name, so they take no dependency on this module — and this
/// leaf takes no dependency on any particular loop. The activity returns its outcome atomically with its
/// unit result; the runtime persists that completion, which the parent loop reads. <c>Break</c> placed
/// outside a loop is a no-op: the outcome is recorded but no enclosing loop consumes it.
/// </para>
/// <para>
/// <b>Propagation through composites.</b> A <c>Break</c> placed inside an intermediate composite that sits
/// between the leaf and the loop only ends the loop if that composite <em>propagates</em> the <c>Break</c>
/// outcome up to its parent. The composites a loop body commonly uses do propagate it: <c>Sequence</c>
/// (stops and does not run later steps), <c>If</c> (the taken branch's <c>Break</c> becomes the <c>If</c>
/// outcome instead of True/False), and <c>Switch</c> (the selected case's <c>Break</c> becomes the
/// <c>Switch</c> outcome instead of the match outcome). Each completes itself with <c>Break</c> so the
/// outcome bubbles to the nearest enclosing loop. <c>Flowchart</c> (#304) also propagates: when any path
/// reaches a <c>Break</c>, the Flowchart ends — it cancels any other in-flight paths (e.g. sibling parallel
/// fork branches) and completes itself with <c>Break</c> so the outcome bubbles to the enclosing loop.
/// </para>
/// </remarks>
// Break completes with the Break outcome and nothing else. Declaring it is what puts a Break port on the node in
// the catalog: without the attribute the scanner emits no outcomes facet at all, the studio falls back to its own
// "Done" default, and the designer shows a port that can never be taken while hiding the one that always is
// (found by the behavioural drive, #1119).
[ActivityOutcome(ActivityOutcomes.Break)]
public sealed class Break : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value, ActivityOutcomes.Break));
}
