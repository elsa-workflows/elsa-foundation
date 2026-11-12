using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    [Theory]
    [InlineData("executions")]
    [InlineData("scheduler")]
    [InlineData("checkpoints")]
    [InlineData("outbox")]
    [InlineData("background")]
    [InlineData("timers")]
    public async Task Test_entry_boundary_refuses_a_privileged_selected_store_even_in_the_same_context_and_scope(string role)
    {
        await using var fixture = await TestGateFixture.CreateAsync();
        await fixture.FinalizeAsync();
        await using var context = fixture.Open();
        var selected = TestProviderTuple(context);
        var boundary = new EfRuntimeTestPersistenceBoundary(context, new FixedAccessor("tenant-a"));
        Assert.True(boundary.CanPersistTestEntry(selected));
        var privileged = new PrivilegedTestAccessor();
        var codec = new TestFormatCodec();
        selected = role switch
        {
            "executions" => selected with { Executions = new EfWorkflowExecutionStateStore(context, privileged, codec) },
            "scheduler" => selected with { Scheduler = new EfSchedulerWorkQueueStore(context, privileged, codec) },
            "checkpoints" => selected with { Checkpoints = new EfRuntimeCheckpointCommitStore(context, privileged) },
            "outbox" => selected with { Outbox = new EfRuntimePostCommitOutboxStore(context, privileged) },
            "background" => selected with { Background = new EfBackgroundActivityWorkStore(context, privileged) },
            _ => selected with { Timers = new EfDurableTimerStore(context, privileged, codec) }
        };
        Assert.False(boundary.CanPersistTestEntry(selected));
    }

    [Fact]
    public async Task Test_entry_admitted_gate_avoids_an_owning_header_query_for_ordinary_operational_writes()
    {
        await using var fixture = await TestGateFixture.CreateAsync();
        await fixture.FinalizeAsync();
        var queries = new HeaderQueryCounter();
        await using var context = fixture.Open(queries);
        var timers = new EfDurableTimerStore(context, new FixedAccessor("tenant-a"), new TestFormatCodec());
        var now = DateTimeOffset.UtcNow;
        await timers.SaveAsync(new("timer", "ordinary", "Delay", "wait", now.AddMinutes(1), now));
        Assert.Equal(0, queries.Count);
        Assert.Equal(RuntimeOperationalStateEfModule.Chain.CurrentVersion,
            (await context.DurableTimers.AsNoTracking().SingleAsync()).SchemaVersion);
        // Ensure the counter observes the exact header table when a real lookup occurs.
        await context.WorkflowExecutionStates.AsNoTracking().ToArrayAsync();
        Assert.Equal(1, queries.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Test_entry_predecessor_gate_refuses_bulk_claim_consumption_without_deleting_work(bool checkpoint)
    {
        await using var fixture = await TestGateFixture.CreateAsync();
        RuntimeSchedulerWorkClaim claim;
        await using (var peer = fixture.Open(ungated: true))
        {
            var queue = new EfSchedulerWorkQueueStore(peer, new FixedAccessor("tenant-a"), new TestFormatCodec());
            var now = DateTimeOffset.UtcNow;
            await queue.EnqueueAsync(new("work", "test-workflow", "command", WorkflowExecutionCommandKind.Start, "envelope", "key", now, now,
                payload: JsonSerializer.SerializeToElement(new { TestEntry = TestExecution(now).TestEntry })));
            claim = (await queue.ClaimAsync(new("test-workflow", "owner", now, TimeSpan.FromMinutes(1))))!;
        }
        await using var context = fixture.Open();
        var consumed = ConsumedSchedulerWorkItem.FromClaim(claim);
        if (checkpoint)
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() =>
                EfRuntimeCheckpointParticipantStaging.StageConsumedSchedulerWorkAsync(context, consumed, "tenant-a", CancellationToken.None).AsTask());
            await transaction.RollbackAsync();
        }
        else
        {
            var queue = new EfSchedulerWorkQueueStore(context, new FixedAccessor("tenant-a"), new TestFormatCodec());
            await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => queue.ConsumeClaimedAsync(consumed).AsTask());
        }
        var saved = await context.SchedulerWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeSchedulerWorkEfModule.TestEntrySchemaVersion, saved.SchemaVersion);
        Assert.Equal(claim.FencingToken, saved.ClaimToken);
    }

    private sealed class HeaderQueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(RuntimeWorkflowExecutionEfModule.TableName, StringComparison.Ordinal)) Count++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PrivilegedTestAccessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.PrivilegedScoped(new("tenant-a"), new("owned test"));
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
        public RuntimeSqliteDbContext Open(DbCommandInterceptor? interceptor = null, bool ungated = false)
        {
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection);
            if (!ungated) options.UseApplicationServiceProvider(services);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            if (!ungated) EfSchemaWriteGateInterceptor.EnsureAdded(options);
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
