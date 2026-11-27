using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal static partial class KernelScheduleOccurrenceContract
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 1, 0, 0, TimeSpan.Zero);

    internal static async Task RunAsync(Func<RuntimeDbContext> open)
    {
        const string slotName = "Blue";
        const string scheduleKey = "schedule:material-producer:node";
        const string node = "node";
        await using var context = open();
        var tenant = $"schedule-{Guid.NewGuid():N}";
        var scope = new TriggerScope(new PersistenceScope(tenant), tenant);
        var clock = new FakeTimeProvider(Now);
        var access = new Accessor(scope.Persistence);
        var artifacts = new EfWorkflowExecutableStore(context, access);
        var references = new EfWorkflowExecutableSourceReferenceStore(context, access, new Codec(), clock);
        var bindings = new EfWorkflowTriggerBindingStore(context, access);
        var authority = new EfWorkflowActivationAuthority(context, access);
        var activationSwitch = new EfWorkflowActivationSwitch(authority, bindings, references, access, clock);
        var controls = new EfRuntimeKernelScheduleControlStore(context, access);
        var store = Store(context, access, clock);
        ValueTask<bool> AcceptAsync(RuntimeKernelScheduleOccurrence offer, DateTimeOffset? scheduleNow = null) =>
            store.TryAcceptAsync(offer, _ => ValueTask.FromResult(scheduleNow ?? clock.GetUtcNow()));
        var first = new WorkflowExecutableIdentity("first-material", "material-producer", "material-v1", "1", "first-hash");
        var second = first with { ArtifactId = "replacement-material", DefinitionVersionId = "material-v2", ArtifactHash = "replacement-hash" };
        foreach (var identity in new[] { first, second }) await artifacts.SaveAsync(Executable(identity, node));

        async Task PrepareAsync(WorkflowExecutableIdentity identity, string activation, string name, string trigger = node, string? overrideKey = null)
        {
            var slot = WorkflowActivationSlotIdentity.Create(identity.DefinitionId, name);
            var logicalKey = $"schedule:{identity.DefinitionId}:{trigger}";
            var key = RuntimeKernelScheduleOccurrence.ReservationKey(logicalKey);
            await references.SaveAsync(new(WorkflowActivationReferenceIdentity.Create(activation), identity.ArtifactId,
                "definition", identity.DefinitionId, identity.ArtifactVersion, identity.DefinitionId, identity.DefinitionVersionId,
                identity.ArtifactVersion, Now, Now, WorkflowExecutableReferenceScope.Published,
                ActivationId: activation, SlotId: slot, TenantId: scope.TenantId));
            await bindings.PrepareActivationAsync(activation,
                [new(WorkflowTriggerBinding.BuildId(activation, identity.ArtifactId, trigger, key), identity.ArtifactId,
                    identity.DefinitionId, identity.ArtifactVersion, identity.ArtifactHash, trigger,
                    RuntimeKernelScheduleOccurrence.ReservationType, key, null,
                    new Dictionary<string, string> { [RuntimeKernelScheduleControl.ScheduleKeyMetadata] = overrideKey ?? logicalKey }, Now,
                    activation, slot, TriggerCardinality.ReservationOnly, false)]);
        }

        async Task<WorkflowActivationSlot> PublishAsync(WorkflowExecutableIdentity identity, string activation, long expectedRevision)
        {
            await PrepareAsync(identity, activation, slotName);
            var transition = await activationSwitch.TryActivateAsync(Request(activation, slotName, expectedRevision));
            Assert.True(transition.Succeeded);
            return transition.Slot;
        }

        WorkflowActivationSlotRequest Request(string activation, string slot, long revision) =>
            new(first.DefinitionId, slot, activation, WorkflowActivationSource.Publishing, revision, Now);

        var original = new RuntimeKernelScheduleOccurrence(scope, scheduleKey, new string('A', 64), slotName,
            "schedule-a", 1, 1, "timer-a", Now, Now.AddMinutes(1), first, node, "{\"version\":1}");
        await PublishAsync(first, original.ActivationId, 0);
        var exact = Assert.IsType<RuntimeKernelScheduleControl>(await controls.FindAsync(scope, scheduleKey));
        Assert.Equal(new(scope, scheduleKey, first.DefinitionId, node, 1,
            new(WorkflowActivationSlotIdentity.Create(first.DefinitionId, slotName), slotName, original.ActivationId, 1,
                WorkflowActivationReferenceIdentity.Create(original.ActivationId), first)), exact);
        Assert.False(await AcceptAsync(original with { Due = Now.AddSeconds(1) }));
        var clockReads = 0;
        Assert.False(await store.TryAcceptAsync(original, _ => ValueTask.FromResult(++clockReads == 3 ? original.AcceptBefore : Now)));
        Assert.Empty(await store.ListPendingAsync(scope, null, 1));
        Assert.Empty(await store.ListPinnedExecutableArtifactIdsAsync());

        await using var contenderContext = open();
        var contender = Store(contenderContext, access, clock);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contenderTask = Task.Run(async () =>
        {
            await start.Task;
            return await contender.TryAcceptAsync(original, _ => ValueTask.FromResult(Now));
        });
        start.SetResult();
        Assert.All(await Task.WhenAll(contenderTask, AcceptAsync(original).AsTask()), accepted => Assert.True(accepted));
        Assert.Single(await store.ListPendingAsync(scope, null, 2));
        var secondSlot = await PublishAsync(second, "schedule-b", 1);
        var secondControl = Assert.IsType<RuntimeKernelScheduleControl>(await controls.FindAsync(scope, scheduleKey));
        Assert.Equal(2, secondControl.Revision);
        Assert.Equal(second, secondControl.Owner?.Executable);
        var preparedSchedules = new EfRecurringTriggerScheduleStore(context, access, new Codec());
        await preparedSchedules.PrepareActivationAsync("schedule-b", []);
        var repairing = new EfWorkflowActivationSwitch(authority, bindings, references, access, clock, preparedSchedules);
        Assert.True(await repairing.TryRepairAsync(secondSlot, []));
        Assert.Equal(secondControl, await controls.FindAsync(scope, scheduleKey));
        Assert.False(await AcceptAsync(original with { ArmGeneration = 2, TimerDeliveryId = "stale-timer" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => AcceptAsync(original with { PinnedStart = "{\"version\":2}" }).AsTask());

        var replacement = original with { ActivationId = "schedule-b", PublicationRevision = 2, ArmGeneration = 2,
            TimerDeliveryId = "timer-b", Executable = second, PinnedStart = "{\"version\":2}" };
        Assert.True(await AcceptAsync(replacement));
        Assert.False(await AcceptAsync(replacement with { ArmGeneration = 3, TimerDeliveryId = "expired-timer" }, Now.AddMinutes(2)));
        await using var restarted = open();
        var relay = Store(restarted, access, clock);
        var pageOne = Assert.Single(await relay.ListPendingAsync(scope, null, 1));
        var pageTwo = Assert.Single(await relay.ListPendingAsync(scope, pageOne.Cursor, 1));
        Assert.Empty(await relay.ListPendingAsync(scope, pageTwo.Cursor, 1));
        Assert.Contains(original, new[] { pageOne.Occurrence, pageTwo.Occurrence });
        Assert.Contains(replacement, new[] { pageOne.Occurrence, pageTwo.Occurrence });

        var delayed = replacement with { ArmGeneration = 4, TimerDeliveryId = "delayed-commit",
            Due = Now.AddMinutes(-2), AcceptBefore = Now.AddSeconds(-1) };
        Assert.True(await AcceptAsync(delayed, Now.AddMinutes(-1)));
        Assert.Empty(await relay.ListPendingAsync(scope, pageTwo.Cursor, 1));
        Assert.Equal(delayed, Assert.Single(await relay.ListPendingAsync(scope, null, 1)).Occurrence);
        await relay.MarkMaterializedAsync(delayed, new string('D', 64));

        var binding = Assert.Single(await bindings.ListAllByActivationAsync(replacement.ActivationId));
        await bindings.SaveAsync(binding with { IsActive = false });
        Assert.False(await AcceptAsync(replacement with { ArmGeneration = 5, TimerDeliveryId = "inactive-binding" }));
        await bindings.SaveAsync(binding);

        await PrepareAsync(first, "malformed-claim", "Malformed", overrideKey: "another-definition");
        await Assert.ThrowsAsync<InvalidDataException>(() => activationSwitch.TryActivateAsync(Request("malformed-claim", "Malformed", 0)).AsTask());
        Assert.Null(await authority.FindAsync(first.DefinitionId, "Malformed"));
        Assert.Equal(secondControl, await controls.FindAsync(scope, scheduleKey));

        await PrepareAsync(first, "lower-green", "Green");
        await PrepareAsync(second, "lower-other", "Other");
        var conflict = await Assert.ThrowsAsync<TriggerOperationException>(() => activationSwitch.TryActivateAsync(Request("lower-green", "Green", 0)).AsTask());
        Assert.Equal("route-conflict", conflict.Code);
        Assert.Equal(secondControl, await controls.FindAsync(scope, scheduleKey));

        Assert.True((await activationSwitch.TryDeactivateAsync(new(first.DefinitionId, slotName,
            WorkflowActivationSource.Publishing, 2, Now), [])).Succeeded);
        var released = secondControl with { Revision = 3, Owner = null };
        Assert.Equal(released, await controls.FindAsync(scope, scheduleKey));
        Assert.Equal(released, Assert.Single((await controls.ListAsync(scope, null, 1)).Items));
        var staleRelease = await activationSwitch.TryDeactivateAsync(new(first.DefinitionId, slotName, WorkflowActivationSource.Publishing, 2, Now), []);
        Assert.False(staleRelease.Succeeded);
        Assert.Equal(released, await controls.FindAsync(scope, scheduleKey));

        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<WorkflowActivationSlot?> ContendAsync(string slot, string activation)
        {
            await using var contenderDb = open();
            var contenderSwitch = new EfWorkflowActivationSwitch(new EfWorkflowActivationAuthority(contenderDb, access),
                new EfWorkflowTriggerBindingStore(contenderDb, access), new EfWorkflowExecutableSourceReferenceStore(contenderDb, access, new Codec(), clock), access, clock);
            await begin.Task;
            try
            {
                var result = await contenderSwitch.TryActivateAsync(Request(activation, slot, 0));
                return result.Succeeded ? result.Slot : throw new InvalidOperationException("The contender did not have its expected slot revision.");
            }
            catch (TriggerOperationException error) when (error.Code == "route-conflict") { return null; }
        }
        var attempts = new[] { ContendAsync("Green", "lower-green"), ContendAsync("Other", "lower-other") };
        begin.SetResult();
        var winningSlot = Assert.Single((await Task.WhenAll(attempts)).OfType<WorkflowActivationSlot>());
        Assert.Equal(1, winningSlot.Revision);
        var reclaimed = Assert.IsType<RuntimeKernelScheduleControl>(await controls.FindAsync(scope, scheduleKey));
        Assert.Equal(4, reclaimed.Revision);
        Assert.Equal(winningSlot.SlotId, reclaimed.Owner?.SlotId);
        Assert.Equal(winningSlot.ActiveActivationId, reclaimed.Owner?.ActivationId);
        Assert.Equal(1, reclaimed.Owner?.PublicationRevision);
        Assert.False((await activationSwitch.TryDeactivateAsync(new(first.DefinitionId, slotName,
            WorkflowActivationSource.Publishing, 2, Now), [])).Succeeded);
        Assert.Equal(reclaimed, await controls.FindAsync(scope, scheduleKey));

        // An unrelated node advances its own revision and uses the same scoped paging path.
        await PrepareAsync(first, "separate-node", "Separate", "another-node");
        Assert.True((await activationSwitch.TryActivateAsync(Request("separate-node", "Separate", 0))).Succeeded);
        clock.Advance(TimeSpan.FromMinutes(2));
        var controlReads = new EfRuntimeKernelScheduleControlStore(restarted, access);
        var desiredFirst = await controlReads.ListAsync(scope, null, 1);
        var desiredSecond = await controlReads.ListAsync(scope, Assert.IsType<string>(desiredFirst.Next), 1);
        Assert.Null(desiredSecond.Next);
        Assert.Contains(reclaimed, desiredFirst.Items.Concat(desiredSecond.Items));
        Assert.Contains(desiredFirst.Items.Concat(desiredSecond.Items), control => control.TriggerNode == "another-node" && control.Revision == 1);
        Assert.Empty((await controlReads.ListAsync(scope with { TenantId = null }, null, 1)).Items);
        Assert.Null(await controlReads.FindAsync(scope with { TenantId = null }, scheduleKey));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controlReads.ListAsync(scope with { TenantId = "another-tenant" }, null, 1).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controlReads.ListAsync(scope with { Persistence = new("a-different-runtime") }, null, 1).AsTask());

        var actorReference = new string('B', 64);
        await relay.MarkMaterializedAsync(original, actorReference);
        await relay.MarkMaterializedAsync(original, actorReference);
        await Assert.ThrowsAsync<InvalidDataException>(() => relay.MarkMaterializedAsync(original, new string('C', 64)).AsTask());
        Assert.Equal(replacement, Assert.Single(await relay.ListPendingAsync(scope, null, 2)).Occurrence);
        Assert.True(await store.TryAcceptAsync(original, _ => throw new InvalidOperationException("Replays need no schedule clock.")));
        Assert.Equal(new[] { first.ArtifactId, second.ArtifactId }, (await relay.ListPinnedExecutableArtifactIdsAsync()).Order(StringComparer.Ordinal));

        var openFirst = Assert.Single(await relay.ListMaterializedAsync(scope, null, 1));
        var openSecond = Assert.Single(await relay.ListMaterializedAsync(scope, openFirst.Cursor, 1));
        Assert.Empty(await relay.ListMaterializedAsync(scope, openSecond.Cursor, 1));
        Assert.Equal(delayed, openFirst.Occurrence);
        Assert.Equal(original, openSecond.Occurrence);
        static ValueTask<bool> MustNotReadProof(RuntimeKernelScheduleOccurrence _, string __, CancellationToken ___) =>
            throw new InvalidOperationException("This call cannot request a retirement proof.");
        await Assert.ThrowsAsync<InvalidDataException>(() => relay.TryRetireAsync(original, new string('C', 64), MustNotReadProof).AsTask());
        Assert.False(await relay.TryRetireAsync(original, actorReference, (_, _, _) => ValueTask.FromResult(false)));
        Assert.Contains(first.ArtifactId, await relay.ListPinnedExecutableArtifactIdsAsync());
        Assert.True(await relay.TryRetireAsync(original, actorReference, (proofOccurrence, proofReference, _) =>
        {
            Assert.Equal(original, proofOccurrence);
            Assert.Equal(actorReference, proofReference);
            return ValueTask.FromResult(true);
        }));
        Assert.Equal(delayed, Assert.Single(await relay.ListMaterializedAsync(scope, null, 2)).Occurrence);
        Assert.Equal(second.ArtifactId, Assert.Single(await relay.ListPinnedExecutableArtifactIdsAsync()));
        var retired = await context.KernelScheduleOccurrences.AsNoTracking().SingleAsync(row => row.ActorReference == actorReference);
        Assert.NotNull(retired.RetiredAtUtcTicks);
        Assert.Empty(retired.ArtifactId);
        Assert.DoesNotContain("PinnedStart", retired.ContentJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(first.ArtifactId, retired.ContentJson);
        Assert.True(await relay.TryRetireAsync(original, actorReference, MustNotReadProof));
        Assert.True(await store.TryAcceptAsync(original, _ => throw new InvalidOperationException("Retired replays need no schedule clock.")));
        await relay.MarkMaterializedAsync(original, actorReference);
        await Assert.ThrowsAsync<InvalidDataException>(() => AcceptAsync(original with { PinnedStart = "{\"version\":2}" }).AsTask());
        Assert.Equal(3, await context.KernelScheduleOccurrences.CountAsync());
        await CheckDefaultAliasHandoffAsync(open);
        await CheckScheduleAdoptionAsync(open);
    }

    private static async Task CheckDefaultAliasHandoffAsync(Func<RuntimeDbContext> open)
    {
        const string defaultTenant = PersistenceScope.DefaultValue;
        const string definition = "default-alias-timer";
        const string node = "timer";
        const string slotName = "Blue";
        const string key = "schedule:default-alias-timer:timer";
        var unqualified = new TriggerScope(new(defaultTenant), null);
        var named = unqualified with { TenantId = defaultTenant };
        var access = new Accessor(unqualified.Persistence);
        var clock = new FakeTimeProvider(Now);
        var identity = new WorkflowExecutableIdentity("default-alias-artifact", definition, "v1", "1", "default-alias-hash");
        var slot = WorkflowActivationSlotIdentity.Create(definition, slotName);
        var claim = RuntimeKernelScheduleOccurrence.ReservationKey(key);
        await using var context = open();
        var artifacts = new EfWorkflowExecutableStore(context, access);
        var references = new EfWorkflowExecutableSourceReferenceStore(context, access, new Codec(), clock);
        var bindings = new EfWorkflowTriggerBindingStore(context, access);
        var authority = new EfWorkflowActivationAuthority(context, access);
        var activationSwitch = new EfWorkflowActivationSwitch(authority, bindings, references, access, clock);
        var claims = new EfIngressReservationStore(context, access);
        await artifacts.SaveAsync(Executable(identity, node));

        var physicalScope = EfRuntimeOperationalStoreSupport.Hash(defaultTenant);
        string? rowId = null;
        var aliases = new[] { unqualified, named, unqualified };
        for (var i = 0; i < aliases.Length; i++)
        {
            var exact = aliases[i];
            var activation = $"default-alias-{i + 1}";
            var sourceReferenceId = WorkflowActivationReferenceIdentity.Create(activation);
            await references.SaveAsync(new(sourceReferenceId, identity.ArtifactId, "definition", definition, identity.ArtifactVersion,
                definition, identity.DefinitionVersionId, identity.ArtifactVersion, Now, Now, WorkflowExecutableReferenceScope.Published,
                ActivationId: activation, SlotId: slot, TenantId: exact.TenantId));
            await bindings.PrepareActivationAsync(activation,
                [new(WorkflowTriggerBinding.BuildId(activation, identity.ArtifactId, node, claim), identity.ArtifactId,
                    definition, identity.ArtifactVersion, identity.ArtifactHash, node, RuntimeKernelScheduleOccurrence.ReservationType,
                    claim, null, new Dictionary<string, string> { [RuntimeKernelScheduleControl.ScheduleKeyMetadata] = key }, Now,
                    activation, slot, TriggerCardinality.ReservationOnly, false)]);
            Assert.True((await activationSwitch.TryActivateAsync(new(definition, slotName, activation,
                WorkflowActivationSource.Publishing, i, Now))).Succeeded);

            await using var restarted = open();
            var reader = new EfRuntimeKernelScheduleControlStore(restarted, access);
            var expected = new RuntimeKernelScheduleControl(exact, key, definition, node, i + 1,
                new(slot, slotName, activation, i + 1, sourceReferenceId, identity));
            Assert.Equal(expected, await reader.FindAsync(unqualified, key));
            Assert.Equal(expected, await reader.FindAsync(named, key));
            Assert.Equal(expected, Assert.Single((await reader.ListAsync(unqualified, null, 1)).Items));
            Assert.Equal(expected, Assert.Single((await reader.ListAsync(named, null, 1)).Items));
            var physicalId = await restarted.KernelScheduleControls.Where(row => row.ScopeKeyHash == physicalScope).Select(row => row.Id).SingleAsync();
            rowId ??= physicalId;
            Assert.Equal(rowId, physicalId);
            Assert.NotNull(await claims.FindAsync(exact, RuntimeKernelScheduleOccurrence.ReservationType, claim));
            Assert.Null(await claims.FindAsync(exact.TenantId is null ? named : unqualified, RuntimeKernelScheduleOccurrence.ReservationType, claim));
        }
    }

    private static WorkflowExecutable Executable(WorkflowExecutableIdentity identity, string node) => new(identity,
        new ExecutableNode(node, "authored", "test/activity", "1.0.0",
            new RuntimeActivityDescriptor("test", "1.0.0", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
        new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(),
        incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static EfRuntimeKernelScheduleOccurrenceStore Store(RuntimeDbContext context, Accessor access, TimeProvider clock)
    {
        var artifacts = new EfWorkflowExecutableStore(context, access);
        var leases = new WorkflowExecutableRootWriteLeaseManager(artifacts,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock);
        return new(context, access, artifacts, new EfWorkflowExecutableSourceReferenceStore(context, access, new Codec(), clock),
            new EfWorkflowTriggerBindingStore(context, access), leases, clock);
    }

    private sealed class Accessor(PersistenceScope scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope);
    }

    private sealed class Codec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }
}
