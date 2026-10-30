using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Attributes;
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
[RefusesSecretBinding(nameof(CanStartWorkflow), SecretBindingRefusalReason.FixedAtPublish)]
[Activity(Icon = "Workflow")]
[TriggerActivity]
[ActivityStructure("elsa.bpmn.structure", "1.0.0", Mode = "bpmn", SupportsScopedVariables = true)]
[ActivityChildSlot("Bpmn.Activities", "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityOutcome(ActivityOutcomes.Done)]
[ActivityChildControlConsumer(BpmnControlProtocol.Key, BpmnControlProtocol.Version)]
public sealed class BpmnProcess(IBpmnProcessRuntime executionEngine) : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildFaultHandler, IRuntimeActivityChildNotificationHandler, IRuntimeActivityChildOutcomeHandler, IRuntimeLiveChildActivityConsumer, IRuntimeScopedVariableReader, IRuntimeActivityChildControlConsumer
{
    public const string ActivitiesSlotName = "Bpmn.Activities";
    public const string StructureKind = "elsa.bpmn.structure";
    public const string StructureSchemaVersion = "1.0.0";

    /// <summary>
    /// Whether this process may be started from an event-defined start element's stimulus. Defaults to
    /// <c>true</c> (root-process identity). A <c>BpmnProcess</c> bound as a child inside another workflow/process
    /// sets it to <c>false</c> so its event-defined start events register no start triggers — the publish-time
    /// trigger provider skips it. Root position is not recoverable from the published node alone, so this is the
    /// opt-out; a none-start process is unaffected either way (it starts on direct invocation).
    /// </summary>
    [ActivityInput(Key = nameof(CanStartWorkflow), DisplayName = "Start new workflows", Description = "Allow event-defined start events to start new workflow instances. When off, this process can still run inside an existing workflow.", DefaultValue = "true")]
    public bool CanStartWorkflow { get; set; } = true;

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context) => executionEngine.StartAsync(context);

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        return executionEngine.CompleteAsync(runtimeContext, context);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var runtimeContext = RequireRuntimeContext(context.ParentContext);
        return executionEngine.FaultAsync(runtimeContext, context);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(notification);

        return executionEngine.SignalAsync(context, notification);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildOutcomeAsync(ActivityChildOutcomeContext context) =>
        executionEngine.OutcomeAsync(RequireRuntimeContext(context.ParentContext), context);

    public ValueTask<RuntimeStructuralContinuation> OnChildControlAsync(RuntimeChildControlContext context) =>
        executionEngine.ControlAsync(context.ParentContext, context);

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new BpmnHostException("BpmnProcess requires an Elsa runtime activity execution context.");
    }
}
