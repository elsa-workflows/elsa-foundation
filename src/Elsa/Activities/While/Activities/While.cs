using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.While.Exceptions;
using Elsa.Activities.While.Internal;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.While.Activities;

/// <summary>
/// Conditional-loop composite. Evaluates a boolean <see cref="Condition"/> input <b>before</b> each pass
/// and, while it holds, schedules the single <c>Body</c> branch (in the <see cref="BodySlotName"/> child
/// slot). The condition is re-evaluated on entry and again after every body completion, so the body runs
/// zero or more times: a condition that is false on entry completes the composite immediately without ever
/// running the body. The composite completes with a <see cref="ActivityOutcomes.Done"/> outcome once the
/// condition no longer holds.
/// </summary>
/// <remarks>
/// <para>
/// Each pass is scheduled with a distinct engine <see cref="ActivitySchedulingProvenance.IterationId"/>
/// (ADR 0028 / #259): the loop owner publishes a per-pass iteration identity and threads it through the
/// body child's scheduling provenance, so the body execution's
/// <see cref="ActivityExecutionState.IterationId"/> is distinct for every pass. <c>While</c> is a
/// condition-only loop with no current item or index, so it declares no per-iteration item variable; the
/// iteration identity is its per-pass loop state.
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
/// <b>Known limitation (#286).</b> A <b>workflow-scope</b> variable mutated mid-run by the body (e.g. via
/// a SetVariable activity) does <em>not</em> yet flow back into materialization: workflow variables are
/// seeded at start only, so the condition keeps seeing the start-time value and a <c>While</c> over such a
/// variable will <b>not</b> terminate until #286 (mid-run workflow-variable write-back) lands. Use an
/// activity output or a container-scoped variable for the loop condition until then.
/// </para>
/// <para>
/// An unbound or null <see cref="Condition"/> resolves to <c>false</c> (the default of <c>bool</c>),
/// mirroring <c>If</c>: a <c>While</c> with no condition wired up never runs its body and completes
/// immediately.
/// </para>
/// </remarks>
public sealed class While : ActivityBase, IActivityChildCompletionHandler
{
    public const string BodySlotName = "While.Body";
    public const string StructureKind = "elsa.while.structure";
    public const string StructureSchemaVersion = "1.0.0";

    /// <summary>The boolean condition evaluated before each pass; the body runs while it holds.</summary>
    public InputArgument<bool> Condition { get; set; } = null!;

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var condition = context.Get(Condition);

        // First condition evaluation: false on entry means the body never runs.
        ContinueOrComplete(runtimeContext, condition, completedChildActivityExecutionId: null);
    }

    public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);

        // The runtime re-materializes this composite's inputs for every child-completion evaluation, so
        // this read reflects any state the body mutated this pass: the condition is re-evaluated before
        // the next pass.
        var condition = context.ParentContext.Get(Condition);

        ContinueOrComplete(runtimeContext, condition, context.CompletedChildActivityExecutionId);
        return ValueTask.CompletedTask;
    }

    private static void ContinueOrComplete(
        IRuntimeActivityExecutionContext runtimeContext,
        bool condition,
        string? completedChildActivityExecutionId)
    {
        var navigator = WhileNavigator.From(runtimeContext.ExecutableNode);

        if (!condition || navigator.Body is not { } body)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return;
        }

        var compositeExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var iterationId = NewIterationId(compositeExecutionId, completedChildActivityExecutionId);

        runtimeContext.ScheduleChildActivity(
            body.ExecutableNodeId,
            compositeExecutionId,
            new Dictionary<string, string>
            {
                ["while.parentActivityExecutionId"] = compositeExecutionId,
                ["while.targetNodeId"] = body.ExecutableNodeId,
                ["while.iterationId"] = iterationId
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
    /// from the composite execution id plus the completed child's execution id (the entry pass uses the
    /// fixed <c>:iter:0</c> seed). Every body execution has a unique activity-execution id, so successive
    /// passes never share an iteration id (ADR 0028: a distinct <c>IterationId</c> per pass).
    /// </summary>
    private static string NewIterationId(string compositeExecutionId, string? completedChildActivityExecutionId) =>
        $"{compositeExecutionId}:iter:{completedChildActivityExecutionId ?? "0"}";

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new WhileExecutionException("While requires an Elsa runtime activity execution context.");
    }
}
