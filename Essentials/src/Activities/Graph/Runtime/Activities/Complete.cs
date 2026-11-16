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
[ActivityPalette(ResourceKinds = ["activity-definition-graph"], Description = "Complete ends an enclosing reusable activity and belongs inside its graph body.")]
[Activity(Icon = "CircleCheck")]
public sealed class Complete : StructuralActivity, IRuntimeStructuralActivity
{
    /// <summary>The fault code when <see cref="Outcome"/> is blank at run time.</summary>
    public const string OutcomeRequiredFaultCode = "activity.graph.complete.outcome-required";

    /// <summary>The fault code when both <see cref="Outcome"/> and <see cref="Outcomes"/> are set at run time.</summary>
    public const string OutcomeConflictFaultCode = "activity.graph.complete.outcome-conflict";

    /// <summary>
    /// The reference key of the enclosing reusable activity's boundary outcome to complete with. Leave it empty and set
    /// <see cref="Outcomes"/> to complete with several.
    /// </summary>
    [ActivityInput(Key = "outcome")]
    public string? Outcome { get; set; }

    /// <summary>
    /// The reference keys of several boundary outcomes to complete with at once, each a literal (spec 1208 FR-032). The
    /// boundary passes every one to its parent; how the paths after it join is the default join's concern.
    /// </summary>
    [ActivityInput(Key = "outcomes")]
    public ICollection<string>? Outcomes { get; set; }

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        // Publication refuses a node that sets both inputs (activity.graph.complete.outcome-conflict); a node that
        // reaches run time with both anyway faults rather than silently dropping one of them.
        if (!string.IsNullOrWhiteSpace(Outcome) && Outcomes is { Count: > 0 })
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault(
                OutcomeConflictFaultCode,
                $"Complete node '{runtimeContext.ExecutableNode.ExecutableNodeId}' sets both 'outcome' and 'outcomes'.")));
        }

        if (Outcomes is { Count: > 0 } listed && listed.Any(string.IsNullOrWhiteSpace))
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault(
                OutcomeRequiredFaultCode,
                $"Complete node '{runtimeContext.ExecutableNode.ExecutableNodeId}' lists a blank boundary outcome in 'outcomes'; every entry must name one.")));
        }

        string[] outcomes = Outcomes is { Count: > 0 } several
            ? several.Distinct(StringComparer.Ordinal).ToArray()
            : string.IsNullOrWhiteSpace(Outcome) ? [] : [Outcome];
        if (outcomes.Length == 0)
        {
            return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault(
                OutcomeRequiredFaultCode,
                $"Complete node '{runtimeContext.ExecutableNode.ExecutableNodeId}' has no boundary outcome.")));
        }

        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [GraphCompleteNotification.OutcomePayloadKey] = outcomes[0]
        };
        if (outcomes.Length > 1)
            payload[GraphCompleteNotification.OutcomesPayloadKey] = outcomes;
        runtimeContext.RequestAncestorNotification(
            WellKnownRuntimeActivityConsumers.GraphActivity,
            GraphCompleteNotification.Code,
            JsonSerializer.SerializeToElement(payload));
        return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }
}
