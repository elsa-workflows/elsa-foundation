using System.Data.Common;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// The EF activity stores behind the coalescing overlay, for a run that has no durable record. The stores are probed
// through the overlay, which is the only caller the probe has, and the cost is counted in commands on the wire.
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
            var accessor = new Accessor(scope);
            var codec = new HmacRuntimeRecoveryContinuationCodec(Microsoft.Extensions.Options.Options.Create(
                new RuntimeRecoveryContinuationOptions { SigningKey = "ef-empty-baseline-test-signing-key-32" }));
            State = new EfActivityExecutionStateStore(context, accessor, codec);
            Inspection = new EfActivityExecutionInspectionStore(context, accessor, codec);
        }

        public List<string> Commands { get; }
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

        // A drain of a run that has no durable record: the session is the ambient one for both overlay stores.
        public EmptyBaselineOverlay Overlay(string workflowExecutionId)
        {
            var session = new RuntimeCoalescingSession(
                workflowExecutionId,
                new InMemoryWorkflowSchedulerWorkQueue(),
                new PersistenceModeOptions(),
                innerOutboxStore: null,
                new ResolvedPersistenceMode(PersistenceMode.Balanced, RunHasRecord: false));
            var sessions = new FixedSessionAccessor(session);
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

    private sealed class FixedSessionAccessor(RuntimeCoalescingSession session) : IRuntimeCoalescingSessionAccessor
    {
        public RuntimeCoalescingSession? Current => session;

        public IDisposable Push(RuntimeCoalescingSession? pushedSession) =>
            throw new NotSupportedException("This test provides a fixed ambient session.");
    }
}
