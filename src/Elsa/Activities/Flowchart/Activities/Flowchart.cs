using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Flowchart.Activities;

public sealed class Flowchart : ActivityBase, IActivityChildCompletionHandler
{
    public const string ActivitiesSlotName = "Flowchart.Activities";
    public const string StructureKind = "elsa.flowchart.structure";
    public const string StructureSchemaVersion = "1.0.0";

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);
        var graph = FlowchartGraph.From(runtimeContext.ExecutableNode);
        var startNode = graph.SelectStartNode();

        if (startNode is null)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return;
        }

        runtimeContext.ScheduleChildActivity(
            startNode.ExecutableNodeId,
            runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
            new Dictionary<string, string>
            {
                ["flowchart.parentActivityExecutionId"] = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                ["flowchart.startNodeId"] = startNode.ExecutableNodeId
            });
    }

    public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        var graph = FlowchartGraph.From(runtimeContext.ExecutableNode);
        var targets = graph.SelectTargets(context.CompletedChildExecutableNodeId, context.OutcomeNames);

        if (targets.Count == 0)
        {
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return ValueTask.CompletedTask;
        }

        foreach (var target in targets)
        {
            runtimeContext.ScheduleChildActivity(
                target.ExecutableNodeId,
                context.CompletedChildActivityExecutionId,
                new Dictionary<string, string>
                {
                    ["flowchart.parentActivityExecutionId"] = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                    ["flowchart.completedChildActivityExecutionId"] = context.CompletedChildActivityExecutionId,
                    ["flowchart.completedChildExecutableNodeId"] = context.CompletedChildExecutableNodeId,
                    ["flowchart.targetNodeId"] = target.ExecutableNodeId
                });
        }

        return ValueTask.CompletedTask;
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new FlowchartExecutionException("Flowchart requires an Elsa runtime activity execution context.");
    }
}
