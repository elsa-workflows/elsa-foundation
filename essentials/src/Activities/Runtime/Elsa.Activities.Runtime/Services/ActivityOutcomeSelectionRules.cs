using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// The run-time check of an outcome selection against the node's pinned outcome set (ADR 0045 §6; spec 1208 FR-002,
/// FR-002a, FR-016). Any activity may select several of its declared outcomes; how the paths taken join is the
/// default join's concern (initiative #4193), not the activity's. Every completion path — the value-flow projector and
/// the background receipt — checks through here, so a selection follows one set of rules wherever it comes from.
/// There is no node-level fallback: an unknown name faults (FR-018).
/// </summary>
public static class ActivityOutcomeSelectionRules
{
    /// <summary>The fault code of an invalid outcome selection, carried on the durable fault record.</summary>
    public const string FaultCode = "VF-ACT-006";

    /// <summary>Throws <see cref="ActivityOutcomeSelectionException"/> when <paramref name="selection"/> is not valid for the node.</summary>
    public static void Validate(ActivityContract contract, OutcomeSelection selection)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(selection);

        switch (selection.State)
        {
            case OutcomeSelectionState.Unspecified:
                if (!contract.Outcomes.Contains(OutcomeSelection.DefaultOutcome, StringComparer.Ordinal))
                    throw new ActivityOutcomeSelectionException(
                        $"Activity '{contract.ActivityTypeKey}' completed without selecting an outcome, and its outcome set " +
                        $"[{Describe(contract.Outcomes)}] declares no '{OutcomeSelection.DefaultOutcome}'. This is a defect in the " +
                        "activity's code: it must select one of its outcomes on every successful completion, or declare " +
                        $"'{OutcomeSelection.DefaultOutcome}'.");
                return;

            case OutcomeSelectionState.Empty:
                return;
        }

        foreach (var name in selection.Names)
        {
            if (!contract.Outcomes.Contains(name, StringComparer.Ordinal))
                throw new ActivityOutcomeSelectionException(
                    $"Outcome '{name}' is not in the outcome set [{Describe(contract.Outcomes)}] of activity '{contract.ActivityTypeKey}' " +
                    "pinned at publication.");
        }

        if (selection.Names.Count <= 1)
            return;

        if (selection.Names.Contains(OutcomeSelection.DefaultOutcome, StringComparer.Ordinal))
            throw new ActivityOutcomeSelectionException(
                $"Activity '{contract.ActivityTypeKey}' selected '{OutcomeSelection.DefaultOutcome}' together with another outcome " +
                $"[{Describe(selection.Names)}]; '{OutcomeSelection.DefaultOutcome}' means no branch was chosen and is never selected " +
                "with a branch.");
    }

    private static string Describe(IEnumerable<string> names) => string.Join(", ", names.Select(name => $"'{name}'"));
}

/// <summary>An invalid outcome selection; faults the step with <see cref="ActivityOutcomeSelectionRules.FaultCode"/> as its code.</summary>
public sealed class ActivityOutcomeSelectionException(string message) : InvalidOperationException(message), IRuntimeFaultClassification
{
    public bool IsRetryable => false;
    public string FailureCode => ActivityOutcomeSelectionRules.FaultCode;
}
