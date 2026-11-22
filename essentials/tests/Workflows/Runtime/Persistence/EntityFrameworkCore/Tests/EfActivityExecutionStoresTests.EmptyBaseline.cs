using System.Data.Common;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// The EF activity stores behind the coalescing overlay, for a run that has no durable record. The stores are probed
// through the overlay, which is the only caller the probe has in the runtime, and the cost is counted in commands on
// the wire. One test calls the probe directly, because it is a public contract.
public sealed partial class EfActivityExecutionStoresTests
{
    [Fact]
    public async Task A_run_with_no_rows_costs_one_probe_per_store_however_many_new_rows_are_read()
    {
        await using var fixture = await EmptyBaselineFixture.CreateAsync("tenant-a");
        // Neither another run's rows nor another scope's rows of this run are this run's rows.
        await fixture.State.SaveAsync(State("other", "a", 1));
        await fixture.Inspection.SaveAsync(Projection("other", "a", 1, "a"));
        await using (var otherScope = await fixture.ReopenAsync("tenant-b"))
        {
            await otherScope.State.SaveAsync(State("wf", "a", 1));
            await otherScope.Inspection.SaveAsync(Projection("wf", "a", 1, "a"));
        }

        await using var scoped = await fixture.ReopenAsync("tenant-a");
        var overlay = scoped.Overlay("wf");
        scoped.Commands.Clear();

        foreach (var id in new[] { "a", "b", "c" })
        {
            Assert.Null(await overlay.Activities.FindAsync("wf", id));
            Assert.Null(await overlay.Inspections.FindAsync("wf", id));
        }

        Assert.Equal(2, scoped.Commands.Count);
        Assert.All(scoped.Commands, command => Assert.Contains("EXISTS", command, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_run_with_a_row_in_each_store_is_probed_once_and_has_every_row_read()
    {
        await using var fixture = await EmptyBaselineFixture.CreateAsync("tenant-a");
        await fixture.State.SaveAsync(State("wf", "stray", 1));
        await fixture.Inspection.SaveAsync(Projection("wf", "stray", 1, "stray"));
        var overlay = fixture.Overlay("wf");
        fixture.Commands.Clear();

        Assert.Null(await overlay.Activities.FindAsync("wf", "a"));
        Assert.NotNull(await overlay.Activities.FindAsync("wf", "stray"));
        Assert.Null(await overlay.Inspections.FindAsync("wf", "a"));
        Assert.NotNull(await overlay.Inspections.FindAsync("wf", "stray"));

        // One probe and two reads per store.
        Assert.Equal(6, fixture.Commands.Count);
    }

    // The overlay answers these reads without a query. Each is still refused as the store's own read refuses it:
    // an id the schema cannot hold, a blank id, a cancelled read, and an access context with no ordinary scope.
    [Fact]
    public async Task A_read_answered_without_a_query_is_refused_as_the_stores_own_read_is()
    {
        await using var fixture = await EmptyBaselineFixture.CreateAsync("tenant-a");
        var overlay = fixture.Overlay("wf");
        fixture.Commands.Clear();
        foreach (var id in new[] { "a", "b" })
        {
            Assert.Null(await overlay.Activities.FindAsync("wf", id));
            Assert.Null(await overlay.Inspections.FindAsync("wf", id));
        }

        // Two probes and no read: from here on the overlay answers.
        Assert.Equal(2, fixture.Commands.Count);
        fixture.Commands.Clear();
        var tooLong = new string('a', RuntimeActivityExecutionEfModule.IdentityMaximumLength + 1);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Each refusal is checked on the overlay and then on the store's own read, which must refuse the same way.
        foreach (var activities in new IActivityExecutionStateStore[] { overlay.Activities, fixture.State })
        {
            await Assert.ThrowsAsync<ArgumentException>(async () => await activities.FindAsync("wf", tooLong));
            await Assert.ThrowsAsync<ArgumentException>(async () => await activities.FindAsync("wf", " "));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await activities.FindAsync("wf", "b", cancelled.Token));
        }

        foreach (var inspections in new IActivityExecutionInspectionStore[] { overlay.Inspections, fixture.Inspection })
        {
            await Assert.ThrowsAsync<ArgumentException>(async () => await inspections.FindAsync("wf", tooLong));
            await Assert.ThrowsAsync<ArgumentException>(async () => await inspections.FindAsync("wf", " "));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await inspections.FindAsync("wf", "b", cancelled.Token));
        }

        fixture.Access.Current = PersistenceAccessContext.Global;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await overlay.Activities.FindAsync("wf", "b"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await overlay.Inspections.FindAsync("wf", "b"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.State.FindAsync("wf", "b"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Inspection.FindAsync("wf", "b"));

        // None of it reached the database.
        Assert.Empty(fixture.Commands);
    }

    // The probe is a public contract, so it is also called directly here. It refuses a bad run id, a cancelled
    // token and a context with no scope before it queries, as the stores' other reads do.
    [Fact]
    public async Task The_probe_refuses_a_bad_run_id_a_cancelled_token_and_a_context_with_no_scope()
    {
        await using var fixture = await EmptyBaselineFixture.CreateAsync("tenant-a");
        fixture.Commands.Clear();
        var tooLong = new string('w', RuntimeActivityExecutionEfModule.IdentityMaximumLength + 1);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        foreach (var probe in new IRunRowPresenceProbe[] { fixture.State, fixture.Inspection })
        {
            await Assert.ThrowsAsync<ArgumentException>(async () => await probe.HasAnyAsync(" "));
            await Assert.ThrowsAsync<ArgumentException>(async () => await probe.HasAnyAsync(tooLong));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await probe.HasAnyAsync("wf", cancelled.Token));
        }

        fixture.Access.Current = PersistenceAccessContext.Global;
        foreach (var probe in new IRunRowPresenceProbe[] { fixture.State, fixture.Inspection })
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await probe.HasAnyAsync("wf"));

        Assert.Empty(fixture.Commands);
    }

    // What the probe found under one scope says nothing about another scope's rows of the same run id.
    [Fact]
    public async Task A_read_under_another_scope_than_the_probe_is_read_from_the_store()
    {
        await using var fixture = await EmptyBaselineFixture.CreateAsync("tenant-a");
        await using (var otherScope = await fixture.ReopenAsync("tenant-b"))
        {
            await otherScope.State.SaveAsync(State("wf", "a", 1));
            await otherScope.Inspection.SaveAsync(Projection("wf", "a", 1, "a"));
        }

        await using var scoped = await fixture.ReopenAsync("tenant-a");
        var overlay = scoped.Overlay("wf");
        scoped.Commands.Clear();
        foreach (var id in new[] { "a", "b" })
        {
            Assert.Null(await overlay.Activities.FindAsync("wf", id));
            Assert.Null(await overlay.Inspections.FindAsync("wf", id));
        }

        // Two probes and no read: under tenant-a the overlay answers.
        Assert.Equal(2, scoped.Commands.Count);
        scoped.Commands.Clear();

        scoped.Access.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        Assert.NotNull(await overlay.Activities.FindAsync("wf", "a"));
        Assert.NotNull(await overlay.Inspections.FindAsync("wf", "a"));

        Assert.Equal(2, scoped.Commands.Count);
    }

    private sealed record EmptyBaselineOverlay(
        CoalescingActivityExecutionStateStore Activities,
        CoalescingActivityExecutionInspectionStore Inspections);

    private sealed class EmptyBaselineFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly RuntimeDbContext context;
        private readonly bool ownsConnection;

        private EmptyBaselineFixture(SqliteConnection connection, RuntimeDbContext context, string scope, List<string> commands, bool ownsConnection)
        {
            this.connection = connection;
            this.context = context;
            this.ownsConnection = ownsConnection;
            Commands = commands;
            var accessor = Access = new SwitchableAccessor(scope);
            var codec = new HmacRuntimeRecoveryContinuationCodec(Microsoft.Extensions.Options.Options.Create(
                new RuntimeRecoveryContinuationOptions { SigningKey = "ef-empty-baseline-test-signing-key-32" }));
            State = new EfActivityExecutionStateStore(context, accessor, codec);
            Inspection = new EfActivityExecutionInspectionStore(context, accessor, codec);
        }

        public List<string> Commands { get; }
        public SwitchableAccessor Access { get; }
        public EfActivityExecutionStateStore State { get; }
        public EfActivityExecutionInspectionStore Inspection { get; }

        public static async Task<EmptyBaselineFixture> CreateAsync(string scope)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = Open(connection, scope, ownsConnection: true);
            await fixture.context.Database.EnsureCreatedAsync();
            return fixture;
        }

