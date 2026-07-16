using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;

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
                context.TenantId), cancellationToken);
            return await plan.ToViewAsync(publications, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new ActivityAuthoringException(
                400,
                "activity.request.invalid",
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
        return await plan.ToViewAsync(publications, cancellationToken);
    }

    private void EnsureTenant(ActivityUpgradePlan plan)
    {
        if (!StringComparer.Ordinal.Equals(plan.TenantId, context.TenantId))
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
            if (plan is null || !StringComparer.Ordinal.Equals(plan.TenantId, context.TenantId))
                throw new ActivityAuthoringException(404, "activity.upgrade.plan-not-found", "Upgrade plan not found", "The activity upgrade plan was not found.");
            return (await applier.ApplyAsync(new(command.PlanId, command.SelectedStepIds), cancellationToken)).ToView();
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
