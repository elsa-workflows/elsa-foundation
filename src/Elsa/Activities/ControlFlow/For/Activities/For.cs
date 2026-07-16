using System.Globalization;
using System.Text.Json;
using Elsa.Activities.For.Exceptions;
using Elsa.Activities.For.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.For.Activities;

/// <summary>
/// Counted-loop composite. Runs its body once for each index in the numeric range
/// <c>[<see cref="Start"/>, <see cref="End"/>)</c> (end exclusive by default; closed when
/// <see cref="EndInclusive"/> is true), advancing by <see cref="Step"/> each pass. Each pass exposes the
/// current index to the body as a per-iteration scoped variable (#259, ADR 0028) and is scheduled with a
/// distinct engine <c>IterationId</c> that encodes the index. When the range is exhausted (or empty) the
/// composite completes with the <see cref="ActivityOutcomes.Done"/> outcome.
/// </summary>
/// <remarks>
/// <para>
/// <b>Range/step semantics.</b> <see cref="Start"/> is inclusive. <see cref="End"/> is exclusive by
/// default (the half-open <c>[Start, End)</c> convention of <c>for (i = start; i &lt; end; i += step)</c>);
/// set <see cref="EndInclusive"/> to walk the closed <c>[Start, End]</c> range. <see cref="Step"/>
/// defaults to <c>1</c>; a positive step counts up and a negative step counts down. A step pointing away
/// from the end (e.g. a positive step with <c>End &lt; Start</c>) yields an empty range: the body never
/// runs and the loop completes immediately. A zero step is a configuration error and faults the activity.
/// </para>
/// <para>
/// The body re-runs as the same executable node with a fresh activity execution per pass. The loop holds
/// no mutable state across passes: on each child completion it recovers the just-completed index from the
/// completed child's <c>IterationId</c> and computes the next index, so it is safe under the runtime's
/// stateless re-construction of composite activities.
/// </para>
/// <para>
/// <b>Early exit.</b> A body that completes with a <c>Break</c> outcome (#261) ends the loop early with
/// <see cref="ActivityOutcomes.Done"/>; any other outcome advances to the next index. <c>Break</c> is
/// recognized by outcome name so the loop does not take a hard dependency on the (separate) Break
/// activity module.
/// </para>
/// </remarks>
[ActivityStructure("elsa.for.structure", "1.0.0")]
[ActivityChildSlot("For.Body", "body", "Body", ActivityChildSlotCardinalities.Single)]
public sealed class For : ActivityBase, IActivityResult<ActivityUnit>, IActivityChildCompletionHandler
{
    public const string BodySlotName = "For.Body";
    public const string StructureKind = "elsa.for.structure";
    public const string StructureSchemaVersion = "1.0.0";

    /// <summary>The outcome a body emits to break out of the loop early (#261/#299); matched by name.</summary>
    public const string BreakOutcome = ActivityOutcomes.Break;

    /// <summary>The reference key / authored name of the per-iteration current-index variable.</summary>
    public const string IndexVariableName = "index";

    /// <summary>The first index (inclusive). Defaults to <c>0</c> when unbound.</summary>
    [ActivityInput(Key = nameof(Start), Order = 0)]
    public int Start { get; set; }

    /// <summary>The end index. Exclusive unless <see cref="EndInclusive"/> is set. Defaults to <c>0</c> when unbound.</summary>
    [ActivityInput(Key = nameof(End), Order = 10)]
    public int End { get; set; }

    /// <summary>The amount each pass advances the index by. Defaults to <c>1</c> when unbound; must be non-zero.</summary>
    [ActivityInput(Key = nameof(Step), Order = 20, DefaultValue = "1", DefaultSyntax = "Literal")]
    public int Step { get; set; } = 1;

    /// <summary>When true the range is closed (<c>[Start, End]</c>); otherwise half-open (<c>[Start, End)</c>).</summary>
    [ActivityInput(Key = nameof(EndInclusive), Order = 30)]
    public bool EndInclusive { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var range = ResolveRange();
        var navigator = ForNavigator.From(runtimeContext.ExecutableNode);

        if (!range.HasFirst || navigator.Body is null)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return;
        }

        ScheduleBody(runtimeContext, navigator.Body, range.Start);
    }

    public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = ForNavigator.From(runtimeContext.ExecutableNode);

        if (!navigator.IsBody(context.CompletedChildExecutableNodeId))
            throw new ForExecutionException($"Completed child executable node '{context.CompletedChildExecutableNodeId}' is not the For body.");

        if (context.OutcomeNames.Contains(BreakOutcome, StringComparer.Ordinal))
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return ValueTask.CompletedTask;
        }

        var range = ResolveRange();
        var completedIndex = ReadIterationIndex(context);
        var nextIndex = range.Next(completedIndex);

        if (nextIndex is not { } index || navigator.Body is null)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return ValueTask.CompletedTask;
        }

        ScheduleBody(runtimeContext, navigator.Body, index);
        return ValueTask.CompletedTask;
    }

    private void ScheduleBody(IRuntimeActivityExecutionContext runtimeContext, ExecutableNode body, int index)
    {
        var ownerNodeId = runtimeContext.ExecutableNode.ExecutableNodeId;
        var iterationId = FormatIterationId(index);
        var indexEnvelope = ValueEnvelope.Inline(
            new ValueTypeDescriptor("Int32"),
            JsonSerializer.SerializeToElement(index),
            ValueProtectionPolicy.InstanceInline);

        // ADR 0028 / #259: publish this pass's iteration variable in the body child's scheduling
        // provenance. The runtime (RuntimeContainerScopeService) reads these keys to layer a fresh
        // per-iteration variable scope — exposing the current index under `index` — as the innermost
        // scope on top of the body's enclosing container chain, so the body's input expressions resolve
        // the index through the real evaluation path. The IterationId (which encodes the index) is the
        // body execution's iteration identity and agrees with that scope's execution id.
        var provenance = ActivitySchedulingProvenance.From(
            runtimeContext.WorkflowExecutionId,
            parentActivityExecutionId: runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            schedulingActivityExecutionId: runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            branchId: null,
            iterationId: iterationId,
            executionPathId: null,
            executionScopeId: null,
            schedulingCause: null,
            metadata: new Dictionary<string, string>
            {
                ["for.parentActivityExecutionId"] = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                ["for.targetNodeId"] = body.ExecutableNodeId
            });

        runtimeContext.ScheduleChildActivity(
            body.ExecutableNodeId,
            runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            metadata: provenance.Metadata,
            schedulingProvenance: provenance,
            iterationFrame: new LoopIterationScopeRequest(
                ownerNodeId,
                iterationId,
                new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal)
                {
                    [IndexVariableName] = indexEnvelope
                }));
    }

    private ForRange ResolveRange() => ForRange.Create(Start, End, Step, EndInclusive);

    private static string FormatIterationId(int index) => index.ToString(CultureInfo.InvariantCulture);

    private static int ReadIterationIndex(ActivityChildCompletedContext context)
    {
        if (context.CompletedChildIterationId is not { } iterationId
            || !int.TryParse(iterationId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            throw new ForExecutionException(
                $"For body completion '{context.CompletedChildActivityExecutionId}' carried no parseable iteration index (IterationId '{context.CompletedChildIterationId}').");

        return index;
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new ForExecutionException("For requires an Elsa runtime activity execution context.");
    }
}
