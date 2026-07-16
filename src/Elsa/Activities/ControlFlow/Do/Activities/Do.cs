using Elsa.Activities.Do.Exceptions;
using Elsa.Activities.Do.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Do.Activities;

/// <summary>
/// Post-test conditional-loop composite (<c>Do</c>/<c>DoWhile</c>). Schedules its single <c>Body</c>
/// branch (in the <see cref="BodySlotName"/> child slot) <b>before</b> any condition check and then
/// re-evaluates the boolean <see cref="Condition"/> input <b>after</b> every body completion, scheduling
/// the body again while it holds. Because the first pass runs unconditionally, the body runs <b>at least
/// once</b> even when the condition is false on entry — this is the post-test counterpart to <c>While</c>
/// (which checks the condition first and may run the body zero times). The composite completes with a
/// <see cref="ActivityOutcomes.Done"/> outcome once the condition no longer holds (or a body
/// <c>Break</c> outcome ends the loop early).
/// </summary>
/// <remarks>
/// <para>
/// Each pass is scheduled with a distinct engine <see cref="ActivitySchedulingProvenance.IterationId"/>
/// (ADR 0028 / #259): the loop owner publishes a per-pass iteration identity and threads it through the
/// body child's scheduling provenance, so the body execution's
/// <see cref="ActivityExecutionState.IterationId"/> is distinct for every pass. <c>Do</c> is a
/// condition-only loop with no current item or index, so it declares no per-iteration item variable; the
/// iteration identity is its per-pass loop state.
/// </para>
/// <para>
/// <b>Early exit.</b> A body that completes with a <c>Break</c> outcome ends the loop early with
/// <see cref="ActivityOutcomes.Done"/> without re-checking the condition. <c>Break</c> is recognized by
/// outcome name so the loop does not take a hard dependency on the (separate) Break activity module.
/// </para>
/// <para>
/// <b>What the body can change to make the loop terminate.</b> The runtime re-materializes this
/// composite's inputs before every condition re-evaluation, drawing them from persisted runtime state.
/// So the condition terminates the loop when it reads state the body actually <em>persists</em> per pass:
/// an <b>activity output</b> the body produces (e.g. a counter activity whose output the condition
/// compares), or a <b>container-scoped variable</b> the body mutates (ADR 0027 scope mutations are
/// written back and re-projected each pass). For example, a body that produces output <c>count</c> with a
/// condition <c>count &lt; 3</c> runs three times and then stops.
/// </para>
/// <para>
/// A <b>workflow-scope</b> variable mutated mid-run by the body (e.g. via a SetVariable activity) also drives
/// the condition: the mutation is persisted as a durable value and re-projected into <c>variables.*</c> each
/// pass (#286 mid-run write-back), so a <c>Do</c> whose condition reads a workflow-scope variable the body
/// updates terminates just like the activity-output and container-scoped-variable cases.
/// </para>
/// <para>
/// An unbound or null <see cref="Condition"/> resolves to <c>false</c> (the default of <c>bool</c>): a
/// <c>Do</c> with no condition wired up still runs its body once (the unconditional first pass) and then
/// completes, because the post-completion re-evaluation reads <c>false</c>.
/// </para>
/// </remarks>
[ActivityStructure("elsa.do.structure", "1.0.0")]
[ActivityChildSlot("Do.Body", "body", "Body", ActivityChildSlotCardinalities.Single)]
public sealed class Do : ActivityBase, IActivityResult<ActivityUnit>, IActivityChildCompletionHandler
{
    public const string BodySlotName = "Do.Body";
    public const string StructureKind = "elsa.do.structure";
    public const string StructureSchemaVersion = "1.0.0";

    /// <summary>The outcome a body emits to break out of the loop early (#299); matched by name.</summary>
    public const string BreakOutcome = ActivityOutcomes.Break;

    /// <summary>The boolean condition evaluated after each pass; the body repeats while it holds.</summary>
    [ActivityInput(Key = nameof(Condition))]
    public bool Condition { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);

        // Post-test loop: schedule the body unconditionally before the first condition check, so it runs
        // at least once even when the condition is false on entry.
        var navigator = DoNavigator.From(runtimeContext.ExecutableNode);

        if (navigator.Body is not { } body)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return;
        }

        ScheduleBody(runtimeContext, body, completedChildActivityExecutionId: null);
    }

    public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = DoNavigator.From(runtimeContext.ExecutableNode);

        // #381: validate the completed child is actually this loop's body (mirroring For/ForEach/If/
        // Switch/Parallel) so a stray callback fails diagnosably instead of silently rescheduling.
        if (!navigator.IsBody(context.CompletedChildExecutableNodeId))
            throw new DoExecutionException($"Completed child executable node '{context.CompletedChildExecutableNodeId}' is not the Do body.");

        // A body Break outcome ends the loop early without re-checking the condition.
        if (context.OutcomeNames.Contains(BreakOutcome, StringComparer.Ordinal))
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return ValueTask.CompletedTask;
        }

        // The runtime re-materializes this composite's inputs for every child-completion evaluation, so
        // this read reflects any state the body mutated this pass: the condition is re-evaluated after the
        // just-completed pass and the body repeats while it holds.
        if (!Condition || navigator.Body is not { } body)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return ValueTask.CompletedTask;
        }

        ScheduleBody(runtimeContext, body, context.CompletedChildActivityExecutionId);
        return ValueTask.CompletedTask;
    }

    private static void ScheduleBody(
        IRuntimeActivityExecutionContext runtimeContext,
        ExecutableNode body,
        string? completedChildActivityExecutionId)
    {
        var compositeExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var iterationId = NewIterationId(compositeExecutionId, completedChildActivityExecutionId);

        runtimeContext.ScheduleChildActivity(
            body.ExecutableNodeId,
            compositeExecutionId,
            new Dictionary<string, string>
            {
                ["do.parentActivityExecutionId"] = compositeExecutionId,
                ["do.targetNodeId"] = body.ExecutableNodeId,
                ["do.iterationId"] = iterationId
            },
            ActivitySchedulingProvenance.From(
                runtimeContext.WorkflowExecutionId,
                parentActivityExecutionId: compositeExecutionId,
                schedulingActivityExecutionId: compositeExecutionId,
                branchId: null,
                iterationId: iterationId,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: null));
    }

    /// <summary>
    /// Builds a per-pass iteration identity that is distinct for every pass and derivable statelessly
    /// from the composite execution id plus the completed child's execution id (the first pass uses the
    /// fixed <c>:iter:0</c> seed). Every body execution has a unique activity-execution id, so successive
    /// passes never share an iteration id (ADR 0028: a distinct <c>IterationId</c> per pass).
    /// </summary>
    private static string NewIterationId(string compositeExecutionId, string? completedChildActivityExecutionId) =>
        $"{compositeExecutionId}:iter:{completedChildActivityExecutionId ?? "0"}";

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new DoExecutionException("Do requires an Elsa runtime activity execution context.");
    }
}
