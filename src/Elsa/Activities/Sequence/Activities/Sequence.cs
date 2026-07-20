using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence.Exceptions;
using Elsa.Activities.Sequence.Internal;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Sequence.Activities;

[ActivityStructure("elsa.sequence.structure", "1.0.0", Mode = "sequence", SupportsScopedVariables = true)]
[ActivityChildSlot("Sequence.Activities", "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityOutcome(ActivityOutcomes.Done)]
[ActivityOutcome(ActivityOutcomes.Break)]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class Sequence : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
{
    public const string ActivitiesSlotName = "Sequence.Activities";
    public const string StructureKind = "elsa.sequence.structure";
    public const string StructureSchemaVersion = "1.0.0";

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext runtimeContext)
    {
        var navigator = SequenceNavigator.From(runtimeContext.ExecutableNode);
        var firstChild = navigator.SelectFirst();

        if (firstChild is null)
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        runtimeContext.ScheduleChildActivity(
            firstChild.ExecutableNodeId,
            runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            new Dictionary<string, string>
            {
                ["sequence.parentActivityExecutionId"] = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                ["sequence.targetNodeId"] = firstChild.ExecutableNodeId
            });

        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = SequenceNavigator.From(runtimeContext.ExecutableNode);

        // Break propagation (#299): a child that completes with a Break outcome stops the sequence — later
        // steps do not run — and the sequence itself completes with Break, so the outcome bubbles up to the
        // nearest enclosing loop (which ends early). Matched by name so Sequence takes no dependency on the
        // (separate) Break activity module.
        if (context.OutcomeNames.Contains(ActivityOutcomes.Break, StringComparer.Ordinal))
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete(ActivityOutcomes.Break));
        }

        var nextChild = navigator.SelectAfter(context.CompletedChildExecutableNodeId);

        if (nextChild is null)
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        runtimeContext.ScheduleChildActivity(
            nextChild.ExecutableNodeId,
            context.CompletedChildActivityExecutionId,
            new Dictionary<string, string>
            {
                ["sequence.parentActivityExecutionId"] = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                ["sequence.completedChildActivityExecutionId"] = context.CompletedChildActivityExecutionId,
                ["sequence.completedChildExecutableNodeId"] = context.CompletedChildExecutableNodeId,
                ["sequence.targetNodeId"] = nextChild.ExecutableNodeId
            });

        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new SequenceExecutionException("Sequence requires an Elsa runtime activity execution context.");
    }
}
