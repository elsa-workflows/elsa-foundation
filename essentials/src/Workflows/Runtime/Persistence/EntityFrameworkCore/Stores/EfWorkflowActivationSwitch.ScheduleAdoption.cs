using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Services.Executables.WorkflowActivationSwitchRules;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowActivationSwitch
{
    async ValueTask<bool> IWorkflowScheduleAdoption.TryAdoptSchedulesAsync(WorkflowActivationSlot slot,
        WorkflowExecutableSourceReference source, IReadOnlyCollection<WorkflowTriggerBinding> claims,
        IReadOnlyCollection<string> alsoServing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(claims);
        var plan = RepairPlan(slot, alsoServing);
        if (claims.Count == 0 || claims.Any(binding => binding.StimulusType != RuntimeKernelScheduleOccurrence.ReservationType))
            throw new ArgumentException("Schedule adoption requires the selected publication's exact schedule claims.", nameof(claims));
        var desired = claims.ToArray();

        return await RunAsync($"Schedule adoption for activation '{plan.ActivationId}'", async scope =>
        {
            if (!await EfAdmissionTargetFence.LockSlotAsync(_context, scope, slot.WorkflowDefinitionId, slot.SlotName, slot, cancellationToken) ||
                !await EfAdmissionTargetFence.LockSourceAsync(_references, scope, source, cancellationToken))
                return Step.Discard(false);

            var states = await StageStatesAsync(scope, plan.ActivationId, cancellationToken);
            var repair = IsRepairable(states);
            var effectiveSource = repair ? plan.ResumeOwn(source) ?? source : source;
            if (source.SourceReferenceId != plan.ReferenceId || source.ActivationId != plan.ActivationId || source.SlotId != slot.SlotId ||
                !effectiveSource.IsLive(_timeProvider.GetUtcNow()))
                throw new InvalidDataException("Schedule adoption requires the active slot's exact live publication source.");
            if (!repair && states.Any(state => state != WorkflowActivationProjectionState.Active))
                throw new InvalidDataException("Schedule adoption requires an active or repairable publication projection.");

            var persistence = new PersistenceScope(scope);
            _accessContextAccessor.Current.EnsureTenantScope(source.TenantId);
            if (source.TenantId is null && scope != PersistenceScope.DefaultValue)
                throw new InvalidDataException("Schedule adoption requires an exact tenant on publications outside the default scope.");
            var controls = EfRuntimeKernelScheduleControlStore.Describe(new(persistence, source.TenantId), slot, source, desired);
            var executable = await new EfWorkflowExecutableStore(_context, _accessContextAccessor).FindAsync(source.ArtifactId, cancellationToken);
            if (executable is null || controls.Any(control => control.Owner!.Executable != executable.Identity ||
                    !executable.NodesById.ContainsKey(control.TriggerNode)))
                throw new InvalidDataException("The schedule claims differ from the selected immutable executable.");

            // An old workflow pump can already hold a cursor when this transaction begins. It needs an explicit
            // handoff before the Kernel scheduler may replace it. Prepared workflow cursors would become active on repair.
            var scopeHash = Hash(scope);
            var activeId = Encode(plan.ActivationId);
            if (await _context.RecurringTriggerSchedules.AsNoTracking().AnyAsync(row => row.ScopeKeyHash == scopeHash &&
                    (row.ActivationId == activeId || (row.IsActive || row.ClaimOwnerId != null) &&
                        (row.ArtifactId != null || row.ActivationId != null)), cancellationToken))
                throw new InvalidOperationException("The Runtime store has a legacy recurring workflow cursor. Keep the legacy scheduler for this store or use a fresh Runtime database before enabling Kernel Timer and Cron.");

            if (repair && await StageRepairAsync(scope, plan, cancellationToken)) return Step.Moved<bool>();
            await _bindings.StageScheduleAdoptionAsync(scope, plan.ActivationId, desired, cancellationToken);
            await StageReservationsAsync(slot, cancellationToken, desired, repair);
            if (!effectiveSource.IsLive(_timeProvider.GetUtcNow())) return Step.Discard(false);
            return Step.Commit(true);
        }, cancellationToken, retryStageConflicts: true);
    }
}
