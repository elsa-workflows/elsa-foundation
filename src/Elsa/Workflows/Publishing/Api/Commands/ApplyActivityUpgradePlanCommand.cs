using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Commands;

/// <summary>Validates plan state/selection and delegates one cross-domain atomic mutation.</summary>
public sealed class ApplyActivityUpgradePlanCommand(
    IActivityUpgradePlanStore planStore,
    IActivityUpgradePlanMutationStore mutationStore,
    ISystemClock clock) : IActivityUpgradePlanApplier
{
    public async ValueTask<ActivityUpgradeApplyResult> ApplyAsync(
        ActivityUpgradeApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var plan = await planStore.FindAsync(request.PlanId, cancellationToken)
                   ?? throw Rejected(404, "activity.upgrade.plan-not-found", "The activity upgrade plan was not found.");
        if (plan.Status == ActivityUpgradePlanStatus.Applied)
            return new(plan.PlanId, plan.Status, plan.AppliedAt ?? plan.CreatedAt, plan.AppliedDrafts ?? [], plan.Diagnostics);
        if (clock.UtcNow >= plan.ExpiresAt)
            throw Rejected(410, "activity.upgrade.plan-expired", "The activity upgrade plan has expired.");
        if (plan.Status == ActivityUpgradePlanStatus.Blocked)
            throw Rejected(422, "activity.upgrade.plan-blocked", "The activity upgrade plan contains blocking diagnostics.", plan.Diagnostics);

        var ready = plan.Steps.Where(x => x.Diagnostics.All(d => d.Severity != ActivityDiagnosticSeverity.Error)).ToArray();
        var selectedIds = request.SelectedStepIds is null or { Count: 0 }
            ? ready.Select(x => x.StepId).ToHashSet(StringComparer.Ordinal)
            : request.SelectedStepIds.ToHashSet(StringComparer.Ordinal);
        if (selectedIds.Count != (request.SelectedStepIds?.Count ?? selectedIds.Count) || selectedIds.Any(id => ready.All(x => !StringComparer.Ordinal.Equals(x.StepId, id))))
            throw Rejected(422, "activity.upgrade.selection-invalid", "The selected step set contains a duplicate, unknown, or blocked step.");

        var selected = ready.Where(x => selectedIds.Contains(x.StepId)).OrderBy(x => x.Order).ToArray();
        var missing = selected.SelectMany(x => x.DependsOnStepIds).Where(x => !selectedIds.Contains(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
        {
            var diagnostic = new ActivityDiagnostic(
                "activity.upgrade.selection-not-closed",
                ActivityDiagnosticSeverity.Error,
                "The selected upgrade steps are not dependency-closed.",
                new ActivityDiagnosticSubject("ActivityUpgradePlan", plan.PlanId),
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["missingStepIds"] = string.Join(',', missing) });
            throw Rejected(422, "activity.upgrade.selection-not-closed", diagnostic.Message, [diagnostic]);
        }

        return await mutationStore.ApplyAsync(plan, selected, clock.UtcNow, cancellationToken);
    }

    private static ActivityUpgradeApplyException Rejected(
        int status,
        string code,
        string message,
        IReadOnlyList<ActivityDiagnostic>? diagnostics = null) => new(status, code, message, diagnostics);
}
