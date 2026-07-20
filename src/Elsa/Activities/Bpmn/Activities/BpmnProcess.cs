using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Activities;

/// <summary>
/// BPMN 2.0 process composite. Executes an authored BPMN element graph with token semantics: start
/// events emit tokens, sequence flows carry them, gateways fork and join them, and task-family elements
/// run bound Elsa child activities from the <c>Bpmn.Activities</c> slot. Gateway and event elements are
/// engine-interpreted; only task-family and subprocess elements execute children.
/// </summary>
/// <remarks>
/// Branch faults are fault-aware (Flowchart #308 parity): a faulted child can never complete, so its
/// token — and any downstream join requiring it — can never proceed. <see cref="OnChildFaultedAsync"/>
/// faults the composite deterministically (surfacing a composite incident); error boundary events
/// replace this rule in the events tier. The faulted leaf keeps its own blocking incident regardless.
/// </remarks>
[ActivityStructure("elsa.bpmn.structure", "1.0.0", Mode = "bpmn", SupportsScopedVariables = true)]
[ActivityChildSlot("Bpmn.Activities", "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityOutcome(ActivityOutcomes.Done)]
public sealed class BpmnProcess(BpmnExecutionEngine executionEngine) : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildFaultHandler
{
    public const string ActivitiesSlotName = "Bpmn.Activities";
    public const string StructureKind = "elsa.bpmn.structure";
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

        throw new BpmnExecutionException("BpmnProcess requires an Elsa runtime activity execution context.");
    }
}
