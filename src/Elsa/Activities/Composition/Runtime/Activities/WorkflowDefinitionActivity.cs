using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Primitives.Models;

namespace Elsa.Activities.Composition.Runtime.Activities;

/// <summary>
/// The single backing CLR activity for every workflow-backed activity. It is itself an ordinary
/// <see cref="IActivity"/>. A future workflow-as-activity compiler lowers its typed
/// <see cref="WorkflowIdentity"/> request directly; there is no dynamic argument bag or descriptor constructor.
/// </summary>
/// <remarks>
/// Runtime-side, no <c>Elsa.*.Design.*</c> dependency (Elsa §E2.2). Unit 006 is <b>construct-only</b>:
/// the execution body (load-and-run the referenced workflow version) is deferred to the
/// consumer/pinning unit.
/// </remarks>
public sealed class WorkflowDefinitionActivity : Activity<ActivityUnit>
{
    /// <summary>The workflow this activity runs — applied as typed state by the constructor.</summary>    
    public WorkflowIdentity? WorkflowIdentity { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        => throw new NotSupportedException(
            "WorkflowDefinitionActivity execution (load-and-run the referenced workflow version) is deferred to the consumer/pinning unit; Unit 006 is construct-only.");
}
