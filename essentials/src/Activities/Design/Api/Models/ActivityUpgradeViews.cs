using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;

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
