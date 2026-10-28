using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeModelCheckpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowExecutableIdentity Pin = new("model-artifact", "process", "process-v1", "1.0.0", "model-hash");
    private static readonly RuntimeExecutionProtocol Protocol = new("test.model", 1);
    private static readonly RuntimeExecutionLease Lease = new("lease", "execution", "owner", Now, Now.AddMinutes(10), 1);
    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);

    [Fact]
    public async Task Decisions_survive_context_restart_with_exact_receipts_and_no_native_header()
    {
        await using var database = await Database.CreateAsync();
        var first = Commit(1);
        await using (var context = database.Open())
        {
            var store = Store(context);
            await store.CommitAsync(first, Immediate);
            await store.CommitAsync(Commit(2), Immediate);
            await store.CommitAsync(first, Immediate); // Replay after state advanced must not overwrite it.
            await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() =>
                store.CommitAsync(first with { ModelState = Change(1, payload: "{\"tokens\":[\"conflicting\"]}") }, Immediate).AsTask());
        }
        await using (var restarted = database.Open())
        {
            var reader = Reader(restarted);
            var state = Assert.IsType<RuntimeModelExecutionState>(await reader.FindAsync("execution"));
            Assert.Equal(2, state.Revision);
            Assert.Equal("human-wait", state.Payload.GetProperty("tokens")[0].GetString());
            Assert.Equal(Pin, state.Executable);
            Assert.Equal("incarnation", state.MaterialIncarnationId);
            Assert.Equal(1, (await reader.FindInputAsync("execution", "input-1"))!.AcceptedRevision);
            Assert.Equal(2, (await reader.FindInputAsync("execution", "input-2"))!.AcceptedRevision);
            Assert.Empty(await restarted.WorkflowExecutionStates.ToArrayAsync());
            Assert.Empty(await restarted.ActivityExecutionStates.ToArrayAsync());
            Assert.Equal(2, await restarted.RuntimeCheckpointCommits.CountAsync());
            await Store(restarted).CommitAsync(Commit(3, lifecycle: RuntimeModelLifecycle.TerminalSettled), Immediate);
        }
        await using var completed = database.Open();
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, (await Reader(completed).FindAsync("execution"))!.Lifecycle);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(completed).CommitAsync(Commit(4), Immediate).AsTask());
        Assert.Null(await Reader(completed).FindInputAsync("execution", "input-4"));
    }

    [Fact]
    public async Task Failure_at_the_final_marker_rolls_back_state_receipt_and_fence_touch()
    {
        await using var database = await Database.CreateAsync();
        long fenceRevision;
        await using (var before = database.Open()) fenceRevision = (await before.ExecutionLivenessStates.SingleAsync()).Revision;
        await using (var context = database.Open(new FailMarker()))
        {
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => Store(context).CommitAsync(Commit(1), Immediate).AsTask());
            Assert.Equal("Injected final-marker failure.", failure.InnerException!.Message);
        }
        await using var restarted = database.Open();
        Assert.Empty(await restarted.Set<RuntimeModelExecutionEntity>().ToArrayAsync());
        Assert.Empty(await restarted.Set<RuntimeModelInputEntity>().ToArrayAsync());
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal(fenceRevision, (await restarted.ExecutionLivenessStates.SingleAsync()).Revision);
        await Store(restarted).CommitAsync(Commit(1), Immediate);
        Assert.NotNull(await Reader(restarted).FindInputAsync("execution", "input-1"));
    }

    [Fact]
    public async Task Stale_fence_revision_and_reused_input_refuse_without_partial_decisions()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Open();
        var store = Store(context);
        await store.CommitAsync(Commit(1), Immediate);
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.CommitAsync(Commit(2) with
            { ExpectedFence = new("obsolete", "old-owner", 1) }, Immediate).AsTask());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.CommitAsync(Commit(3), Immediate).AsTask());
        var duplicate = Commit(2) with { ModelState = Change(2, inputId: "input-1") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(duplicate, Immediate).AsTask());
        Assert.Equal(1, (await Reader(context).FindAsync("execution"))!.Revision);
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Null(await Reader(context).FindInputAsync("execution", "input-2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Model_state_input_consumed_claim_outbox_and_marker_share_one_transaction(bool failMarker)
    {
        await using var database = await Database.CreateAsync();
        await using (var context = failMarker ? database.Open(new FailMarker()) : database.Open())
        {
            var queue = new EfSchedulerWorkQueueStore(context, new Access("tenant-a"), new Codec());
            await queue.EnqueueAsync(new("model-input", "execution", "command", WorkflowExecutionCommandKind.ScheduleActivity,
                "envelope", "enqueue-input", Now, Now, 1));
            var claim = (await queue.ClaimAsync(new("execution", "queue-owner", Now, TimeSpan.FromMinutes(5))))!;
            var commit = Commit(1);
            var intent = new RuntimePostCommitIntent("next", "execution", "test.intent", Now.AddSeconds(1), null, null, null);
            var outboxId = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId);
            var item = new RuntimePostCommitOutboxItem(outboxId, intent, RuntimePostCommitOutboxStatus.Pending, Now.AddSeconds(1), Now.AddSeconds(1));
            commit = commit with
            {
                PostCommitIntents = [intent],
                StateChanges = commit.StateChanges.WithConsumedSchedulerWorkItems([ConsumedSchedulerWorkItem.FromClaim(claim)])
                    .WithPostCommitOutbox([new(outboxId, RuntimeStateChangeOperation.Upsert, item, new Dictionary<string, string>())])
            };
            if (failMarker)
                await Assert.ThrowsAsync<DbUpdateException>(() => Store(context).CommitAsync(commit, Immediate).AsTask());
            else
            {
                var accepted = await Store(context).CommitAsync(commit, Immediate);
                Assert.Equal(["model-input"], accepted.ConsumedSchedulerWorkItemIds);
                Assert.Equal([outboxId], accepted.PendingPostCommitWorkIds);
            }
        }
        await using var reopened = database.Open();
        Assert.Equal(failMarker ? 1 : 0, await reopened.SchedulerWorkItems.CountAsync());
        var expected = failMarker ? 0 : 1;
        Assert.Equal(expected, await reopened.Set<RuntimeModelExecutionEntity>().CountAsync());
        Assert.Equal(expected, await reopened.Set<RuntimeModelInputEntity>().CountAsync());
        Assert.Equal(expected, await reopened.RuntimePostCommitOutbox.CountAsync());
        Assert.Equal(expected, await reopened.RuntimeCheckpointCommits.CountAsync());
    }

    [Theory]
    [InlineData("incarnation")]
    [InlineData("authority")]
    [InlineData("pin")]
    [InlineData("protocol")]
    public async Task An_update_cannot_replace_the_execution_pin_or_authority(string field)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Open();
        await Store(context).CommitAsync(Commit(1), Immediate);
        var change = Change(2, incarnation: field == "incarnation" ? "replacement" : null,
            authority: field == "authority" ? new("foreign", "foreign") : null,
            pin: field == "pin" ? Pin with { DefinitionVersionId = "different-version" } : null,
            protocol: field == "protocol" ? new("different.model", 1) : null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(context).CommitAsync(Commit(2) with { ModelState = change }, Immediate).AsTask());
        Assert.Equal(1, (await Reader(context).FindAsync("execution"))!.Revision);
        Assert.Single(await context.Set<RuntimeModelInputEntity>().ToArrayAsync());
    }

    [Theory]
    [InlineData(RuntimeCheckpointPersistenceMode.Deferred)]
    [InlineData(RuntimeCheckpointPersistenceMode.Skip)]
    public async Task A_model_decision_cannot_be_buffered_or_skipped(RuntimeCheckpointPersistenceMode mode)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Open();
        await Assert.ThrowsAsync<NotSupportedException>(() => Store(context).CommitAsync(Commit(1), new(mode)).AsTask());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Material_incarnation_and_all_five_pin_fields_are_verified_before_acceptance()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Open();
        await Assert.ThrowsAsync<InvalidDataException>(() => Store(context).CommitAsync(Commit(1) with
            { ModelState = Change(1, incarnation: "replacement") }, Immediate).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => Store(context).CommitAsync(Commit(1) with
            { ModelState = Change(1, pin: Pin with { DefinitionVersionId = "other" }) }, Immediate).AsTask());
        Assert.Empty(await context.Set<RuntimeModelExecutionEntity>().ToArrayAsync());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Scoped_reader_pages_and_rejects_corrupt_projection_or_foreign_continuation()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Open();
        await Store(context).CommitAsync(Commit(1), Immediate);
        var reader = Reader(context);
        Assert.Single((await reader.ListPageAsync(new(1))).Items);
        Assert.Null(await Reader(context, "foreign").FindAsync("execution"));
        var forged = new Codec().Encode("runtime.model-executions.v1", JsonSerializer.SerializeToUtf8Bytes(new
            { Version = 1, ScopeHash = Hash("foreign"), ExecutionId = "execution", After = Order("execution") }));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ListPageAsync(new(1, forged)).AsTask());
        await context.Set<RuntimeModelExecutionEntity>().ExecuteUpdateAsync(x => x.SetProperty(row => row.Lifecycle, 999));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.FindAsync("execution").AsTask());
    }

    [Fact]
    public async Task Capability_qualification_requires_the_actual_scoped_reader_context_and_access()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Open();
        await using var other = database.Open();
        var access = new Access("tenant-a");
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, access, new Clock(), new LeaseManager());
        Assert.True(writer.SupportsModelCheckpoint);
        Assert.True(writer.CanCommitModelState(new EfRuntimeModelExecutionReader(context, access, new Codec())));
        Assert.False(writer.CanCommitModelState(new EfRuntimeModelExecutionReader(other, access, new Codec())));
        Assert.False(writer.CanCommitModelState(new EfRuntimeModelExecutionReader(context, new Access("tenant-a"), new Codec())));
    }

    private static RuntimeCheckpointCommit Commit(long revision, RuntimeModelLifecycle lifecycle = RuntimeModelLifecycle.Running) =>
        new($"commit-{revision}", new($"checkpoint-{revision}", "ModelDecision", "execution", Now.AddSeconds(revision), [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
        { ExpectedFence = Lease.ToFence(), ModelState = Change(revision, lifecycle: lifecycle) };

    private static RuntimeModelStateChange Change(long revision, string? inputId = null, string? payload = null,
        string? incarnation = null, WorkflowExecutionAuthoritySnapshot? authority = null, WorkflowExecutableIdentity? pin = null,
        RuntimeExecutionProtocol? protocol = null, RuntimeModelLifecycle lifecycle = RuntimeModelLifecycle.Running)
    {
        using var data = JsonDocument.Parse(payload ?? "{\"tokens\":[\"human-wait\"]}");
        using var result = JsonDocument.Parse("{\"outcome\":\"completed\"}");
        var at = Now.AddSeconds(revision);
        var state = new RuntimeModelExecutionState(1, new("tenant-a"), "execution", "tenant-a", protocol ?? Protocol,
            pin ?? Pin, incarnation ?? "incarnation", revision, lifecycle, authority ?? new("system", "initiator"), Now, at,
            data.RootElement, lifecycle is RuntimeModelLifecycle.TerminalSettled or RuntimeModelLifecycle.TerminalPendingSettlement ? result.RootElement : null);
        return new(revision == 1 ? null : revision - 1, state,
            new(new("tenant-a"), "execution", inputId ?? $"input-{revision}", "test.input", $"content-{revision}", revision, at, null, null, result.RootElement));
    }

    private static EfRuntimeCheckpointCommitStore Store(RuntimeDbContext context) => new(context, new Access("tenant-a"), new Clock(), new LeaseManager());
    private static EfRuntimeModelExecutionReader Reader(RuntimeDbContext context, string scope = "tenant-a") => new(context, new Access(scope), new Codec());
    private sealed class Access(string scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new(scope)); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class LeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) => write(cancellationToken);
    }
    private sealed class Codec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => purpose + ":" + Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => token.StartsWith(purpose + ":", StringComparison.Ordinal)
            ? Convert.FromBase64String(token[(purpose.Length + 1)..]) : throw new FormatException();
    }
    private sealed class FailMarker : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"elsa_runtime_checkpoint_commit\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Injected final-marker failure.");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Database(SqliteConnection connection) : IAsyncDisposable
    {
        public static async Task<Database> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var database = new Database(connection);
            await using var context = database.Open(); await context.Database.EnsureCreatedAsync();
            await new EfExecutionLivenessStateStore(context, new Access("tenant-a"), new Codec())
                .SaveAsync(new("ownership:execution", "execution", Lease, null, null, null));
            using var definition = JsonDocument.Parse("{\"start\":\"human\"}");
            var material = new RuntimeModelMaterial(1, Pin, Protocol, "test/1", definition.RootElement, new(1, []), [], [], []);
            const string scope = "tenant-a";
            context.WorkflowExecutables.Add(new()
            {
                Id = Hash($"{scope.Length}:{scope}{Pin.ArtifactId.Length}:{Pin.ArtifactId}"), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
                ArtifactId = Encode(Pin.ArtifactId), ArtifactIdHash = Hash(Pin.ArtifactId), ArtifactIdOrderKey = Order(Pin.ArtifactId),
                ArtifactHash = Encode(Pin.ArtifactHash), ContentJson = RuntimeArtifactJson.Serialize(material), SchemaVersion = RuntimeArtifactEfModule.SchemaVersion,
                IncarnationId = "incarnation"
            });
            await context.SaveChangesAsync(); context.ChangeTracker.Clear();
            return database;
        }
        public RuntimeSqliteDbContext Open(params IInterceptor[] interceptors)
        {
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection);
            if (interceptors.Length > 0) options.AddInterceptors(interceptors);
            return new(options.Options);
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
