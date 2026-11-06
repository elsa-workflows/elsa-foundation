using Elsa.Activities.Design.Core.Contracts;
using Elsa.Decisions.Workflows.Runtime.Activities;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Decisions.Workflows.Design;

/// <summary>Exposes the same publication-fixed selector restriction enforced by DecisionPinSource.</summary>
internal sealed class DecisionInputSourceConstraintProvider : IActivityInputSourceConstraintProvider
{
    public ValueTask<ActivityDataInputConstraint?> GetConstraintAsync(
        IActivityDefinitionVersion activityVersion,
        string inputKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(activityVersion);
        var applies = StringComparer.Ordinal.Equals(activityVersion.Definition.ActivityTypeKey, EvaluateDecision.ActivityType)
            && StringComparer.Ordinal.Equals(inputKey, nameof(EvaluateDecision.Decision));
        return ValueTask.FromResult<ActivityDataInputConstraint?>(applies
            ? new("Elsa.Decisions", ["Literal"], "decisions.selector-fixed-at-publication",
                "Select the decision to evaluate before publishing.")
            : null);
    }
}
