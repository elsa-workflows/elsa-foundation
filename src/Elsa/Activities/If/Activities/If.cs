using Elsa.Activities.If.Exceptions;
using Elsa.Activities.If.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.If.Activities;

/// <summary>
/// Boolean control-flow composite. Evaluates a <see cref="Condition"/> input and schedules either the
/// <c>Then</c> branch (when true) or the <c>Else</c> branch (when false). The branches live in named
/// child slots (<see cref="ThenSlotName"/> / <see cref="ElseSlotName"/>); which slot child is the
/// <c>Then</c> branch and which is the <c>Else</c> branch is recorded in the compiled structure. The
/// composite completes with a <see cref="ActivityOutcomes.True"/> or <see cref="ActivityOutcomes.False"/>
/// outcome reflecting the evaluated condition.
/// </summary>
/// <remarks>
/// An unbound <see cref="Condition"/> resolves to <c>false</c> (the default of <c>bool</c>), so an
/// <c>If</c> with no condition wired up runs the <c>Else</c> branch and emits <see cref="ActivityOutcomes.False"/>.
/// </remarks>
public sealed class If : ActivityBase, IActivityChildCompletionHandler
{
    public const string ThenSlotName = "If.Then";
    public const string ElseSlotName = "If.Else";
    public const string StructureKind = "elsa.if.structure";
    public const string StructureSchemaVersion = "1.0.0";

    /// <summary>The boolean condition that selects the branch to run.</summary>
    public InputArgument<bool> Condition { get; set; } = null!;

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var condition = context.Get(Condition);
        var navigator = IfNavigator.From(runtimeContext.ExecutableNode);
        var branch = navigator.Select(condition);

        if (branch is null)
        {
            runtimeContext.CompleteCompositeActivity([Outcome(condition)]);
            return;
        }

        runtimeContext.ScheduleChildActivity(
            branch.ExecutableNodeId,
            runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            new Dictionary<string, string>
            {
                ["if.parentActivityExecutionId"] = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                ["if.targetNodeId"] = branch.ExecutableNodeId
            });
    }

    public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var navigator = IfNavigator.From(runtimeContext.ExecutableNode);
        var condition = ResolveCompletedBranchCondition(navigator, context.CompletedChildExecutableNodeId);

        runtimeContext.CompleteCompositeActivity([Outcome(condition)]);
        return ValueTask.CompletedTask;
    }

    private static bool ResolveCompletedBranchCondition(IfNavigator navigator, string completedChildExecutableNodeId)
    {
        if (navigator.Then is { } then && StringComparer.Ordinal.Equals(then.ExecutableNodeId, completedChildExecutableNodeId))
            return true;

        if (navigator.Else is { } @else && StringComparer.Ordinal.Equals(@else.ExecutableNodeId, completedChildExecutableNodeId))
            return false;

        throw new IfExecutionException($"Completed child executable node '{completedChildExecutableNodeId}' is not an If branch.");
    }

    private static string Outcome(bool condition) => condition ? ActivityOutcomes.True : ActivityOutcomes.False;

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new IfExecutionException("If requires an Elsa runtime activity execution context.");
    }
}
