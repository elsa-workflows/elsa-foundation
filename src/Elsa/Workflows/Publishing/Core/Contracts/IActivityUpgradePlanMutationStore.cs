using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>
/// Atomic Publishing bridge for applying one dependency-closed set of activity/workflow draft edits
/// together with the plan's Applied transition.
/// </summary>
public interface IActivityUpgradePlanMutationStore
{
    ValueTask<ActivityUpgradeApplyResult> ApplyAsync(
        ActivityUpgradePlan plan,
        IReadOnlyList<ActivityUpgradeStep> selectedSteps,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken = default);
}
