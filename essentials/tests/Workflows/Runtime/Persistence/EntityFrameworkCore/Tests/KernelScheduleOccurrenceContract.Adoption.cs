using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal static partial class KernelScheduleOccurrenceContract
{
    private static async Task CheckScheduleAdoptionAsync(Func<RuntimeDbContext> open)
    {
        await using var context = open();
        var access = new Accessor(new(PersistenceScope.DefaultValue));
        var clock = new FakeTimeProvider(Now);
        var artifacts = new EfWorkflowExecutableStore(context, access);
        var sources = new EfWorkflowExecutableSourceReferenceStore(context, access, new Codec(), clock);
        var bindings = new EfWorkflowTriggerBindingStore(context, access);
        var authority = new EfWorkflowActivationAuthority(context, access);
        var schedules = new EfRecurringTriggerScheduleStore(context, access, new Codec());
        var activationSwitch = new EfWorkflowActivationSwitch(authority, bindings, sources, access, clock, schedules);
        var adoption = (IWorkflowScheduleAdoption)activationSwitch;
        var explicitDefault = new TriggerScope(access.Current.RequireScope(), PersistenceScope.DefaultValue);
        var implicitDefault = explicitDefault with { TenantId = null };

        async Task<(WorkflowActivationSlot Slot, WorkflowExecutableSourceReference Source, WorkflowTriggerBinding Claim,
            WorkflowTriggerBinding Other, RecurringTriggerSchedule? Legacy)> PublishAsync(string suffix, TriggerScope scope,
            bool interrupted = false, bool hasLegacy = false)
        {
            const string node = "clock";
            const string slotName = "Blue";
            var definition = "adopt-definition-" + suffix;
            var activation = "adopt-activation-" + suffix;
            var identity = new WorkflowExecutableIdentity("adopt-artifact-" + suffix, definition, "v1", "1", "adopt-hash-" + suffix);
            var slotId = WorkflowActivationSlotIdentity.Create(definition, slotName);
            var source = new WorkflowExecutableSourceReference(WorkflowActivationReferenceIdentity.Create(activation), identity.ArtifactId,
                "definition", definition, "1", definition, identity.DefinitionVersionId, identity.ArtifactVersion, Now, Now,
                WorkflowExecutableReferenceScope.Published, ActivationId: activation, SlotId: slotId, TenantId: scope.TenantId);
            var key = $"schedule:{definition}:{node}";
            var hash = RuntimeKernelScheduleOccurrence.ReservationKey(key);
            WorkflowTriggerBinding Binding(string type, string bindingHash, IReadOnlyDictionary<string, string> metadata) =>
                new(WorkflowTriggerBinding.BuildId(activation, identity.ArtifactId, node, bindingHash), identity.ArtifactId,
                    definition, identity.ArtifactVersion, identity.ArtifactHash, node, type, bindingHash, null, metadata,
                    Now, activation, slotId, TriggerCardinality.ReservationOnly, false);
            var claim = Binding(RuntimeKernelScheduleOccurrence.ReservationType, hash,
                new Dictionary<string, string> { [RuntimeKernelScheduleControl.ScheduleKeyMetadata] = key });
            var other = Binding("unrelated-reservation", "independent:" + suffix, new Dictionary<string, string>());
            var legacy = hasLegacy ? new RecurringTriggerSchedule(RecurringTriggerSchedule.BuildId(activation, identity.ArtifactId, node),
                identity.ArtifactId, node, "Timer", "legacy:" + suffix, RecurringScheduleKind.Interval, "PT1M", Now,
                Now.AddMinutes(1), activation, slotId, false) : null;
            await artifacts.SaveAsync(Executable(identity, node));
            await sources.SaveAsync(source);
            await bindings.PrepareActivationAsync(activation, [other]);
            await schedules.PrepareActivationAsync(activation, legacy is null ? [] : [legacy]);
            var request = new WorkflowActivationSlotRequest(definition, slotName, activation, WorkflowActivationSource.Publishing, 0, Now);
            var switched = interrupted ? await authority.TryActivateAsync(request) : await activationSwitch.TryActivateAsync(request);
            Assert.True(switched.Succeeded);
            return (switched.Slot, source, claim, other, legacy);
        }

        var active = await PublishAsync("active", implicitDefault);
        var key = active.Claim.Metadata[RuntimeKernelScheduleControl.ScheduleKeyMetadata];
        var observer = new EfRuntimeKernelScheduleControlStore(context, access);
        Assert.Null(await observer.FindAsync(implicitDefault, key));
        var legacy = await PublishAsync("legacy-active", explicitDefault, hasLegacy: true);
        var legacyId = legacy.Legacy!.ScheduleId;
        var cursor = await schedules.FindAsync(legacyId);
        Assert.True(cursor?.IsActive);
        await RefuseAsync(active.Slot, active.Source, active.Claim);
        Assert.Equal(cursor, await schedules.FindAsync(legacyId));
        Assert.Null(await observer.FindAsync(implicitDefault, key));
        Assert.Equal(active.Other.TriggerBindingId, Assert.Single(await bindings.ListAllByActivationAsync(active.Source.ActivationId!)).TriggerBindingId);
        Assert.True((await activationSwitch.TryDeactivateAsync(new(legacy.Slot.WorkflowDefinitionId, legacy.Slot.SlotName,
            WorkflowActivationSource.Publishing, legacy.Slot.Revision, Now), [])).Succeeded);

        await using var restarted = open();
        var restoredSource = new EfWorkflowExecutableSourceReferenceStore(restarted, access, new Codec(), clock);
        var restoredAuthority = new EfWorkflowActivationAuthority(restarted, access);
        var restoredBindings = new EfWorkflowTriggerBindingStore(restarted, access);
        var restoring = (IWorkflowScheduleAdoption)new EfWorkflowActivationSwitch(restoredAuthority, restoredBindings,
            restoredSource, access, clock, new EfRecurringTriggerScheduleStore(restarted, access, new Codec()));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var peer = Task.Run(async () =>
        {
            await start.Task;
            return await adoption.TryAdoptSchedulesAsync(active.Slot, active.Source, [active.Claim], []);
        });
        start.SetResult();
        Assert.All(await Task.WhenAll(peer, restoring.TryAdoptSchedulesAsync(active.Slot, active.Source, [active.Claim], []).AsTask()), result => Assert.True(result));
        Assert.Equal(active.Slot, await restoredAuthority.FindAsync(active.Slot.WorkflowDefinitionId, active.Slot.SlotName));
        var currentSource = Assert.IsType<WorkflowExecutableSourceReference>(await restoredSource.FindAsync(active.Source.SourceReferenceId));
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(currentSource, active.Source));
        var restoredControls = new EfRuntimeKernelScheduleControlStore(restarted, access);
        var persisted = Assert.IsType<RuntimeKernelScheduleControl>(await restoredControls.FindAsync(explicitDefault, key));
        Assert.Equal((implicitDefault, 1L, active.Source.ArtifactId, active.Slot.Revision),
            (persisted.Scope, persisted.Revision, persisted.Owner?.Executable.ArtifactId, persisted.Owner?.PublicationRevision));
        var reservations = new EfIngressReservationStore(restarted, access);
        Assert.NotNull(await reservations.FindAsync(implicitDefault, active.Other.StimulusType, active.Other.StimulusHash));
        Assert.NotNull(await reservations.FindAsync(implicitDefault, active.Claim.StimulusType, active.Claim.StimulusHash));
        Assert.Null(await reservations.FindAsync(explicitDefault, active.Claim.StimulusType, active.Claim.StimulusHash));
        var rows = await restoredBindings.ListAllByActivationAsync(active.Source.ActivationId!);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.True(row.IsActive));
        var reservationRevisions = await restarted.IngressReservations.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Revision).ToArrayAsync();
        Assert.True(await restoring.TryAdoptSchedulesAsync(active.Slot, active.Source, [active.Claim], []));
        Assert.Equal(reservationRevisions, await restarted.IngressReservations.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Revision).ToArrayAsync());
        Assert.Equal(persisted, await restoredControls.FindAsync(implicitDefault, key));

        var unfinished = await PublishAsync("unfinished", explicitDefault, interrupted: true);
        Assert.True(await restoring.TryAdoptSchedulesAsync(unfinished.Slot, unfinished.Source, [unfinished.Claim], []));
        Assert.Equal(WorkflowActivationProjectionState.Active, await bindings.FindActivationStateAsync(unfinished.Source.ActivationId!));
        Assert.Equal(WorkflowActivationProjectionState.Active, await schedules.FindActivationStateAsync(unfinished.Source.ActivationId!));
        Assert.Equal(unfinished.Slot, await authority.FindAsync(unfinished.Slot.WorkflowDefinitionId, unfinished.Slot.SlotName));
        var unfinishedKey = unfinished.Claim.Metadata[RuntimeKernelScheduleControl.ScheduleKeyMetadata];
        Assert.Equal(explicitDefault, (await restoredControls.FindAsync(implicitDefault, unfinishedKey))?.Scope);
        Assert.NotNull(await reservations.FindAsync(explicitDefault, unfinished.Claim.StimulusType, unfinished.Claim.StimulusHash));
        Assert.NotNull(await reservations.FindAsync(explicitDefault, unfinished.Other.StimulusType, unfinished.Other.StimulusHash));

        var preparedLegacy = await PublishAsync("legacy-prepared", explicitDefault, interrupted: true, hasLegacy: true);
        await RefuseAsync(preparedLegacy.Slot, preparedLegacy.Source, preparedLegacy.Claim);
        Assert.Equal(WorkflowActivationProjectionState.Prepared, await bindings.FindActivationStateAsync(preparedLegacy.Source.ActivationId!));
        Assert.Equal(WorkflowActivationProjectionState.Prepared, await schedules.FindActivationStateAsync(preparedLegacy.Source.ActivationId!));
        Assert.Equal(preparedLegacy.Legacy, await schedules.FindAsync(preparedLegacy.Legacy!.ScheduleId));

        Assert.True((await activationSwitch.TryDeactivateAsync(new(active.Slot.WorkflowDefinitionId, active.Slot.SlotName,
            WorkflowActivationSource.Publishing, active.Slot.Revision, Now), [])).Succeeded);
        Assert.False(await restoring.TryAdoptSchedulesAsync(active.Slot, active.Source, [active.Claim], []));
        Assert.Null(await reservations.FindAsync(implicitDefault, active.Claim.StimulusType, active.Claim.StimulusHash));
        Assert.Null((await restoredControls.FindAsync(implicitDefault, key))?.Owner);

        async Task RefuseAsync(WorkflowActivationSlot slot, WorkflowExecutableSourceReference source, WorkflowTriggerBinding claim)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => adoption.TryAdoptSchedulesAsync(slot, source, [claim], []).AsTask());
            Assert.Contains("legacy recurring workflow cursor", error.Message, StringComparison.Ordinal);
        }
    }
}
