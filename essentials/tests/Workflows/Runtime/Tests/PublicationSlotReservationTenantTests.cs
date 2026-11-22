using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class PublicationSlotReservationTenantTests
{
    [Fact]
    public async Task Native_same_slot_keeps_its_route_when_the_captured_tenant_changes_and_can_deactivate()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        var physical = new PersistenceScope("shared-physical"); var access = new Access(physical);
        var state = new InMemoryRuntimeCheckpointStoreState();
        var material = new InMemoryWorkflowExecutableStore();
        IWorkflowExecutableStore materials = material;
        IWorkflowExecutableSourceReferenceStore references = new InMemoryWorkflowExecutableSourceReferenceStore(material, clock);
        var authority = new InMemoryWorkflowActivationAuthority(); var bindings = new InMemoryWorkflowTriggerBindingStore();
        var schedules = new InMemoryRecurringTriggerScheduleStore();
        var activation = new InMemoryWorkflowActivationSwitch(authority, references, clock, state, access, bindings, schedules);
        var coordinator = new WorkflowActivationCoordinator(materials, authority, activation, references,
            new WorkflowExecutableRootWriteLeaseManager(materials, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock),
            clock, new ExclusiveIndexer(bindings, schedules, clock), bindings, schedules, []);
        var identity = new WorkflowExecutableIdentity("shared-artifact", "definition", "version", "1", "hash");
        var node = new ExecutableNode("root", "root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        await materials.SaveAsync(new(identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), clock.GetUtcNow(),
            new Dictionary<string, string>(), new WorkflowExecutableInputContract(1, []), [], IncidentStrategyBuiltIns.FaultReference));
        var selected = (await materials.FindMaterialAsync(identity.ArtifactId))!;
        WorkflowMaterialActivationCommand Command(string tenant, string activationId, long revision) => new("definition", selected,
            new WorkflowExecutableSourceReference("candidate", identity.ArtifactId, "definition", "version", "1", "definition", "version", "1",
                clock.GetUtcNow(), clock.GetUtcNow(), WorkflowExecutableReferenceScope.Published) { TenantId = tenant },
            "default", activationId, WorkflowActivationSource.Publishing, revision);
        var first = await coordinator.ActivateMaterialAsync(Command("tenant-a", "first", 0)); Assert.True(first.Succeeded);
        var reader = new InMemoryIngressReservationReader(state, access);
        Assert.NotNull(await reader.FindAsync(new(physical, "tenant-a"), "Http", "shared-route"));
        var second = await coordinator.ActivateMaterialAsync(Command("tenant-b", "second", first.Slot.Revision)); Assert.True(second.Succeeded);
        Assert.Null(await reader.FindAsync(new(physical, "tenant-a"), "Http", "shared-route"));
        var current = (await reader.FindAsync(new(physical, "tenant-b"), "Http", "shared-route"))!;
        Assert.Equal(second.Slot.SlotId, current.OwnerId);
        var foreignSlot = new RuntimeIngressReservation(current.ReservationId, new(physical, "tenant-a"), current.StimulusType,
            current.StimulusHash, RuntimeIngressReservation.PublicationSlotOwner, "different-slot", 1, 1, new Dictionary<string, string>());
        Assert.Equal("route-conflict", Assert.Throws<TriggerOperationException>(() => RuntimeIngressReservationRules.CheckOwner(current, foreignSlot)).Code);
        RuntimeIngressReservation ResourceClaim(string tenant) => new(RuntimeIngressReservation.CreateIdentity(physical, "Http", "resource-route"),
            new(physical, tenant), "Http", "resource-route", RuntimeIngressReservation.StandaloneTriggerOwner, "same-resource-id", 1, 1,
            new Dictionary<string, string>());
        Assert.Equal("route-conflict", Assert.Throws<TriggerOperationException>(() => RuntimeIngressReservationRules.CheckOwner(
            ResourceClaim("tenant-a"), ResourceClaim("tenant-b"))).Code);
        Assert.True((await coordinator.DeactivateMaterialAsync(new("definition", selected, "default", WorkflowActivationSource.Publishing,
            second.Slot.Revision))).Succeeded);
        Assert.Empty((await reader.ListInScopeAsync(physical, new("Http"))).Items);
    }

    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope); }

    private sealed class ExclusiveIndexer(IWorkflowTriggerBindingStore bindings, IRecurringTriggerScheduleStore schedules, TimeProvider clock) : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(WorkflowExecutable executable,
            string activationId, string slotId, CancellationToken cancellationToken = default)
        {
            var pin = executable.Identity;
            var binding = new WorkflowTriggerBinding(WorkflowTriggerBinding.BuildId(activationId, pin.ArtifactId, "root", "shared-route"),
                pin.ArtifactId, pin.DefinitionId, pin.ArtifactVersion, pin.ArtifactHash, "root", "Http", "shared-route", null,
                new Dictionary<string, string>(), clock.GetUtcNow(), activationId, slotId, TriggerCardinality.Exclusive);
            await bindings.PrepareActivationAsync(activationId, [binding], cancellationToken);
            await schedules.PrepareActivationAsync(activationId, [], cancellationToken);
            return [binding];
        }
    }
}
