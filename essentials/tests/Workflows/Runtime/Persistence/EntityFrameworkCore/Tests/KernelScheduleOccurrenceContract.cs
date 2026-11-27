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

internal static class KernelScheduleOccurrenceContract
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
        var store = Store(context, access, clock);
        ValueTask<bool> AcceptAsync(RuntimeKernelScheduleOccurrence offer, DateTimeOffset? scheduleNow = null) =>
            store.TryAcceptAsync(offer, _ => ValueTask.FromResult(scheduleNow ?? clock.GetUtcNow()));
        var first = new WorkflowExecutableIdentity("first-material", "material-producer", "material-v1", "1", "first-hash");
        var second = first with { ArtifactId = "replacement-material", DefinitionVersionId = "material-v2", ArtifactHash = "replacement-hash" };
        foreach (var identity in new[] { first, second })
            await artifacts.SaveAsync(new(identity,
                new ExecutableNode(node, "authored", "test/activity", "1.0.0",
                    new RuntimeActivityDescriptor("test", "1.0.0", JsonSerializer.SerializeToElement(new { })),
                    new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(),
                incidentStrategy: IncidentStrategyBuiltIns.FaultReference));

        async Task PublishAsync(WorkflowExecutableIdentity identity, string activation, long expectedRevision)
        {
            var slot = WorkflowActivationSlotIdentity.Create(identity.DefinitionId, slotName);
            var key = RuntimeKernelScheduleOccurrence.ReservationKey(scheduleKey);
            await references.SaveAsync(new(WorkflowActivationReferenceIdentity.Create(activation), identity.ArtifactId,
                "definition", identity.DefinitionId, identity.ArtifactVersion, identity.DefinitionId, identity.DefinitionVersionId,
                identity.ArtifactVersion, Now, Now, WorkflowExecutableReferenceScope.Published,
                ActivationId: activation, SlotId: slot, TenantId: scope.TenantId));
            await bindings.PrepareActivationAsync(activation,
                [new(WorkflowTriggerBinding.BuildId(activation, identity.ArtifactId, node, key), identity.ArtifactId,
                    identity.DefinitionId, identity.ArtifactVersion, identity.ArtifactHash, node,
                    RuntimeKernelScheduleOccurrence.ReservationType, key, null, new Dictionary<string, string>(), Now,
                    activation, slot, TriggerCardinality.ReservationOnly, false)]);
            Assert.True((await activationSwitch.TryActivateAsync(new(identity.DefinitionId, slotName, activation,
                WorkflowActivationSource.Publishing, expectedRevision, Now))).Succeeded);
        }

        var original = new RuntimeKernelScheduleOccurrence(scope, scheduleKey, new string('A', 64), slotName,
            "schedule-a", 1, 1, "timer-a", Now, Now.AddMinutes(1), first, node, "{\"version\":1}");
        await PublishAsync(first, original.ActivationId, 0);
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
        await PublishAsync(second, "schedule-b", 1);
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

        Assert.True((await activationSwitch.TryDeactivateAsync(new(first.DefinitionId, slotName,
            WorkflowActivationSource.Publishing, 2, Now), [])).Succeeded);
        clock.Advance(TimeSpan.FromMinutes(2));
        var actorReference = new string('B', 64);
        await relay.MarkMaterializedAsync(original, actorReference);
        await relay.MarkMaterializedAsync(original, actorReference);
        await Assert.ThrowsAsync<InvalidDataException>(() => relay.MarkMaterializedAsync(original, new string('C', 64)).AsTask());
        Assert.Equal(replacement, Assert.Single(await relay.ListPendingAsync(scope, null, 2)).Occurrence);
        Assert.True(await store.TryAcceptAsync(original, _ => throw new InvalidOperationException("Replays need no schedule clock.")));
        Assert.Equal(new[] { first.ArtifactId, second.ArtifactId }, (await relay.ListPinnedExecutableArtifactIdsAsync()).Order(StringComparer.Ordinal));
        Assert.Equal(3, await context.KernelScheduleOccurrences.CountAsync());
    }

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
