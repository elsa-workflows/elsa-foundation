using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Primitives.Models;

namespace Elsa.Activities.Composition.Runtime.Activities;

/// <summary>
/// The single backing CLR activity for every workflow-backed activity. It is itself an ordinary
/// <see cref="IActivity"/> (an <see cref="ActivityBase"/>) — catalogued under a
/// <c>ClrActivityDescriptor</c> like any primitive. For a workflow-as-activity catalog row, the
/// <see cref="Constructors.WorkflowActivityConstructor"/> produces one of these configured with the
/// row's <see cref="WorkflowIdentity"/> and author inputs/outputs.
/// </summary>
/// <remarks>
/// Runtime-side, no <c>Elsa.*.Design.*</c> dependency (Elsa §E2.2). Unit 006 is <b>construct-only</b>:
/// the execution body (load-and-run the referenced workflow version) is deferred to the
/// consumer/pinning unit.
/// </remarks>
public sealed class WorkflowDefinitionActivity : ActivityBase
{
    /// <summary>The workflow this activity runs — applied as typed state by the constructor.</summary>    
    public WorkflowIdentity? WorkflowIdentity { get; set; }

    protected override void Execute(IActivityExecutionContext context)
        => throw new NotSupportedException(
            "WorkflowDefinitionActivity execution (load-and-run the referenced workflow version) is deferred to the consumer/pinning unit; Unit 006 is construct-only.");
}
