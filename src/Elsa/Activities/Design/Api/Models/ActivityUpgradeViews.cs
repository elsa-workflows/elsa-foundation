using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityUpgradeVersionIdentityView(
    string DefinitionId,
    string VersionId,
    string Version,
    string TemplateHash);

public sealed record ActivityUpgradeReplacementView(
    ActivityUpgradeVersionIdentityView From,
    ActivityUpgradeVersionIdentityView To);

public sealed record ActivityUpgradePlanView(
    string PlanId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    IReadOnlyList<ActivityUpgradeReplacementView> Replacements,
    IReadOnlyList<ActivityUpgradeExpectedSnapshot> ExpectedSnapshots,
    IReadOnlyList<ActivityUpgradeStep> Steps,
    IReadOnlyList<ActivityUpgradeStage> Stages,
    ActivityUpgradePlanBinding? Binding,
    IReadOnlyList<ActivityDiagnostic> Diagnostics,
    string? PredecessorPlanId,
    string? SuccessorPlanId);

public sealed record ActivityUpgradeApplyResultView(
    string PlanId,
    string Status,
    DateTimeOffset AppliedAt,
    IReadOnlyList<ActivityUpgradeAppliedDraft> Drafts,
    IReadOnlyList<ActivityDiagnostic> Diagnostics,
    string? ReceiptId,
    string? StageId,
    IReadOnlyList<ActivityUpgradePublicationHandoff> AwaitingPublications);

public sealed record ActivityUpgradeApplyReceiptView(
    string ReceiptId,
    string PlanId,
    string StageId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ActivityUpgradeApplyResultView? Result,
    IReadOnlyList<ActivityDiagnostic> Diagnostics,
    int? RejectionStatusCode,
    string? RejectionCode);

public static class ActivityUpgradeViewMappings
{
    public static async Task<ActivityUpgradePlanView> ToViewAsync(
        this ActivityUpgradePlan plan,
        IActivityDefinitionVersionPublicationStore publications,
        CancellationToken cancellationToken = default)
    {
        var replacements = new List<ActivityUpgradeReplacementView>(plan.Replacements.Count);
        foreach (var replacement in plan.Replacements)
        {
            var from = await RequiredAsync(replacement.FromVersionId, publications, cancellationToken);
            var to = await RequiredAsync(replacement.ToVersionId, publications, cancellationToken);
            replacements.Add(new(ToIdentity(from), ToIdentity(to)));
        }

        return new(
            plan.PlanId,
            plan.CreatedAt,
            plan.ExpiresAt,
            plan.Status.ToString(),
            replacements,
            plan.ExpectedSnapshots,
            plan.Steps.Select(x => x with { Diagnostics = ActivityDiagnosticOrderer.Order(x.Diagnostics) }).ToArray(),
            plan.Stages,
            plan.Binding,
            ActivityDiagnosticOrderer.Order(plan.Diagnostics),
            plan.PredecessorPlanId,
            plan.SuccessorPlanId);
    }

    public static ActivityUpgradeApplyResultView ToView(this ActivityUpgradeApplyResult result) => new(
        result.PlanId,
        result.Status.ToString(),
        result.AppliedAt,
        result.Drafts,
        ActivityDiagnosticOrderer.Order(result.Diagnostics),
        result.ReceiptId,
        result.StageId,
        result.AwaitingPublications);

    public static ActivityUpgradeApplyReceiptView ToView(this ActivityUpgradeApplyReceipt receipt) => new(
        receipt.ReceiptId,
        receipt.PlanId,
        receipt.StageId,
        receipt.Status.ToString(),
        receipt.CreatedAt,
        receipt.UpdatedAt,
        receipt.Result?.ToView(),
        ActivityDiagnosticOrderer.Order(receipt.Diagnostics),
        receipt.RejectionStatusCode,
        receipt.RejectionCode);

    private static async Task<ActivityDefinitionVersionPublication> RequiredAsync(
        string versionId,
        IActivityDefinitionVersionPublicationStore publications,
        CancellationToken cancellationToken) =>
        await publications.FindAsync(versionId, cancellationToken)
        ?? throw new InvalidOperationException($"Activity upgrade plan replacement version '{versionId}' is unavailable.");

    private static ActivityUpgradeVersionIdentityView ToIdentity(ActivityDefinitionVersionPublication version) => new(
        version.DefinitionId,
        version.DefinitionVersionId,
        version.Version,
        version.TemplateHash);
}
