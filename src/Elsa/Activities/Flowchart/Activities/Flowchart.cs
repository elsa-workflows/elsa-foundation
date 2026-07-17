using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Flowchart.Activities;

/// <summary>
/// Graph-structured composite. Schedules nodes by following outbound connections as children complete, with
/// implicit/parallel joins that wait for every inbound branch to arrive before the reconverged node runs.
/// </summary>
/// <remarks>
/// <b>Fault-aware joins (#308).</b> Branch faults are propagated to the Flowchart via
/// <see cref="IRuntimeActivityChildFaultHandler"/> (the engine rides a child-fault parent-evaluation work item on the
/// faulted child's incident). A faulted inbound branch can never produce the completion a join is waiting for,
/// and a Flowchart join requires <em>every</em> inbound branch, so the join can never fire. Rather than leave
/// the Flowchart Running forever, <see cref="OnChildFaultedAsync"/> faults the Flowchart deterministically
/// (surfacing a composite incident), mirroring the <c>Parallel</c> composite's default-threshold behavior where
/// a single faulted branch faults the join. The faulted leaf keeps its own blocking incident regardless.
/// </remarks>
[ActivityStructure("elsa.flowchart.structure", "1.0.0", Mode = "flowchart", SupportsScopedVariables = true)]
[ActivityChildSlot("Flowchart.Activities", "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityOutcome(ActivityOutcomes.Done)]
[ActivityOutcome(ActivityOutcomes.Break)]
public sealed class Flowchart(FlowchartExecutionEngine executionEngine) : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildFaultHandler
{
    public const string ActivitiesSlotName = "Flowchart.Activities";
    public const string StructureKind = "elsa.flowchart.structure";
    public const string StructureSchemaVersion = "1.0.0";

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context) => executionEngine.StartAsync(context);

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        return executionEngine.OnChildCompletedAsync(runtimeContext, context);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        return executionEngine.OnChildFaultedAsync(runtimeContext, context);
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new FlowchartExecutionException("Flowchart requires an Elsa runtime activity execution context.");
    }
}
