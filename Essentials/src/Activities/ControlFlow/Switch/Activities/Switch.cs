using Elsa.Activities.ControlFlow.Exceptions;
using Elsa.Activities.ControlFlow.Results;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Switch.Internal;
using Elsa.Activities.Switch.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Switch.Activities;

/// <summary>
/// Runs the first case whose pinned Boolean condition is true, or the default branch when none match.
/// Retained legacy cases compare the pinned Value ordinally with their labels. Each case owns one
/// named child slot and completes with its authored label; a selected empty case completes directly.
/// </summary>
/// <remarks>
/// Schema 1 retains ordinary Value matching. Schema 2 uses structure-owned inputs pinned before
/// activation, evaluates only cases reached in order, and treats an unbound predicate as false.
/// </remarks>
[Activity(Icon = "Split")]
[Version("4.1.0")]
[ActivityStructure("elsa.switch.structure", "2.0.0")]
[ActivityOutcome(ActivityOutcomes.Default)]
[ActivityOutcome(ActivityOutcomes.Break)]
[SelectsOutcomeOnEveryCompletion]
[ActivityChildSlot(
    "Switch.Case",
    "cases",
    "Cases",
    ActivityChildSlotCardinalities.Single,
    CollectionProperty = "cases",
    ChildProperty = "activity",
    LabelProperty = "match",
    ItemIdProperty = "caseId",
    ConditionProperty = "condition",
    SlotNameTemplate = "Switch.Case[{match}]")]
