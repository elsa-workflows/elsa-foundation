using System.Data.Common;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal static class KernelEventPublicationScanContract
{
    private const string SlotName = "default";
    private const string Node = "event";
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    internal static async Task RunAsync(Func<RuntimeDbContext> open, Func<IInterceptor, RuntimeDbContext> intercepted)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var scope = new TriggerScope(new("event-scan"), "event-scan");
        var access = new Accessor(scope.Persistence);
        var legacySlot = WorkflowActivationSlotIdentity.Create("legacy", SlotName);
        await using (var upgrade = open())
        {
            var predecessor = upgrade.Database.GetMigrations().Single(id => id.EndsWith("_KernelEventPublications", StringComparison.Ordinal));
            await upgrade.GetService<IMigrator>().MigrateAsync(predecessor, token);
            await SeedLegacyAsync(upgrade, 1, current: false);
            await SeedLegacyAsync(upgrade, 2, current: true);
            await upgrade.Database.MigrateAsync(token);
            if (upgrade.Model.GetDefaultSchema() is null)
                Assert.False(upgrade.Database.HasPendingModelChanges());
        }

        await using var readerContext = open();
        var reader = new EfRuntimeKernelEventPublicationStore(readerContext, access);
        Assert.Equal(0L, await reader.ReadScanTailAsync(scope with { TenantId = "another-tenant" }, token));
        Assert.Equal(0L, await reader.ReadScanTailAsync(scope, token));
        var legacyDrain = Assert.Single(await reader.ListLegacyScanAsync(scope, true, null, 0, 1, token));
        Assert.Equal(1L, legacyDrain.Revision);
        var legacyApply = Assert.Single(await reader.ListLegacyScanAsync(scope, false, null, 0, 1, token));
        Assert.Equal(2L, legacyApply.Revision);
        Assert.Empty(await reader.ListLegacyScanAsync(scope, false, legacyApply.SlotId, legacyApply.Revision, 1, token));
        Assert.Equal("legacy-2", Assert.Single(await reader.ListAsync(scope, false, null, 0, 8, token)).Source!.Executable.ArtifactId);

        var held = new HeldCommit();
        await using var writer = intercepted(held);
        var transition = await PrepareAsync(writer, "new", 1);
        held.Armed = true;
        var pending = Task.Run(() => transition(token).AsTask(), token);
        long through;
        try
        {
            await held.Arrived.Task.WaitAsync(token);
            through = await reader.ReadScanTailAsync(scope, token);
            Assert.Equal(0L, through);
        }
        finally { held.Release.TrySetResult(); }
        Assert.True((await pending).Succeeded);

        var firstTail = await reader.ReadScanTailAsync(scope, token);
        Assert.Equal(1L, firstTail);
        for (var index = 1; index <= 4; index++)
        {
            var publish = await PrepareAsync(writer, $"later-{index}", 1);
            Assert.True((await publish(token)).Succeeded);
            Assert.Empty(await reader.ListScanAsync(scope, false, 0, through, 1, token));
            Assert.Equal(firstTail, Assert.Single(await reader.ListScanAsync(scope, false, 0, firstTail, 8, token)).Ordinal);
            Assert.Empty(await reader.ListScanAsync(scope, false, firstTail, firstTail, 1, token));
        }

        await using var restarted = open();
        var reloaded = new EfRuntimeKernelEventPublicationStore(restarted, access);
        var newTail = await reloaded.ReadScanTailAsync(scope, token);
        Assert.Equal(5L, newTail);
        var newlyVisible = await reloaded.ListScanAsync(scope, false, through, newTail, 4, token);
        Assert.Equal([1L, 2L, 3L, 4L], newlyVisible.Select(item => item.Ordinal));
        Assert.Equal("new", newlyVisible[0].Publication.DefinitionId);
        Assert.True(await reloaded.AcknowledgeAsync(newlyVisible[0].Publication, token));
        Assert.Equal(legacyApply.SlotId, Assert.Single(await reloaded.ListLegacyScanAsync(scope, false, null, 0, 4, token)).SlotId);
        Assert.True(await reloaded.ReleaseDrainedAsync(legacyDrain, token));
        Assert.Empty(await reloaded.ListLegacyScanAsync(scope, true, null, 0, 4, token));
        Assert.Empty(await reloaded.ListScanAsync(scope, true, 0, newTail, 4, token));
        Assert.Equal(newTail, await reloaded.ReadScanTailAsync(scope, token));

        var supersede = await PrepareAsync(writer, "later-4", 2);
        Assert.True((await supersede(token)).Succeeded);
        var removable = Assert.Single(await reloaded.ListScanAsync(scope, true, 0, newTail, 4, token));
        Assert.Equal(newTail, removable.Ordinal);
        Assert.True(await reloaded.ReleaseDrainedAsync(removable.Publication, token));
        Assert.Equal(6L, await reloaded.ReadScanTailAsync(scope, token));

        async Task SeedLegacyAsync(RuntimeDbContext db, long revision, bool current)
        {
            var identity = Identity("legacy", revision);
            var activation = $"legacy-{revision}-activation";
            var publication = new RuntimeKernelEventPublication(scope, legacySlot, SlotName, "legacy", revision,
                new(activation, WorkflowActivationReferenceIdentity.Create(activation), "definition", "legacy", "1", identity),
                [new(Node, "Upgrade")]);
            var json = RuntimeArtifactJson.Serialize(publication);
            var table = db.GetService<ISqlGenerationHelper>().DelimitIdentifier(RuntimeKernelEventPublicationEfModule.TableName,
                db.Model.GetDefaultSchema());
            var insert = $$"""
                INSERT INTO {{table}}
                    ("ScopeHash", "SlotHash", "TenantHash", "Revision", "ArtifactId", "ContentJson", "Current", "Applied", "SchemaVersion")
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, false, '1.0.0')
                """;
            await db.Database.ExecuteSqlRawAsync(insert, [Hash(scope.Persistence.Value), Hash(legacySlot), Hash(scope.TenantId!),
                revision, identity.ArtifactId, json, current], token);
        }

        async Task<Func<CancellationToken, ValueTask<WorkflowActivationTransition>>> PrepareAsync(RuntimeDbContext db, string definition, long revision)
        {
            var identity = Identity(definition, revision);
            var activation = $"{definition}-{revision}-activation";
            var slot = WorkflowActivationSlotIdentity.Create(definition, SlotName);
            var claim = RuntimeKernelEventPublication.ReservationKey(slot, Node);
            var bindings = new EfWorkflowTriggerBindingStore(db, access);
            var references = new EfWorkflowExecutableSourceReferenceStore(db, access, new Codec(), TimeProvider.System);
            await new EfWorkflowExecutableStore(db, access).SaveAsync(new(identity,
                new ExecutableNode(Node, "authored", "test/event", "1.0.0",
                    new RuntimeActivityDescriptor("test", "1.0.0", JsonSerializer.SerializeToElement(new { })),
                    new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(),
                incidentStrategy: IncidentStrategyBuiltIns.FaultReference), token);
            await references.SaveAsync(new(WorkflowActivationReferenceIdentity.Create(activation), identity.ArtifactId,
                "definition", definition, "1", definition, identity.DefinitionVersionId, identity.ArtifactVersion, Now, Now,
                WorkflowExecutableReferenceScope.Published, ActivationId: activation, SlotId: slot, TenantId: scope.TenantId), token);
            await bindings.PrepareActivationAsync(activation,
                [new(WorkflowTriggerBinding.BuildId(activation, identity.ArtifactId, Node, claim), identity.ArtifactId,
                    definition, identity.ArtifactVersion, identity.ArtifactHash, Node, RuntimeKernelEventPublication.ReservationType,
                    claim, null, new Dictionary<string, string> { [RuntimeKernelEventPublication.EventNameMetadata] = "Upgrade" },
                    Now, activation, slot, TriggerCardinality.ReservationOnly, IsActive: false)], token);
            var activationSwitch = new EfWorkflowActivationSwitch(new EfWorkflowActivationAuthority(db, access), bindings,
                references, access, TimeProvider.System);
            return ct => activationSwitch.TryActivateAsync(new(definition, SlotName, activation,
                WorkflowActivationSource.Publishing, revision - 1, Now), ct);
        }
    }

    private static WorkflowExecutableIdentity Identity(string definition, long revision) =>
        new($"{definition}-{revision}", definition, $"v{revision}", $"{revision}", $"{definition}-{revision}-hash");

    private sealed class Accessor(PersistenceScope scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope);
    }

    private sealed class Codec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }

    private sealed class HeldCommit : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!Armed) return result;
            Armed = false;
            Arrived.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
