using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Graph.Runtime.Activities;

/// <summary>
/// Ends the nearest enclosing graph activity (reusable activity) with one of its declared boundary outcomes, from any
/// depth in its body. Composites between this node and the boundary, loops and <c>Parallel</c> included, neither see
/// nor intercept the exit; a nested graph activity is its own nearest boundary. The boundary then durably cancels the
/// rest of its body and completes with the chosen outcome, capturing its public outputs through its output mappings.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Outcome"/> is the boundary outcome's <b>reference key</b>, authored as one literal. Publication checks it
/// against the reusable activity's contract; the boundary resolves it to the runtime outcome name from its pinned
/// descriptor. The node completes itself in the same commit that sends the exit, and the exit is evaluated first, so
/// nothing connected after this node ever runs. It is unrelated to <c>Break</c>.
/// </para>
/// <para>
/// See <c>specs/237-graph-boundary-completion/contracts/complete-activity.md</c> for the emitter contract.
/// </para>
/// </remarks>
public sealed class Complete : StructuralActivity, IRuntimeStructuralActivity
{
    /// <summary>The fault code when <see cref="Outcome"/> is blank at run time.</summary>
    public const string OutcomeRequiredFaultCode = "activity.graph.complete.outcome-required";

    /// <summary>The reference key of the enclosing reusable activity's boundary outcome to complete with.</summary>
    [ActivityInput(Key = "outcome")]
    public string? Outcome { get; set; }

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        if (string.IsNullOrWhiteSpace(Outcome))
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault(
                OutcomeRequiredFaultCode,
                $"Complete node '{runtimeContext.ExecutableNode.ExecutableNodeId}' has no boundary outcome.")));
        }

        runtimeContext.RequestAncestorNotification(
            WellKnownRuntimeActivityConsumers.GraphActivity,
            GraphCompleteNotification.Code,
            JsonSerializer.SerializeToElement(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [GraphCompleteNotification.OutcomePayloadKey] = Outcome
            }));
        return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }
}