[ActivityChildSlot("Switch.Default", "default", "Default", ActivityChildSlotCardinalities.Single)]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class Switch(IActivityExecutionStateStore activityExecutionStateStore) : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler, IRuntimeStructureInputConsumer
{
    public const string DefaultSlotName = "Switch.Default";
    public const string CaseSlotPrefix = "Switch.Case[";
    public const string CaseSlotSuffix = "]";
    public const string StructureKind = "elsa.switch.structure";
    public const string LegacyStructureSchemaVersion = "1.0.0";
    public const string StructureSchemaVersion = "2.0.0";
    private IReadOnlyDictionary<string, ValueEnvelope> _structureInputs = new Dictionary<string, ValueEnvelope>();
    private bool _structureInputsBound;

    /// <summary>Builds the named child slot for a case's branch from its match value.</summary>
    public static string CaseSlotName(string match) => $"{CaseSlotPrefix}{match}{CaseSlotSuffix}";

    /// <summary>The value matched against each case's declared match value to select the branch to run.</summary>
    [ActivityInput(Key = nameof(Value), Tier = InputTier.Optional,
        Description = "Used by retained literal-match cases. Configure new cases with their own conditions.")]
    public string? Value { get; set; }

    public static string ConditionInputKey(string caseId) => $"Case:{caseId}:Condition";

    public void BindStructureInputs(IReadOnlyDictionary<string, ValueEnvelope> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (_structureInputsBound) throw new ControlFlowExecutionException("Switch structure inputs have already been bound.");
        _structureInputs = inputs;
        _structureInputsBound = true;
    }

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext runtimeContext)
    {
        var navigator = runtimeContext.ExecutableNode.GetOrAddRoutingStructure(SwitchNavigator.From);
        if (navigator.MatchesAny)
        {
            var matches = navigator.SelectCases(Value, _structureInputs);
            if (matches.Count > 0)
            {
                foreach (var item in matches.Where(item => item.Branch is not null))
                    ScheduleBranch(runtimeContext, item.Branch!, concurrent: true);
                return ValueTask.FromResult(matches.Any(item => item.Branch is not null)
                    ? RuntimeStructuralContinuation.Defer
                    : RuntimeStructuralContinuation.Complete(OutcomeSelection.Named(matches.Select(item => item.Match))));
            }
        }

        var selection = navigator.Select(Value, _structureInputs);
        var branch = selection.Branch;

        if (branch is null)
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete(selection.Outcome));
        }

        ScheduleBranch(runtimeContext, branch, concurrent: false);

        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public async ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = runtimeContext.ExecutableNode.GetOrAddRoutingStructure(SwitchNavigator.From);

        // Validate the completed child is one of this Switch's branches before deciding the outcome.
        var outcome = navigator.OutcomeFor(context.CompletedChildExecutableNodeId);

        if (navigator.MatchesAny)
        {
            var matches = navigator.SelectCases(Value, _structureInputs);
            var selected = matches.Where(item => item.Branch is not null).Select(item => item.Branch!.ExecutableNodeId).ToHashSet(StringComparer.Ordinal);
            if (matches.Count == 0 && navigator.Default is { } fallback)
                selected.Add(fallback.ExecutableNodeId);
            if (!selected.Contains(context.CompletedChildExecutableNodeId))
                throw new ControlFlowExecutionException("Completed child is not a selected Switch case body.");

            var parentId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
            var states = await activityExecutionStateStore.ListAllByParentAsync(runtimeContext.WorkflowExecutionId, parentId, runtimeContext.CancellationToken);
            var completed = states.Where(state => state.ParentActivityExecutionId == parentId
                    && state.Execution.WorkflowExecutionId == runtimeContext.WorkflowExecutionId
                    && state.Status == ActivityExecutionStatus.Completed && state.SupersededByActivityExecutionId is null)
                .ToArray();
            if (!completed.Any(state => state.Execution.ActivityExecutionId == context.CompletedChildActivityExecutionId
                    && state.Execution.ExecutableNodeId == context.CompletedChildExecutableNodeId))
                throw new ControlFlowExecutionException("Completed child does not belong to the current Switch invocation.");

            if (context.OutcomeNames.Contains(ActivityOutcomes.Break, StringComparer.Ordinal)
                || completed.Any(state => selected.Contains(state.Execution.ExecutableNodeId)
                    && state.Completion?.OutcomeNames.Contains(ActivityOutcomes.Break, StringComparer.Ordinal) == true))
            {
                runtimeContext.RequestLiveChildrenSubtreeCancellation("switch.break");
                return RuntimeStructuralContinuation.Complete(ActivityOutcomes.Break);
            }

            var successes = completed.Select(state => state.Execution.ExecutableNodeId).ToHashSet(StringComparer.Ordinal);
            return selected.IsSubsetOf(successes)
                ? RuntimeStructuralContinuation.Complete(matches.Count == 0
                    ? OutcomeSelection.Named(ActivityOutcomes.Default)
                    : OutcomeSelection.Named(matches.Select(item => item.Match)))
                : RuntimeStructuralContinuation.Defer;
        }

        // Break propagation (#299): when the selected case (or default) branch completes with a Break
        // outcome, Switch completes with Break (instead of the case's match outcome) so it bubbles up to the
        // nearest enclosing loop (which ends early). Matched by name so Switch takes no dependency on the
        // (separate) Break activity module.
        if (context.OutcomeNames.Contains(ActivityOutcomes.Break, StringComparer.Ordinal))
        {
            return RuntimeStructuralContinuation.Complete(ActivityOutcomes.Break);
        }

        return RuntimeStructuralContinuation.Complete(outcome);
    }

    private static void ScheduleBranch(IRuntimeActivityExecutionContext context, ExecutableNode branch, bool concurrent)
    {
        var parentId = context.ActivityExecutionState.Execution.ActivityExecutionId;
        var provenance = concurrent
            ? ActivitySchedulingProvenance.From(context.WorkflowExecutionId, parentId, parentId,
                branchId: StructuredExecutionIdentity.Branch(parentId, branch.ExecutableNodeId), iterationId: null,
                executionPathId: null, executionScopeId: null, schedulingCause: "switch.matchAny")
            : null;
        context.ScheduleChildActivity(branch.ExecutableNodeId, parentId,
            new Dictionary<string, string>
            {
                ["switch.parentActivityExecutionId"] = parentId,
                ["switch.targetNodeId"] = branch.ExecutableNodeId
            }, provenance);
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new ControlFlowExecutionException("Switch requires an Elsa runtime activity execution context.");
    }
}
