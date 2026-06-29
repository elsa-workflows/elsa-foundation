using Elsa.Activities.Primitives.Exceptions;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Faults the current activity execution by raising a <see cref="FaultActivityException"/>. The runtime
/// engine (<c>WorkflowInvokeActivitySchedulerWorkHandler</c>) catches the exception and records it as a
/// blocking <c>IncidentState</c> through the engine incident model — it is NOT propagated to the host.
/// This is the sanctioned way for a leaf activity to surface a deliberate fault: the workflow run does
/// not throw out to its caller; instead an incident is persisted for inspection/intervention.
/// </summary>
/// <remarks>
/// Ported from elsa-core's <c>Fault</c> activity and adapted to this repo's model: the message is an
/// <see cref="InputArgument{T}"/> and the activity derives from <see cref="CodeActivity"/>. Resolved by
/// the existing <c>ClrActivityConstructor</c> like <see cref="WriteLine"/>.
/// </remarks>
public sealed class Fault : CodeActivity
{
    /// <summary>The fault message recorded on the incident. Defaults to a generic message when unset.</summary>
    public InputArgument<string>? Message { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var message = context.Get(Message);
        throw new FaultActivityException(string.IsNullOrWhiteSpace(message)
            ? "The workflow faulted."
            : message);
    }
}
