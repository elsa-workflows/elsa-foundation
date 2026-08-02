using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class CreateActivityUpgradePlanHandler(
    IActivityUpgradePlanner planner,
    IActivityDefinitionVersionPublicationStore publications,
    IActivityAuthoringContext context)
    : ICommandHandler<CreateActivityUpgradePlan, ActivityUpgradePlanView>
{
    public async Task<ActivityUpgradePlanView> Handle(CreateActivityUpgradePlan command, CancellationToken cancellationToken)
    {
        try
        {
            var plan = await planner.PlanAsync(new(
                command.Replacements,
                command.Roots,
                command.IncludeTransitiveDependents,
                command.CreateDraftsForPublishedDependents,
                context.TenantId,
                ActivityAccessProfileFingerprint.Create(context.AuthorizationProfile)), cancellationToken);
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
}

public sealed class GetActivityUpgradePlanHandler(
    IActivityUpgradePlanStore store,
    IActivityDefinitionVersionPublicationStore publications,
    IActivityAuthoringContext context)
    : IRequestHandler<GetActivityUpgradePlan, ActivityUpgradePlanView>
{
    public async Task<ActivityUpgradePlanView> Handle(GetActivityUpgradePlan request, CancellationToken cancellationToken)
    {
        var plan = await store.FindAsync(request.PlanId, cancellationToken)
                   ?? throw new ActivityAuthoringException(404, "activity.upgrade.plan-not-found", "Upgrade plan not found", "The activity upgrade plan was not found.");
        EnsureTenant(plan);
        EnsureAccessProfile(plan, context);
        return await plan.ToViewAsync(publications, cancellationToken);
    }

    private void EnsureTenant(ActivityUpgradePlan plan)
    {
        if (!StringComparer.Ordinal.Equals(plan.TenantId, context.TenantId))
            throw new ActivityAuthoringException(404, "activity.upgrade.plan-not-found", "Upgrade plan not found", "The activity upgrade plan was not found.");
    }

    private static void EnsureAccessProfile(ActivityUpgradePlan plan, IActivityAuthoringContext context)
    {
        if (plan.Binding is null ||
            !StringComparer.Ordinal.Equals(
                plan.Binding.AccessProfileFingerprint,
                ActivityAccessProfileFingerprint.Create(context.AuthorizationProfile)))
            throw new ActivityAuthoringException(404, "activity.upgrade.plan-not-found", "Upgrade plan not found", "The activity upgrade plan was not found.");
    }
}

public sealed class ApplyActivityUpgradePlanHandler(
    IActivityUpgradePlanApplier applier,
    IActivityUpgradePlanStore store,
    IActivityAuthoringContext context)
    : ICommandHandler<ApplyActivityUpgradePlan, ActivityUpgradeApplyResultView>
{
    public async Task<ActivityUpgradeApplyResultView> Handle(ApplyActivityUpgradePlan command, CancellationToken cancellationToken)
    {
        try
        {
            var plan = await store.FindAsync(command.PlanId, cancellationToken);
            if (plan is null ||
                !StringComparer.Ordinal.Equals(plan.TenantId, context.TenantId) ||
                plan.Binding is null ||
                !StringComparer.Ordinal.Equals(
                    plan.Binding.AccessProfileFingerprint,
                    ActivityAccessProfileFingerprint.Create(context.AuthorizationProfile)))
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
}

public sealed class GetActivityUpgradeApplyReceiptHandler(
    IActivityUpgradeApplyReceiptStore receipts,
    IActivityUpgradePlanStore plans,
    IActivityAuthoringContext context)
    : IRequestHandler<GetActivityUpgradeApplyReceipt, ActivityUpgradeApplyReceiptView>
{
    public async Task<ActivityUpgradeApplyReceiptView> Handle(
        GetActivityUpgradeApplyReceipt request,
        CancellationToken cancellationToken)
    {
        var plan = await plans.FindAsync(request.PlanId, cancellationToken);
        var fingerprint = ActivityAccessProfileFingerprint.Create(context.AuthorizationProfile);
        if (plan is null ||
            !StringComparer.Ordinal.Equals(plan.TenantId, context.TenantId) ||
            plan.Binding is null ||
            !StringComparer.Ordinal.Equals(plan.Binding.AccessProfileFingerprint, fingerprint))
            throw NotFound();
        var receipt = await receipts.FindAsync(request.ReceiptId, cancellationToken);
        if (receipt is null ||
            !StringComparer.Ordinal.Equals(receipt.PlanId, request.PlanId) ||
            !StringComparer.Ordinal.Equals(receipt.TenantId, context.TenantId) ||
            !StringComparer.Ordinal.Equals(receipt.AccessProfileFingerprint, fingerprint))
            throw NotFound();
        return receipt.ToView();
    }

    private static ActivityAuthoringException NotFound() => new(
        404,
        "activity.upgrade.receipt-not-found",
        "Upgrade apply receipt not found",
        "The activity upgrade apply receipt was not found.");
}

public sealed class RefreshActivityUpgradePlanHandler(
    IActivityUpgradePlanRefresher refresher,
    IActivityDefinitionVersionPublicationStore publications,
    IActivityAuthoringContext context)
    : ICommandHandler<RefreshActivityUpgradePlan, ActivityUpgradePlanView>
{
    public async Task<ActivityUpgradePlanView> Handle(
        RefreshActivityUpgradePlan command,
        CancellationToken cancellationToken)
    {
        try
        {
            var plan = await refresher.RefreshAsync(new(
                command.PlanId,
                command.Publications,
                context.TenantId,
                ActivityAccessProfileFingerprint.Create(context.AuthorizationProfile)), cancellationToken);
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
}
