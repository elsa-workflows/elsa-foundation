using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>The staged upgrade-plan operations the Design endpoints dispatch to.</summary>
public sealed class ActivityUpgradeOperations(
    IActivityUpgradePlanner planner,
    IActivityUpgradePlanRefresher refresher,
    IActivityUpgradePlanApplier applier,
    IActivityUpgradePlanStore plans,
    IActivityUpgradeApplyReceiptStore receipts,
    IActivityDefinitionVersionPublicationStore publications,
    IActivityAuthoringContextAsync context) : IActivityUpgradeOperations
{
    public async Task<ActivityUpgradePlanView> CreatePlanAsync(CreateActivityUpgradePlan command, CancellationToken cancellationToken)
    {
        try
        {
            var plan = await planner.PlanAsync(new(
                command.Replacements,
                command.Roots,
                command.IncludeTransitiveDependents,
                command.CreateDraftsForPublishedDependents,
                context.TenantId,
                ActivityAccessProfileFingerprint.Create(await context.GetAuthorizationProfileAsync(cancellationToken))), cancellationToken);
            return await plan.ToViewAsync(publications, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new ActivityAuthoringException(
                400,
                ActivityErrorCodes.RequestInvalid,
                "Invalid activity upgrade plan request",
                exception.Message,
                innerException: exception);
        }
    }

    public async Task<ActivityUpgradePlanView> GetPlanAsync(GetActivityUpgradePlan request, CancellationToken cancellationToken)
    {
        var plan = await plans.FindAsync(request.PlanId, cancellationToken)
                   ?? throw new ActivityAuthoringException(404, "activity.upgrade.plan-not-found", "Upgrade plan not found", "The activity upgrade plan was not found.");
        EnsureTenant(plan);
        await EnsureAccessProfileAsync(plan, context, cancellationToken);
        return await plan.ToViewAsync(publications, cancellationToken);
    }

    public async Task<ActivityUpgradeApplyResultView> ApplyPlanAsync(ApplyActivityUpgradePlan command, CancellationToken cancellationToken)
    {
        try
        {
            var plan = await plans.FindAsync(command.PlanId, cancellationToken);
            if (plan is null ||
                !StringComparer.Ordinal.Equals(plan.TenantId, context.TenantId) ||
                plan.Binding is null ||
                !StringComparer.Ordinal.Equals(
                    plan.Binding.AccessProfileFingerprint,
                    ActivityAccessProfileFingerprint.Create(await context.GetAuthorizationProfileAsync(cancellationToken))))
                throw new ActivityAuthoringException(404, "activity.upgrade.plan-not-found", "Upgrade plan not found", "The activity upgrade plan was not found.");
            return (await applier.ApplyAsync(new(command.PlanId, command.StageId, command.IdempotencyKey), cancellationToken)).ToView();
        }
        catch (ActivityUpgradeApplyException exception)
        {
            throw new ActivityAuthoringException(
                exception.StatusCode,
                exception.ErrorCode,
                "Activity upgrade plan rejected",
                exception.Message,
                exception.Diagnostics,
                exception);
        }
    }

    public async Task<ActivityUpgradeApplyReceiptView> GetApplyReceiptAsync(GetActivityUpgradeApplyReceipt request, CancellationToken cancellationToken)
    {
        var plan = await plans.FindAsync(request.PlanId, cancellationToken);
        var fingerprint = ActivityAccessProfileFingerprint.Create(await context.GetAuthorizationProfileAsync(cancellationToken));
        if (plan is null ||
            !StringComparer.Ordinal.Equals(plan.TenantId, context.TenantId) ||
            plan.Binding is null ||
            !StringComparer.Ordinal.Equals(plan.Binding.AccessProfileFingerprint, fingerprint))
            throw ReceiptNotFound();
        var receipt = await receipts.FindAsync(request.ReceiptId, cancellationToken);
        if (receipt is null ||
            !StringComparer.Ordinal.Equals(receipt.PlanId, request.PlanId) ||
            !StringComparer.Ordinal.Equals(receipt.TenantId, context.TenantId) ||
            !StringComparer.Ordinal.Equals(receipt.AccessProfileFingerprint, fingerprint))
            throw ReceiptNotFound();
        return receipt.ToView();
    }

    public async Task<ActivityUpgradePlanView> RefreshPlanAsync(RefreshActivityUpgradePlan command, CancellationToken cancellationToken)
    {
        try
        {
            var plan = await refresher.RefreshAsync(new(
                command.PlanId,
                command.Publications,
                context.TenantId,
                ActivityAccessProfileFingerprint.Create(await context.GetAuthorizationProfileAsync(cancellationToken))), cancellationToken);
            return await plan.ToViewAsync(publications, cancellationToken);
        }
        catch (ActivityUpgradeApplyException exception)
        {
            throw new ActivityAuthoringException(
                exception.StatusCode,
                exception.ErrorCode,
                "Activity upgrade plan refresh rejected",
                exception.Message,
                exception.Diagnostics,
                exception);
        }
    }

    private void EnsureTenant(ActivityUpgradePlan plan)
    {
        if (!StringComparer.Ordinal.Equals(plan.TenantId, context.TenantId))
            throw new ActivityAuthoringException(404, "activity.upgrade.plan-not-found", "Upgrade plan not found", "The activity upgrade plan was not found.");
    }

    private static async ValueTask EnsureAccessProfileAsync(
        ActivityUpgradePlan plan,
        IActivityAuthoringContextAsync context,
        CancellationToken cancellationToken)
    {
        if (plan.Binding is null ||
            !StringComparer.Ordinal.Equals(
                plan.Binding.AccessProfileFingerprint,
                ActivityAccessProfileFingerprint.Create(await context.GetAuthorizationProfileAsync(cancellationToken))))
            throw new ActivityAuthoringException(404, "activity.upgrade.plan-not-found", "Upgrade plan not found", "The activity upgrade plan was not found.");
    }

    private static ActivityAuthoringException ReceiptNotFound() => new(
        404,
        "activity.upgrade.receipt-not-found",
        "Upgrade apply receipt not found",
        "The activity upgrade apply receipt was not found.");
}

/// <summary>The upgrade-plan seam, one method per route.</summary>
public interface IActivityUpgradeOperations
{
    Task<ActivityUpgradePlanView> CreatePlanAsync(CreateActivityUpgradePlan command, CancellationToken cancellationToken);
    Task<ActivityUpgradePlanView> GetPlanAsync(GetActivityUpgradePlan request, CancellationToken cancellationToken);
    Task<ActivityUpgradeApplyResultView> ApplyPlanAsync(ApplyActivityUpgradePlan command, CancellationToken cancellationToken);
    Task<ActivityUpgradeApplyReceiptView> GetApplyReceiptAsync(GetActivityUpgradeApplyReceipt request, CancellationToken cancellationToken);
    Task<ActivityUpgradePlanView> RefreshPlanAsync(RefreshActivityUpgradePlan command, CancellationToken cancellationToken);
}
