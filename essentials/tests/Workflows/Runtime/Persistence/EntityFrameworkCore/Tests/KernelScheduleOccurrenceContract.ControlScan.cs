using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal static partial class KernelScheduleOccurrenceContract
{
    private const string ControlTenant = "schedule-control-scan";
    private const string ControlNode = "tick";
    private static TriggerScope ControlScope => new(new PersistenceScope(ControlTenant), ControlTenant);

    internal static async Task InstallWithLegacyControlsAsync(RuntimeDbContext context)
    {
        var prior = context.Database.GetMigrations().Single(id => id.EndsWith("_KernelScheduleOccurrenceRetirement", StringComparison.Ordinal));
        await context.GetService<IMigrator>().MigrateAsync(prior);
        foreach (var control in LegacyControls())
        {
            var id = ControlId(control.ScheduleKey);
            var scopeHash = Hash(ControlTenant);
            var tenantHash = Hash(JsonSerializer.Serialize(ControlTenant));
            var json = RuntimeArtifactJson.Serialize(control);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "elsa_runtime_kernel_schedule_control" ("Id", "ScopeKeyHash", "TenantKeyHash", "ContentJson", "SchemaVersion", "Revision")
                VALUES ({id}, {scopeHash}, {tenantHash}, {json}, '1.0.0', {control.Revision})
                """);
        }
        await context.Database.MigrateAsync();
    }

    private static async Task CheckControlScanAsync(Func<RuntimeDbContext> open, Func<IInterceptor, RuntimeDbContext> intercepted)
    {
        var scope = ControlScope;
        var access = new Accessor(scope.Persistence);
        var legacy = LegacyControls();
        var legacyKeys = legacy.Select(control => control.ScheduleKey).ToArray();
        var orderedLegacy = legacyKeys.Select(ControlId).ToArray();
        await using var writer = open();
        var priorKey = await PublishAsync("before-control-scan");
        IRuntimeKernelScheduleControlScanPage obsolete;
        await using (var firstRead = open())
        await using (var contender = open())
        {
            var first = new EfRuntimeKernelScheduleControlStore(firstRead, access);
            var peer = new EfRuntimeKernelScheduleControlStore(contender, access);
            var page = Assert.IsAssignableFrom<IRuntimeKernelScheduleControlScanPage>(await first.ReadScanPageAsync(scope, 1));
            obsolete = Assert.IsAssignableFrom<IRuntimeKernelScheduleControlScanPage>(await peer.ReadScanPageAsync(scope, 1));
            Assert.True(page.HasMore);
            Assert.Equal(legacy[0], Assert.Single(page.Items));
            Assert.Equal(legacy[0], Assert.Single(obsolete.Items));
            Assert.True(await first.TryAdvanceScanAsync(scope, page));
            Assert.False(await peer.TryAdvanceScanAsync(scope, obsolete));
        }

        // Every new hash is inside the initial legacy hash interval. Publication continues between pages.
        var lateDefinitions = Enumerable.Range(0, 80).Select(index => "during-control-scan-" + index)
            .Where(definition => string.CompareOrdinal(orderedLegacy[0], ControlId(ControlKey(definition))) < 0 &&
                string.CompareOrdinal(ControlId(ControlKey(definition)), orderedLegacy[1]) < 0).Take(4).ToArray();
        Assert.Equal(4, lateDefinitions.Length);
        var late = new List<string> { await PublishAsync(lateDefinitions[0]) };

        await using var restarted = open();
        var reader = new EfRuntimeKernelScheduleControlStore(restarted, access);
        var secondPage = Assert.IsAssignableFrom<IRuntimeKernelScheduleControlScanPage>(await reader.ReadScanPageAsync(scope, 1));
        Assert.True(secondPage.HasMore);
        Assert.Equal(legacy[1], Assert.Single(secondPage.Items));
        late.Add(await PublishAsync(lateDefinitions[1]));
        Assert.True(await reader.TryAdvanceScanAsync(scope, secondPage));
        var lastPage = Assert.IsAssignableFrom<IRuntimeKernelScheduleControlScanPage>(await reader.ReadScanPageAsync(scope, 1));
        Assert.False(lastPage.HasMore);
        Assert.Equal(priorKey, Assert.Single(lastPage.Items).ScheduleKey);
        late.Add(await PublishAsync(lateDefinitions[2]));
        Assert.True(await reader.TryAdvanceScanAsync(scope, lastPage));

        var freshPass = Assert.IsAssignableFrom<IRuntimeKernelScheduleControlScanPage>(await reader.ReadScanPageAsync(scope, 32));
        late.Add(await PublishAsync(lateDefinitions[3]));
        Assert.Equal([.. legacyKeys, priorKey, .. late.Take(3)], freshPass.Items.Select(control => control.ScheduleKey));
        Assert.False(await reader.TryAdvanceScanAsync(scope, obsolete));
        Assert.True(await reader.TryAdvanceScanAsync(scope, freshPass));
        var followingPass = Assert.IsAssignableFrom<IRuntimeKernelScheduleControlScanPage>(await reader.ReadScanPageAsync(scope, 32));
        Assert.Equal([.. legacyKeys, priorKey, .. late], followingPass.Items.Select(control => control.ScheduleKey));
        Assert.True(await reader.TryAdvanceScanAsync(scope, followingPass));

        var scopeHash = Hash(ControlTenant);
        var physical = await restarted.KernelScheduleControls.AsNoTracking().Where(row => row.ScopeKeyHash == scopeHash)
            .OrderBy(row => row.InsertionOrdinal).ThenBy(row => row.Id).ToArrayAsync();
        Assert.Equal([0L, 0L, 1L, 2L, 3L, 4L, 5L], physical.Select(row => row.InsertionOrdinal));
        Assert.Equal(legacy.Select(RuntimeArtifactJson.Serialize), physical.Take(2).Select(row => row.ContentJson));
        Assert.All(physical, row => Assert.Equal("1.0.0", row.SchemaVersion));
        await CheckContendersAsync(rollBack: false, previousOrdinal: 5);
        await CheckContendersAsync(rollBack: true, previousOrdinal: 7);

        async Task<string> PublishAsync(string definition)
        {
            var publish = await PrepareAsync(writer, definition);
            Assert.True((await publish(CancellationToken.None)).Succeeded);
            return ControlKey(definition);
        }

        async Task CheckContendersAsync(bool rollBack, long previousOrdinal)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var token = deadline.Token;
            var gate = new HeldControlCommit(rollBack);
            await using var holding = intercepted(gate);
            await using var competing = open();
            var firstDefinition = "control-hold-" + previousOrdinal;
            var secondDefinition = "control-compete-" + previousOrdinal;
            var first = await PrepareAsync(holding, firstDefinition);
            var second = await PrepareAsync(competing, secondDefinition);
            var selected = Assert.IsAssignableFrom<IRuntimeKernelScheduleControlScanPage>(await reader.ReadScanPageAsync(scope, 32, token));
            gate.Arm();
            var firstWrite = first(token).AsTask();
            await gate.Arrived.Task.WaitAsync(token);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondWrite = Task.Run(async () =>
            {
                started.TrySetResult();
                return await second(token);
            }, token);
            try
            {
                await started.Task.WaitAsync(token);
                Assert.Null(await reader.FindAsync(scope, ControlKey(firstDefinition), token));
                Assert.Null(await reader.FindAsync(scope, ControlKey(secondDefinition), token));
                Assert.False(secondWrite.IsCompleted);
            }
            finally { gate.Release.TrySetResult(); }

            if (rollBack)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => firstWrite);
                Assert.Null(await ReadOrdinalAsync(firstDefinition, token));
            }
            else
            {
                Assert.True((await firstWrite).Succeeded);
                Assert.Equal(previousOrdinal + 1, await ReadOrdinalAsync(firstDefinition, token));
            }
            Assert.True((await secondWrite).Succeeded);
            Assert.Equal(previousOrdinal + (rollBack ? 1 : 2), await ReadOrdinalAsync(secondDefinition, token));
            Assert.True(await reader.TryAdvanceScanAsync(scope, selected, token));
        }

        Task<long?> ReadOrdinalAsync(string definition, CancellationToken token)
        {
            var id = ControlId(ControlKey(definition));
            return restarted.KernelScheduleControls.AsNoTracking().Where(row => row.Id == id)
                .Select(row => (long?)row.InsertionOrdinal).SingleOrDefaultAsync(token);
        }

        async Task<Func<CancellationToken, ValueTask<WorkflowActivationTransition>>> PrepareAsync(RuntimeDbContext target, string definition)
        {
            const string slotName = "Blue";
            var activation = definition + "-activation";
            var key = ControlKey(definition);
            var hash = RuntimeKernelScheduleOccurrence.ReservationKey(key);
            var slot = WorkflowActivationSlotIdentity.Create(definition, slotName);
            var identity = new WorkflowExecutableIdentity(definition + "-artifact", definition, "v1", "1", definition + "-hash");
            var clock = new FakeTimeProvider(Now);
            var bindings = new EfWorkflowTriggerBindingStore(target, access);
            var references = new EfWorkflowExecutableSourceReferenceStore(target, access, new Codec(), clock);
            await new EfWorkflowExecutableStore(target, access).SaveAsync(Executable(identity, ControlNode));
            await references.SaveAsync(new(WorkflowActivationReferenceIdentity.Create(activation), identity.ArtifactId,
                "definition", definition, "1", definition, identity.DefinitionVersionId, identity.ArtifactVersion, Now, Now,
                WorkflowExecutableReferenceScope.Published, ActivationId: activation, SlotId: slot, TenantId: scope.TenantId));
            await bindings.PrepareActivationAsync(activation,
                [new(WorkflowTriggerBinding.BuildId(activation, identity.ArtifactId, ControlNode, hash), identity.ArtifactId,
                    definition, identity.ArtifactVersion, identity.ArtifactHash, ControlNode, RuntimeKernelScheduleOccurrence.ReservationType,
                    hash, null, new Dictionary<string, string> { [RuntimeKernelScheduleControl.ScheduleKeyMetadata] = key },
                    Now, activation, slot, TriggerCardinality.ReservationOnly, false)]);
            var activationSwitch = new EfWorkflowActivationSwitch(new EfWorkflowActivationAuthority(target, access), bindings,
                references, access, clock);
            return token => activationSwitch.TryActivateAsync(new(definition, slotName, activation,
                WorkflowActivationSource.Publishing, 0, Now), token);
        }
    }

    private sealed class HeldControlCommit(bool rollBack) : DbTransactionInterceptor
    {
        private bool _armed;
        internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Arm() => _armed = true;

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!_armed) return result;
            _armed = false;
            Arrived.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            if (rollBack) throw new InvalidOperationException("The fixture refused the staged publication commit.");
            return result;
        }
    }

    private static RuntimeKernelScheduleControl[] LegacyControls()
    {
        var candidates = Enumerable.Range(0, 32).Select(index => "legacy-control-scan-" + index)
            .OrderBy(definition => ControlId(ControlKey(definition)), StringComparer.Ordinal).ToArray();
        return new[] { candidates[0], candidates[^1] }.Select(definition =>
            new RuntimeKernelScheduleControl(ControlScope, ControlKey(definition), definition, ControlNode, 7, null)).ToArray();
    }

    private static string ControlKey(string definition) => $"schedule:{definition}:{ControlNode}";

    private static string ControlId(string scheduleKey) =>
        CompositeId(ControlTenant, "kernel-schedule-control-v1", JsonSerializer.Serialize(ControlTenant), scheduleKey);
}
