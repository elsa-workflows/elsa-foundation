using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfSchedulerWorkQueueStoreTests
{
    [Theory]
    [InlineData(RuntimeWorkflowExecutionEfModule.SchemaFamily)]
    [InlineData(RuntimeSchedulerWorkEfModule.SchemaFamily)]
    [InlineData(RuntimeOperationalStateEfModule.SchemaFamily)]
    [InlineData(RuntimePostCommitOutboxEfModule.SchemaFamily)]
    [InlineData(RuntimeBackgroundActivityWorkEfModule.SchemaFamily)]
    public async Task Test_entry_boundary_requires_every_actual_admitted_format_and_exact_physical_tuple(string heldFamily)
    {
        await using var fixture = await TestGateFixture.CreateAsync();
        await fixture.FinalizeAsync(except: heldFamily);
        await using var context = fixture.Open();
        var selected = TestProviderTuple(context);
        var boundary = new EfRuntimeTestPersistenceBoundary(context, new FixedAccessor("tenant-a"));
        Assert.False(boundary.CanPersistTestEntry(selected));
        await fixture.FinalizeAsync();
        Assert.True(boundary.CanPersistTestEntry(selected));
        Assert.False(boundary.CanPersistTestEntry(selected with { Scheduler = new InMemoryWorkflowSchedulerWorkQueue() }));
        await using var otherContext = fixture.Open();
        Assert.False(boundary.CanPersistTestEntry(selected with { Timers = new EfDurableTimerStore(otherContext, new FixedAccessor("tenant-a"), new TestFormatCodec()) }));
    }

    [Fact]
    public async Task Test_entry_predecessor_gate_refuses_before_enqueue_while_ordinary_commands_remain_usable()
    {
        await using var fixture = await TestGateFixture.CreateAsync();
        await using var context = fixture.Open();
        var now = DateTimeOffset.UtcNow;
        var queue = new EfSchedulerWorkQueueStore(context, new FixedAccessor("tenant-a"), new TestFormatCodec());
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => queue.EnqueueAsync(new("test", "test-workflow", "test-command",
            WorkflowExecutionCommandKind.Start, "test-envelope", "test-key", now, now,
            payload: JsonSerializer.SerializeToElement(new { TestEntry = TestExecution(now).TestEntry }))).AsTask());
        Assert.Empty(await context.SchedulerWorkItems.AsNoTracking().ToArrayAsync());
        await queue.EnqueueAsync(new("ordinary", "ordinary-workflow", "ordinary-command", WorkflowExecutionCommandKind.Start,
            "ordinary-envelope", "ordinary-key", now, now));
        Assert.Equal("1.0.0", (await context.SchedulerWorkItems.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.NotNull(await queue.ClaimAsync(new("ordinary-workflow", "ordinary-worker", now, TimeSpan.FromMinutes(1))));
    }

    private static RuntimeTestPersistenceBoundaryContext TestProviderTuple(RuntimeDbContext context)
    {
        var access = new FixedAccessor("tenant-a");
        var codec = new TestFormatCodec();
        return new(TestExecution(DateTimeOffset.UtcNow).TestEntry!, new EfWorkflowExecutionStateStore(context, access, codec),
            new EfSchedulerWorkQueueStore(context, access, codec), new EfRuntimeCheckpointCommitStore(context, access),
            new EfRuntimePostCommitOutboxStore(context, access), new EfBackgroundActivityWorkStore(context, access),
            new EfDurableTimerStore(context, access, codec));
    }

    private sealed class TestFormatCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }

    private sealed class TestGateFixture : IAsyncDisposable
    {
        private static readonly string[] ProtectedFamilies = [RuntimeWorkflowExecutionEfModule.SchemaFamily,
            RuntimeSchedulerWorkEfModule.SchemaFamily, RuntimeOperationalStateEfModule.SchemaFamily,
            RuntimePostCommitOutboxEfModule.SchemaFamily, RuntimeBackgroundActivityWorkEfModule.SchemaFamily];
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly EfSchemaFinalizationGates gates = new();
        private readonly ServiceProvider services;
        private readonly EfSchemaModuleGate gate;
        private TestGateFixture()
        {
            services = new ServiceCollection().AddSingleton(gates).BuildServiceProvider();
            gate = new(EfSchemaModuleFamilies.ForContext(typeof(RuntimeDbContext))!, new TestCurrentFleet(), new EfSchemaFinalizationObservations(), new());
        }
        public static async Task<TestGateFixture> CreateAsync()
        {
            var fixture = new TestGateFixture();
            await fixture.connection.OpenAsync();
            await using var context = fixture.Open();
            await context.Database.EnsureCreatedAsync();
            var finalization = new EfSchemaFinalizationStore(context);
            foreach (var family in ProtectedFamilies)
            {
                var chain = fixture.gate.Families.Chains.Single(x => x.Family == family);
                var record = await finalization.GetOrCreateAsync(family, chain.ReadableVersions[0], chain.ReadableVersions, SchemaFinalizationActor.OfOperator("test"));
                await finalization.PlaceHoldAsync(family, record.Revision, null, "predecessor rollout", "test", chain.ReadableVersions);
            }
            await fixture.gate.ActivateAsync(context);
            fixture.gates.Register(typeof(RuntimeDbContext), fixture.gate);
            return fixture;
        }
        public RuntimeSqliteDbContext Open()
        {
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).UseApplicationServiceProvider(services);
            EfSchemaWriteGateInterceptor.EnsureAdded(options);
            return new(options.Options);
        }
        public async Task FinalizeAsync(string? except = null)
        {
            await using var context = Open();
            var finalization = new EfSchemaFinalizationStore(context);
            foreach (var family in ProtectedFamilies.Where(x => x != except))
            {
                var record = (await finalization.FindAsync(family))!;
                if (record.Holds.Count != 0) await finalization.ReleaseHoldAsync(family, record.Revision, null, "test");
            }
            for (var attempt = 0; attempt < 8; attempt++)
            {
                await gate.EvaluateAsync(context);
                await gate.RefreshAsync(context);
                if (ProtectedFamilies.Where(x => x != except).All(family =>
                    gate.StateOf(family)!.WriteVersion == gate.Families.Chains.Single(x => x.Family == family).CurrentVersion)) return;
            }
            Assert.Fail("The isolated current-member gate did not admit its declared current formats.");
        }
        public async ValueTask DisposeAsync() { await services.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class TestCurrentFleet : IEfSchemaFleet
    {
        public EfSchemaFleetStanding GetLocalStanding() => new(new("test-host", "test-generation"), false);
        public ValueTask PublishAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public ValueTask<EfSchemaFleetAnswer> CountObservingAsync(string family, IReadOnlyList<string> versions, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public TimeSpan SettleMargin => TimeSpan.Zero;
        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
}
