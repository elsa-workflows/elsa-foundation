using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Activities.Bpmn.Activities;

/// <summary>
/// The expression-condition evaluator for BPMN gateways. A transient CLR leaf whose
/// <see cref="Outcome"/> input is evaluated by the runtime's value binding (literal or any bound
/// expression language), completing with the evaluated string as its outcome name. Bind one
/// <c>BpmnDecision</c> to an exclusive or inclusive gateway and author conditional sequence flows whose
/// <c>conditionOutcome</c> matches the values the expression can produce — e.g. a JavaScript expression
/// <c>amount &gt; 1000 ? "High" : "Low"</c> with flows conditioned on <c>High</c> and <c>Low</c>.
/// </summary>
/// <remarks>
/// A blank or unbound <see cref="Outcome"/> resolves to <see cref="ActivityOutcomes.Done"/>, which
/// matches no conditional flow — gateway routing then falls through to the default flow (or faults
/// deterministically when none is declared), so a misconfigured decision never routes silently.
/// </remarks>
public sealed class BpmnDecision : Activity<BpmnDecisionResult>
{
    /// <summary>The outcome name to complete with; conditional sequence flows match against it.</summary>
    [ActivityInput(Key = nameof(Outcome))]
    public string? Outcome { get; set; }

    protected override ValueTask<ActivityTransition<BpmnDecisionResult>> ExecuteAsync(ActivityExecutionContext context)
    {
        var outcome = string.IsNullOrWhiteSpace(Outcome) ? ActivityOutcomes.Done : Outcome.Trim();
        return ValueTask.FromResult(ActivityTransition.Complete(new BpmnDecisionResult(outcome), outcome));
    }
}

/// <summary>The atomic result recording which outcome the decision produced.</summary>
public sealed record BpmnDecisionResult(string Outcome);
