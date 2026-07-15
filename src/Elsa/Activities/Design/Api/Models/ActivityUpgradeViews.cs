using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityUpgradePlanView(
    string PlanId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    IReadOnlyList<ActivityVersionReplacement> Replacements,
    IReadOnlyList<ActivityUpgradeExpectedSnapshot> ExpectedSnapshots,
    IReadOnlyList<ActivityUpgradeStep> Steps,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record ActivityUpgradeApplyResultView(
    string PlanId,
    string Status,
    DateTimeOffset AppliedAt,
    IReadOnlyList<ActivityUpgradeAppliedDraft> Drafts,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public static class ActivityUpgradeViewMappings
{
    public static ActivityUpgradePlanView ToView(this ActivityUpgradePlan plan) => new(
        plan.PlanId,
        plan.CreatedAt,
        plan.ExpiresAt,
        plan.Status.ToString(),
        plan.Replacements,
        plan.ExpectedSnapshots,
        plan.Steps,
        plan.Diagnostics);

    public static ActivityUpgradeApplyResultView ToView(this ActivityUpgradeApplyResult result) => new(
        result.PlanId,
        result.Status.ToString(),
        result.AppliedAt,
        result.Drafts,
        result.Diagnostics);
}
