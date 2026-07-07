using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence.Exceptions;
using Elsa.Activities.Sequence.Internal;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Sequence.Activities;

[ActivityStructure("elsa.sequence.structure", "1.0.0", Mode = "sequence", SupportsScopedVariables = true)]
[ActivityChildSlot("Sequence.Activities", "activities", "Activities", ActivityChildSlotCardinalities.Many)]
public sealed class Sequence : ActivityBase, IActivityChildCompletionHandler
{
    public const string ActivitiesSlotName = "Sequence.Activities";
    public const string StructureKind = "elsa.sequence.structure";
    public const string StructureSchemaVersion = "1.0.0";

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var navigator = SequenceNavigator.From(runtimeContext.ExecutableNode);
        var firstChild = navigator.SelectFirst();

        if (firstChild is null)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return;
        }

        runtimeContext.ScheduleChildActivity(
            firstChild.ExecutableNodeId,
            runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            new Dictionary<string, string>
            {
                ["sequence.parentActivityExecutionId"] = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                ["sequence.targetNodeId"] = firstChild.ExecutableNodeId
            });
    }

    public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
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
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Break]);
            return ValueTask.CompletedTask;
        }

        var nextChild = navigator.SelectAfter(context.CompletedChildExecutableNodeId);

        if (nextChild is null)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return ValueTask.CompletedTask;
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

        return ValueTask.CompletedTask;
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new SequenceExecutionException("Sequence requires an Elsa runtime activity execution context.");
    }
}
