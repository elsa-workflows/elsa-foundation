using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Switch.Exceptions;
using Elsa.Activities.Switch.Internal;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Switch.Activities;

/// <summary>
/// Multi-way control-flow composite. Evaluates a <see cref="Value"/> input and schedules the branch of
/// the first case whose match value equals it; when no case matches it schedules the <c>Default</c>
/// branch. Each case branch lives in its own named child slot (<see cref="CaseSlotName"/>); the default
/// branch lives in <see cref="DefaultSlotName"/>. Which slot child belongs to which case is recorded in
/// the compiled structure. The composite completes with the matched case's match value as its outcome,
/// or <see cref="Workflows.Runtime.Core.Constants.ActivityOutcomes.Default"/> when no case matched.
/// </summary>
/// <remarks>
/// An unbound or null <see cref="Value"/> takes the <c>Default</c> branch: case match values are
/// non-nullable strings and selection compares them ordinally against the value, so a null value never
/// equals any declared case.
/// </remarks>
[ActivityStructure("elsa.switch.structure", "1.0.0")]
[ActivityChildSlot(
    "Switch.Case",
    "cases",
    "Cases",
    ActivityChildSlotCardinalities.Single,
    CollectionProperty = "cases",
    ChildProperty = "activity",
    LabelProperty = "match",
    SlotNameTemplate = "Switch.Case[{match}]")]
[ActivityChildSlot("Switch.Default", "default", "Default", ActivityChildSlotCardinalities.Single)]
public sealed class Switch : ActivityBase, IActivityChildCompletionHandler
{
    public const string DefaultSlotName = "Switch.Default";
    public const string CaseSlotPrefix = "Switch.Case[";
    public const string CaseSlotSuffix = "]";
    public const string StructureKind = "elsa.switch.structure";
    public const string StructureSchemaVersion = "1.0.0";

    /// <summary>Builds the named child slot for a case's branch from its match value.</summary>
    public static string CaseSlotName(string match) => $"{CaseSlotPrefix}{match}{CaseSlotSuffix}";

    /// <summary>The value matched against each case's declared match value to select the branch to run.</summary>
    public InputArgument<string> Value { get; set; } = null!;

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var value = context.Get(Value);
        var navigator = SwitchNavigator.From(runtimeContext.ExecutableNode);
        var branch = navigator.Select(value);

        if (branch is null)
        {
            runtimeContext.CompleteCompositeActivity([navigator.OutcomeForValue(value)]);
            return;
        }

        runtimeContext.ScheduleChildActivity(
            branch.ExecutableNodeId,
            runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            new Dictionary<string, string>
            {
                ["switch.parentActivityExecutionId"] = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                ["switch.targetNodeId"] = branch.ExecutableNodeId
            });
    }

    public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = SwitchNavigator.From(runtimeContext.ExecutableNode);

        // Validate the completed child is one of this Switch's branches before deciding the outcome.
        var outcome = navigator.OutcomeFor(context.CompletedChildExecutableNodeId);

        // Break propagation (#299): when the selected case (or default) branch completes with a Break
        // outcome, Switch completes with Break (instead of the case's match outcome) so it bubbles up to the
        // nearest enclosing loop (which ends early). Matched by name so Switch takes no dependency on the
        // (separate) Break activity module.
        if (context.OutcomeNames.Contains(ActivityOutcomes.Break, StringComparer.Ordinal))
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Break]);
            return ValueTask.CompletedTask;
        }

        runtimeContext.CompleteCompositeActivity([outcome]);
        return ValueTask.CompletedTask;
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new SwitchExecutionException("Switch requires an Elsa runtime activity execution context.");
    }
}
