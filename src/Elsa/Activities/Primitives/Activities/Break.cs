using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Exits the enclosing loop early. Ported from elsa-core's <c>Break</c> activity (#299) and adapted to this
/// repo's model: a CLR leaf (resolved by the existing <c>ClrActivityConstructor</c>, like <see cref="Fault"/>
/// and <see cref="Finish"/>) that completes with the <see cref="ActivityOutcomes.Break"/> outcome. The four
/// loop composites (<c>For</c>/<c>ForEach</c>/<c>While</c>/<c>Do</c>) detect a body that completes with that
/// outcome and end the loop instead of scheduling the next pass.
/// </summary>
/// <remarks>
/// <para>
/// The loops recognize <c>Break</c> by outcome name, so they take no dependency on this module — and this
/// leaf takes no dependency on any particular loop. The activity simply records its completion outcome
/// through <see cref="IActivityExecutionContext.SetOutcomes"/>; the runtime persists those names as the
/// activity's completion outcomes, which the parent loop reads on child completion. <c>Break</c> placed
/// outside a loop is a no-op: the outcome is recorded but no enclosing loop consumes it.
/// </para>
/// <para>
/// <b>Propagation through composites.</b> A <c>Break</c> placed inside an intermediate composite that sits
/// between the leaf and the loop only ends the loop if that composite <em>propagates</em> the <c>Break</c>
/// outcome up to its parent. The composites a loop body commonly uses do propagate it: <c>Sequence</c>
/// (stops and does not run later steps), <c>If</c> (the taken branch's <c>Break</c> becomes the <c>If</c>
/// outcome instead of True/False), and <c>Switch</c> (the selected case's <c>Break</c> becomes the
/// <c>Switch</c> outcome instead of the match outcome). Each completes itself with <c>Break</c> so the
/// outcome bubbles to the nearest enclosing loop. <b>Flowchart propagation is not yet implemented</b>
/// (it requires graph routing) and is tracked as a follow-up: a <c>Break</c> reached inside a Flowchart
/// loop body does not yet bubble out of the Flowchart.
/// </para>
/// </remarks>
public sealed class Break : CodeActivity
{
    protected override void Execute(IActivityExecutionContext context) =>
        context.SetOutcomes([ActivityOutcomes.Break]);
}