        public Task<EmptyBaselineFixture> ReopenAsync(string scope) => Task.FromResult(Open(connection, scope, ownsConnection: false));

        // A drain of a run that has no durable record, begun through the drain scope factory as the orchestrator
        // begins it. Its session is the ambient one for both overlay stores.
        public EmptyBaselineOverlay Overlay(string workflowExecutionId)
        {
            var sessions = new LastPushedSessionAccessor();
            var commitStore = new InMemoryRuntimeCheckpointCommitStore();
            var coalescingCommitStore = new CoalescingRuntimeCheckpointCommitStore(new CoalescingInner<IRuntimeCheckpointCommitStore>(commitStore), sessions);
            var factory = new RuntimeCoalescingDrainScopeFactory(
                sessions,
                new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), coalescingCommitStore, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []),
                new CoalescingInner<IWorkflowSchedulerWorkQueue>(new InMemoryWorkflowSchedulerWorkQueue()),
                new CoalescingInner<IRuntimePostCommitOutboxStore>(commitStore),
                new PersistenceModeOptions(),
                TimeProvider.System);
            _ = factory.Begin(workflowExecutionId, new ResolvedPersistenceMode(PersistenceMode.Balanced, RunHasRecord: false));
            return new EmptyBaselineOverlay(
                new CoalescingActivityExecutionStateStore(new CoalescingInner<IActivityExecutionStateStore>(State), sessions),
                new CoalescingActivityExecutionInspectionStore(new CoalescingInner<IActivityExecutionInspectionStore>(Inspection), sessions));
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            if (ownsConnection)
                await connection.DisposeAsync();
        }

        private static EmptyBaselineFixture Open(SqliteConnection connection, string scope, bool ownsConnection)
        {
            var commands = new List<string>();
            var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new CommandRecorder(commands))
                .Options);
            return new EmptyBaselineFixture(connection, context, scope, commands, ownsConnection);
        }
    }

    private sealed class CommandRecorder(List<string> commands) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            commands.Add(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class SwitchableAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class LastPushedSessionAccessor : IRuntimeCoalescingSessionAccessor
    {
        public RuntimeCoalescingSession? Current { get; private set; }

        public IDisposable Push(RuntimeCoalescingSession? pushedSession)
        {
            Current = pushedSession;
            return new Kept();
        }

        private sealed class Kept : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
