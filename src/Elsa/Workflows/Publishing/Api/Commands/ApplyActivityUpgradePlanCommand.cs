using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Commands;

/// <summary>Validates plan state/selection and delegates one cross-domain atomic mutation.</summary>
public sealed class ApplyActivityUpgradePlanCommand(
    IActivityUpgradePlanStore planStore,
    IActivityUpgradeApplyReceiptStore receiptStore,
    IActivityUpgradePlanMutationStore mutationStore,
    ISystemClock clock) : IActivityUpgradePlanApplier
{
    public async ValueTask<ActivityUpgradeApplyResult> ApplyAsync(
        ActivityUpgradeApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlanId) ||
            string.IsNullOrWhiteSpace(request.StageId) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 200)
            throw Rejected(400, "activity.request.invalid", "Plan, stage, and a bounded idempotency key are required.");

        var plan = await planStore.FindAsync(request.PlanId, cancellationToken)
                   ?? throw Rejected(404, "activity.upgrade.plan-not-found", "The activity upgrade plan was not found.");
        if (plan.Binding is null)
            throw Rejected(409, "activity.upgrade.stale-plan", "The activity upgrade plan does not contain an exact access and scope binding.");

        var keyHash = ActivityUpgradeApplyIdentity.HashIdempotencyKey(request.IdempotencyKey);
        var requestFingerprint = ActivityUpgradeApplyIdentity.CreateRequestFingerprint(request.PlanId, request.StageId);
        var existing = await receiptStore.FindByIdempotencyKeyAsync(plan.PlanId, keyHash, cancellationToken);
        if (existing is not null)
            return ExistingResult(existing, requestFingerprint);

        if (clock.UtcNow >= plan.ExpiresAt)
            throw Rejected(410, "activity.upgrade.plan-expired", "The activity upgrade plan has expired.");
        if (plan.Status is ActivityUpgradePlanStatus.Blocked or ActivityUpgradePlanStatus.Superseded)
            throw Rejected(422, "activity.upgrade.plan-blocked", "The activity upgrade plan contains blocking diagnostics.", plan.Diagnostics);
        var stage = plan.Stages.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.StageId, request.StageId))
                    ?? throw Rejected(422, "activity.upgrade.stage-invalid", "The requested upgrade stage is unknown.");
        if (stage.Status != ActivityUpgradeStageStatus.Ready)
            throw Rejected(422, "activity.upgrade.stage-not-ready", "The requested upgrade stage is not ready to apply.");
        var unappliedPrerequisites = stage.DependsOnStageIds
            .Where(id => plan.Stages.All(x => !StringComparer.Ordinal.Equals(x.StageId, id) || x.Status != ActivityUpgradeStageStatus.Applied))
            .ToArray();
        if (unappliedPrerequisites.Length != 0)
            throw Rejected(422, "activity.upgrade.stage-prerequisite", "The requested upgrade stage has unapplied prerequisites.");

        var selected = stage.StepIds
            .Select(id => plan.Steps.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.StepId, id))
                          ?? throw Rejected(409, "activity.upgrade.stale-plan", "The upgrade stage references an unavailable step."))
            .OrderBy(x => x.Order)
            .ToArray();
        var now = clock.UtcNow;
        var receipt = new ActivityUpgradeApplyReceipt(
            ActivityUpgradeApplyIdentity.CreateReceiptId(plan.PlanId, keyHash),
            plan.PlanId,
            stage.StageId,
            keyHash,
            requestFingerprint,
            plan.TenantId,
            plan.Binding.AccessProfileFingerprint,
            ActivityUpgradeApplyReceiptStatus.Preparing,
            now,
            now,
            1);
        if (!await receiptStore.TryCreateAsync(receipt, cancellationToken))
        {
            existing = await receiptStore.FindByIdempotencyKeyAsync(plan.PlanId, keyHash, cancellationToken)
                       ?? throw Rejected(409, "activity.upgrade.outcome-unknown", "The apply outcome is not yet available; query receipt status before retrying.");
            return ExistingResult(existing, requestFingerprint);
        }

        try
        {
            return await mutationStore.ApplyAsync(plan, selected, receipt, now, cancellationToken);
        }
        catch (ActivityUpgradeApplyException exception)
        {
            await receiptStore.RejectAsync(
                receipt,
                exception.StatusCode,
                exception.ErrorCode,
                exception.Diagnostics,
                clock.UtcNow,
                cancellationToken);
            throw;
        }
    }

    private static ActivityUpgradeApplyResult ExistingResult(
        ActivityUpgradeApplyReceipt receipt,
        string requestFingerprint)
    {
        if (!StringComparer.Ordinal.Equals(receipt.RequestFingerprint, requestFingerprint))
            throw Rejected(409, "activity.upgrade.idempotency-conflict", "The idempotency key is already bound to another apply request.");
        if (receipt.Status == ActivityUpgradeApplyReceiptStatus.Applied && receipt.Result is not null)
            return receipt.Result;
        if (receipt.Status == ActivityUpgradeApplyReceiptStatus.Rejected)
            throw Rejected(
                receipt.RejectionStatusCode ?? 422,
                receipt.RejectionCode ?? "activity.upgrade.apply-rejected",
                "The apply operation was rejected.",
                receipt.Diagnostics);
        var diagnostic = new ActivityDiagnostic(
            "activity.upgrade.outcome-unknown",
            ActivityDiagnosticSeverity.Warning,
            "The apply operation is still preparing or its outcome is ambiguous.",
            new("ActivityUpgradeApplyReceipt", receipt.ReceiptId),
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["receiptId"] = receipt.ReceiptId,
                ["statusHref"] = $"/design/activities/upgrade-plans/{receipt.PlanId}/receipts/{receipt.ReceiptId}"
            });
        throw Rejected(409, diagnostic.Code, diagnostic.Message, [diagnostic]);
    }

    private static ActivityUpgradeApplyException Rejected(
        int status,
        string code,
        string message,
        IReadOnlyList<ActivityDiagnostic>? diagnostics = null) => new(status, code, message, diagnostics);
}
