using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
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
        runtimeContext.GetRequiredService<FlowchartExecutionEngine>().Start(runtimeContext);
    }

    public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        return runtimeContext.GetRequiredService<FlowchartExecutionEngine>().OnChildCompletedAsync(runtimeContext, context);
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new FlowchartExecutionException("Flowchart requires an Elsa runtime activity execution context.");
    }
}
