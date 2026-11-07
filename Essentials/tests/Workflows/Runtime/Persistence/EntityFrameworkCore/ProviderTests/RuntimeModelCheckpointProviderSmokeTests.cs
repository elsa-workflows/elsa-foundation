using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeModelCheckpointPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    private static RuntimeDbContext Open(string connection, IInterceptor[] interceptors) => new RuntimePostgreSqlDbContext(
        new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).AddInterceptors(interceptors).Options);

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PostgreSql_model_checkpoint_participants_are_atomic(bool failMarker) => RuntimeModelCheckpointProviderSmoke.AtomicAsync(fixture, Open, failMarker);

    [SkippableFact]
    public Task PostgreSql_model_checkpoint_fences_revisions_and_replay() => RuntimeModelCheckpointProviderSmoke.ConflictAsync(fixture, Open);

    [SkippableFact]
    public Task PostgreSql_terminal_cleanup_keeps_result_authority_and_current_fence() => RuntimeModelCheckpointProviderSmoke.SettlementAsync(fixture, Open);

    [SkippableTheory]
    [InlineData("before-write")]
    [InlineData("after-write")]
    [InlineData("ack-cancel")]
    [InlineData("ack-unreadable")]
    public Task PostgreSql_native_admission_preserves_committed_outcomes(string scenario) =>
        RuntimeModelCheckpointProviderSmoke.NativeAdmissionAsync(fixture, Open, scenario);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeModelCheckpointSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    private static RuntimeDbContext Open(string connection, IInterceptor[] interceptors) => new RuntimeSqlServerDbContext(
        new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).AddInterceptors(interceptors).Options);

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SqlServer_model_checkpoint_participants_are_atomic(bool failMarker) => RuntimeModelCheckpointProviderSmoke.AtomicAsync(fixture, Open, failMarker);

    [SkippableFact]
    public Task SqlServer_model_checkpoint_fences_revisions_and_replay() => RuntimeModelCheckpointProviderSmoke.ConflictAsync(fixture, Open);

    [SkippableFact]
    public Task SqlServer_terminal_cleanup_keeps_result_authority_and_current_fence() => RuntimeModelCheckpointProviderSmoke.SettlementAsync(fixture, Open);

    [SkippableTheory]
    [InlineData("before-write")]
    [InlineData("after-write")]
    [InlineData("ack-cancel")]
    [InlineData("ack-unreadable")]
    public Task SqlServer_native_admission_preserves_committed_outcomes(string scenario) =>
        RuntimeModelCheckpointProviderSmoke.NativeAdmissionAsync(fixture, Open, scenario);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeModelCheckpointMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    private static RuntimeDbContext Open(string connection, IInterceptor[] interceptors) => new RuntimeMySqlDbContext(
        new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).AddInterceptors(interceptors).Options);

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MySql_model_checkpoint_participants_are_atomic(bool failMarker) => RuntimeModelCheckpointProviderSmoke.AtomicAsync(fixture, Open, failMarker);

    [SkippableFact]
    public Task MySql_model_checkpoint_fences_revisions_and_replay() => RuntimeModelCheckpointProviderSmoke.ConflictAsync(fixture, Open);

    [SkippableFact]
    public Task MySql_terminal_cleanup_keeps_result_authority_and_current_fence() => RuntimeModelCheckpointProviderSmoke.SettlementAsync(fixture, Open);

    [SkippableTheory]
    [InlineData("before-write")]
    [InlineData("after-write")]
    [InlineData("ack-cancel")]
    [InlineData("ack-unreadable")]
    public Task MySql_native_admission_preserves_committed_outcomes(string scenario) =>
        RuntimeModelCheckpointProviderSmoke.NativeAdmissionAsync(fixture, Open, scenario);
}

internal static class RuntimeModelCheckpointProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);
    private static readonly RuntimeExecutionLease Lease = new("lease", "execution", "owner", Now, Now.AddMinutes(10), 1);
    private static readonly string[] Tables = [RuntimeModelStateEfModule.StateTable, RuntimeModelStateEfModule.InputTable,
        "elsa_runtime_post_commit_outbox", "elsa_runtime_scheduler_work_item", "elsa_runtime_checkpoint_commit"];

    public static async Task NativeAdmissionAsync(RuntimeBookmarksProviderFixture fixture,
        Func<string, IInterceptor[], RuntimeDbContext> open, string scenario)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var access = new Access(new("native-outcome-" + Guid.NewGuid().ToString("N")));
        var material = await PrepareAsync(fixture.ConnectionString, open, access);
        var nativeLease = new RuntimeExecutionLease("native-lease", "native-worker", "owner", Now, Now.AddMinutes(10), 1);
        RuntimeModelExecutionState expected;
        await using (var setup = open(fixture.ConnectionString, []))
        {
            await Store(setup, access).CommitAsync(Commit(access, material, 1), Immediate);
            expected = (await Reader(setup, access).FindAsync("execution"))!;
            await new EfExecutionLivenessStateStore(setup, access, new Codec()).SaveAsync(
                new("ownership:native-worker", "native-worker", nativeLease, null, null, null));
        }
        var at = Now.AddSeconds(2);
        var pin = new WorkflowExecutableIdentity("native-artifact", "worker", "worker-version", "1.0.0", "worker-hash");
        var workflow = new WorkflowExecutionState("native-worker", pin, WorkflowExecutionStatus.Running,
            null, at, at, at, null, null, null, null, new Dictionary<string, string>())
        {
            Partition = new(access.Current.RequireScope().Value), Authority = expected.Authority,
            ModelWorkOrigin = new(1, expected.Scope, expected.ExecutionId, expected.Protocol, expected.Executable,
                expected.MaterialIncarnationId, "binding", "invocation", "attempt", pin, "native-worker",
                "commit-1:invocation", "invocation", "test.native-result", "result-input")
        };
        var intent = new RuntimePostCommitIntent("root", workflow.WorkflowExecutionId, "test.native-root", at, null,
            "root", JsonSerializer.SerializeToElement(new { node = "root" }));
        var outboxId = RuntimePostCommitOutboxIdentity.CreateLogicalValue("native-admission", intent.IntentId);
        var item = new RuntimePostCommitOutboxItem(outboxId, intent, RuntimePostCommitOutboxStatus.Pending, at, at);
        var admission = new RuntimeCheckpointCommit("native-admission", new("native-admission", RuntimeCheckpointNames.WorkflowStarted,
                workflow.WorkflowExecutionId, at, [], new Dictionary<string, string>()),
            new(new(workflow.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()),
                null, [], [], [], [], [], postCommitOutbox: [new(outboxId, RuntimeStateChangeOperation.Upsert, item, new Dictionary<string, string>())]),
            [intent], new Dictionary<string, string>()) { ExpectedFence = nativeLease.ToFence(), ExpectedModelExecution = expected };
        var acknowledgement = new NativeAdmissionAcknowledgement(scenario);
        var lookup = new NativeAdmissionLookup(acknowledgement, scenario == "ack-unreadable");
        await using (var context = open(fixture.ConnectionString, [acknowledgement, lookup]))
        {
            // Only the lease boundary is injected. All participant writes, COMMIT and marker reads use this provider.
            var writer = new EfRuntimeCheckpointCommitStore(context, access, new Clock(), new NativeAdmissionLease(scenario));
            if (scenario == "before-write")
                await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => writer.CommitAsync(admission, Immediate).AsTask());
            else if (scenario == "ack-unreadable")
            {
                var failure = await Assert.ThrowsAnyAsync<Exception>(() => writer.CommitAsync(admission, Immediate).AsTask());
                Assert.Equal("NativeAdmissionCommitOutcomeUnknownException", failure.GetType().Name);
                var causes = Assert.IsType<AggregateException>(failure.InnerException);
                Assert.Same(acknowledgement.Failure, causes.InnerExceptions[0]);
                Assert.Same(lookup.Failure, causes.InnerExceptions[1]);
            }
            else Assert.Equal([outboxId], (await writer.CommitAsync(admission, Immediate)).PendingPostCommitWorkIds);
        }
        await using var reopened = open(fixture.ConnectionString, []);
        var landed = scenario != "before-write";
        Assert.Equal(landed ? 1 : 0, await reopened.WorkflowExecutionStates.CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Equal(landed ? 1 : 0, await reopened.RuntimePostCommitOutbox.CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Equal(landed ? 2 : 1, await reopened.RuntimeCheckpointCommits.CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Equal(1, (await Reader(reopened, access).FindAsync("execution"))!.Revision);
        if (landed)
            Assert.Equal([outboxId], (await Store(reopened, access).CommitAsync(admission, Immediate)).PendingPostCommitWorkIds);
    }

    private sealed class NativeAdmissionLease(string scenario) : IWorkflowExecutableRootWriteLeaseManager
    {
        public async ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default)
        {
            if (scenario == "before-write") throw new WorkflowExecutableRootWriteLeaseLostException(artifactId, leaseId);
            await write(cancellationToken);
            if (scenario == "after-write") throw new WorkflowExecutableRootWriteLeaseLostException(artifactId, leaseId);
        }
    }

    private sealed class NativeAdmissionAcknowledgement(string scenario) : DbTransactionInterceptor
    {
        public bool Committed { get; private set; }
        public Exception Failure { get; } = scenario == "ack-cancel" ? new OperationCanceledException("Native commit acknowledgement cancelled.")
            : new IOException("Native commit acknowledgement lost.");
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!scenario.StartsWith("ack-", StringComparison.Ordinal)) return Task.CompletedTask;
            Committed = true;
            throw Failure;
        }
    }

    private sealed class NativeAdmissionLookup(NativeAdmissionAcknowledgement acknowledgement, bool fail) : DbCommandInterceptor
    {
        public IOException Failure { get; } = new("Native admission marker lookup failed.");
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (fail && acknowledgement.Committed && command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.Ordinal))
                throw Failure;
            return ValueTask.FromResult(result);
        }
    }

    public static async Task AtomicAsync(RuntimeBookmarksProviderFixture fixture, Func<string, IInterceptor[], RuntimeDbContext> open, bool failMarker)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var access = new Access(new("model-atomic-" + Guid.NewGuid().ToString("N")));
        var material = await PrepareAsync(fixture.ConnectionString, open, access);
        RuntimeSchedulerWorkClaim claim;
        long fenceRevision;
        await using (var setup = open(fixture.ConnectionString, []))
        {
            var queue = new EfSchedulerWorkQueueStore(setup, access, new Codec());
            var command = new RuntimeModelExecutionCommand(material.Identity, material.IncarnationId, "input-1", "start",
                JsonSerializer.SerializeToElement(new { }), new("system", "initiator"), null);
            await queue.EnqueueAsync(new("model-input", "execution", command.InputId, WorkflowExecutionCommandKind.ExecuteModel,
                "envelope", "enqueue-input", Now, Now, payload: JsonSerializer.SerializeToElement(command, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            claim = (await queue.ClaimAsync(new("execution", "queue-owner", Now, TimeSpan.FromMinutes(5))))!;
            fenceRevision = (await setup.ExecutionLivenessStates.AsNoTracking().SingleAsync(row => row.ScopeKeyHash == access.Hash)).Revision;
        }
        var commit = Commit(access, material, 1);
        var intent = new RuntimePostCommitIntent("next", "execution", "test.model.next", Now.AddSeconds(1), null, null, JsonSerializer.SerializeToElement(new { waiting = "next" }));
        var outboxId = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId);
        var item = new RuntimePostCommitOutboxItem(outboxId, intent, RuntimePostCommitOutboxStatus.Pending, Now.AddSeconds(1), Now.AddSeconds(1));
        commit = commit with
        {
            PostCommitIntents = [intent],
            StateChanges = commit.StateChanges.WithConsumedSchedulerWorkItems([ConsumedSchedulerWorkItem.FromClaim(claim)])
                .WithPostCommitOutbox([new(outboxId, RuntimeStateChangeOperation.Upsert, item, new Dictionary<string, string>())])
        };
        var probe = new BeforeMarker(access.Hash, failMarker);
        await using (var context = open(fixture.ConnectionString, [probe]))
        {
            probe.Delimit = context.GetService<ISqlGenerationHelper>().DelimitIdentifier;
            if (failMarker)
            {
                var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => Store(context, access).CommitAsync(commit, Immediate).AsTask());
                Assert.IsType<DbUpdateException>(failure.InnerException);
                Assert.Contains(commit.ModelState!.State.Scope.Value, failure.Message);
                Assert.Contains(commit.WorkflowExecutionId, failure.Message);
                Assert.Contains(commit.ModelState.Input.InputId, failure.Message);
                Assert.Contains(commit.CommitId, failure.Message);
                Assert.Equal(BeforeMarker.Failure, failure.GetBaseException().Message);
            }
            else
            {
                var accepted = await Store(context, access).CommitAsync(commit, Immediate);
                Assert.Equal(["model-input"], accepted.ConsumedSchedulerWorkItemIds);
                Assert.Equal([outboxId], accepted.PendingPostCommitWorkIds);
            }
        }
        // These counts are observed through the same physical transaction before the actual final-marker insert.
        Assert.Equal(new[] { 1, 1, 1, 0, 0 }, probe.Counts);
        await using var reopened = open(fixture.ConnectionString, []);
        var expected = failMarker ? 0 : 1;
        Assert.Equal(expected, await reopened.Set<RuntimeModelExecutionEntity>().CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Equal(expected, await reopened.Set<RuntimeModelInputEntity>().CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Equal(expected, await reopened.RuntimePostCommitOutbox.CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Equal(expected, await reopened.RuntimeCheckpointCommits.CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Equal(failMarker ? 1 : 0, await reopened.SchedulerWorkItems.CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Empty(await reopened.WorkflowExecutionStates.Where(row => row.ScopeKeyHash == access.Hash).ToArrayAsync());
        var fence = await reopened.ExecutionLivenessStates.AsNoTracking().SingleAsync(row => row.ScopeKeyHash == access.Hash);
        Assert.Equal(failMarker ? fenceRevision : fenceRevision + 1, fence.Revision);
        if (failMarker)
        {
            var retained = await reopened.SchedulerWorkItems.AsNoTracking().SingleAsync(row => row.ScopeKeyHash == access.Hash);
            Assert.Equal(EfRelationalIdentity.Encode(claim.OwnerId), retained.ClaimOwnerId);
            Assert.Equal(claim.FencingToken, retained.ClaimToken);
            await Store(reopened, access).CommitAsync(commit, Immediate);
        }
        else await Store(reopened, access).CommitAsync(commit, Immediate); // Consumed claim replay needs no second admission.
        Assert.Equal(1, (await Reader(reopened, access).FindAsync("execution"))!.Revision);
        Assert.Equal(1, (await Reader(reopened, access).FindInputAsync("execution", "input-1"))!.AcceptedRevision);
        Assert.Equal(1, await reopened.RuntimeCheckpointCommits.CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Equal(1, await reopened.RuntimePostCommitOutbox.CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Empty(await reopened.SchedulerWorkItems.Where(row => row.ScopeKeyHash == access.Hash).ToArrayAsync());
    }

    public static async Task ConflictAsync(RuntimeBookmarksProviderFixture fixture, Func<string, IInterceptor[], RuntimeDbContext> open)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var access = new Access(new("model-fence-" + Guid.NewGuid().ToString("N")));
        var material = await PrepareAsync(fixture.ConnectionString, open, access);
        var first = Commit(access, material, 1);
        await using (var context = open(fixture.ConnectionString, [])) await Store(context, access).CommitAsync(first, Immediate);
        await using (var context = open(fixture.ConnectionString, []))
        {
            var store = Store(context, access);
            var fenceBefore = (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync(row => row.ScopeKeyHash == access.Hash)).Revision;
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.CommitAsync(Commit(access, material, 2) with
                { ExpectedFence = new("superseded-lease", "old-owner", 1) }, Immediate).AsTask());
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.CommitAsync(Commit(access, material, 3), Immediate).AsTask());
            var second = Commit(access, material, 2);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(Commit(access, material, 2, inputId: "input-1"), Immediate).AsTask());
            Assert.Equal(fenceBefore, (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync(row => row.ScopeKeyHash == access.Hash)).Revision);
            Assert.Equal(1, (await Reader(context, access).FindAsync("execution"))!.Revision);
            Assert.Null(await Reader(context, access).FindInputAsync("execution", "input-2"));
            Assert.Single(await context.RuntimeCheckpointCommits.Where(row => row.ScopeKeyHash == access.Hash).ToArrayAsync());
            await store.CommitAsync(second, Immediate);
        }
        await using var restarted = open(fixture.ConnectionString, []);
        await Store(restarted, access).CommitAsync(first, Immediate);
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => Store(restarted, access)
            .CommitAsync(Commit(access, material, 1, waiting: "conflict"), Immediate).AsTask());
        Assert.Equal(2, (await Reader(restarted, access).FindAsync("execution"))!.Revision);
        Assert.Equal(2, await restarted.Set<RuntimeModelInputEntity>().CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Equal(2, await restarted.RuntimeCheckpointCommits.CountAsync(row => row.ScopeKeyHash == access.Hash));
    }

    public static async Task SettlementAsync(RuntimeBookmarksProviderFixture fixture, Func<string, IInterceptor[], RuntimeDbContext> open)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var access = new Access(new("model-settlement-" + Guid.NewGuid().ToString("N")));
        var material = await PrepareAsync(fixture.ConnectionString, open, access);
        RuntimeCheckpointCommit Cleanup(long revision, RuntimeModelLifecycle lifecycle, string outcome = "Faulted")
        {
            var commit = Commit(access, material, revision, waiting: "physical-cleanup");
            var state = commit.ModelState!.State;
            var terminal = lifecycle is RuntimeModelLifecycle.TerminalSettled or RuntimeModelLifecycle.TerminalPendingSettlement
                ? JsonSerializer.SerializeToElement(new { status = outcome }) : (JsonElement?)null;
            var proposed = new RuntimeModelExecutionState(state.FormatVersion, state.Scope, state.ExecutionId, state.TenantId, state.Protocol,
                state.Executable, state.MaterialIncarnationId, state.Revision, lifecycle, state.Authority, state.CreatedAt, state.UpdatedAt, state.Payload, terminal);
            return commit with { ModelState = new(commit.ModelState.ExpectedRevision, proposed, commit.ModelState.Input) };
        }
        await using (var context = open(fixture.ConnectionString, []))
            await Store(context, access).CommitAsync(Cleanup(1, RuntimeModelLifecycle.TerminalSettled), Immediate);
        await using (var context = open(fixture.ConnectionString, []))
        {
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => Store(context, access).CommitAsync(
                Cleanup(2, RuntimeModelLifecycle.TerminalPendingSettlement) with { ExpectedFence = new("superseded-lease", "old-owner", 1) }, Immediate).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(context, access).CommitAsync(Cleanup(2, RuntimeModelLifecycle.Running), Immediate).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(context, access).CommitAsync(Cleanup(2, RuntimeModelLifecycle.TerminalPendingSettlement, "Completed"), Immediate).AsTask());
            Assert.Equal(1, (await Reader(context, access).FindAsync("execution"))!.Revision);
            Assert.Null(await Reader(context, access).FindInputAsync("execution", "input-2"));
            await Store(context, access).CommitAsync(Cleanup(2, RuntimeModelLifecycle.TerminalPendingSettlement), Immediate);
        }
        await using (var restarted = open(fixture.ConnectionString, []))
        {
            var state = (await Reader(restarted, access).FindAsync("execution"))!;
            Assert.Equal(RuntimeModelLifecycle.TerminalPendingSettlement, state.Lifecycle);
            Assert.Equal("Faulted", state.TerminalResult!.Value.GetProperty("status").GetString());
            Assert.True(WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, state.Executable));
            await Store(restarted, access).CommitAsync(Cleanup(3, RuntimeModelLifecycle.TerminalSettled), Immediate);
        }
        await using var final = open(fixture.ConnectionString, []);
        var settled = (await Reader(final, access).FindAsync("execution"))!;
        Assert.Equal(3, settled.Revision); Assert.Equal(RuntimeModelLifecycle.TerminalSettled, settled.Lifecycle);
        Assert.Equal("Faulted", settled.TerminalResult!.Value.GetProperty("status").GetString());
        Assert.Equal(3, await final.Set<RuntimeModelInputEntity>().CountAsync(row => row.ScopeKeyHash == access.Hash));
        Assert.Equal(3, await final.RuntimeCheckpointCommits.CountAsync(row => row.ScopeKeyHash == access.Hash));
    }

    private static async Task<RuntimeExecutionMaterial> PrepareAsync(string connection, Func<string, IInterceptor[], RuntimeDbContext> open, Access access)
    {
        await using var context = open(connection, []);
        await context.Database.EnsureCreatedAsync();
        IWorkflowExecutableStore artifacts = new EfWorkflowExecutableStore(context, access);
        var seed = new WorkflowExecutableIdentity("model-artifact", "definition", "version", "1.0.0", "pending");
        var definition = JsonSerializer.SerializeToElement(new { waiting = "human" });
        var model = new RuntimeModelMaterial(1, seed, new("test.model", 1), "physical-atomic-probe-v1", definition, new(1, []), [], [], []);
        var pin = seed with { ArtifactHash = model.ComputeContentHash() };
        await artifacts.SaveModelAsync(new(1, pin, model.Protocol, model.Profile, definition, model.InputContract, [], [], []));
        await new EfExecutionLivenessStateStore(context, access, new Codec()).SaveAsync(new("ownership:execution", "execution", Lease, null, null, null));
        return (await artifacts.FindMaterialAsync(pin.ArtifactId))!;
    }

    private static RuntimeCheckpointCommit Commit(Access access, RuntimeExecutionMaterial material, long revision, string? inputId = null, string waiting = "human")
    {
        var at = Now.AddSeconds(revision);
        var state = new RuntimeModelExecutionState(1, access.Current.RequireScope(), "execution", null, material.Model!.Protocol, material.Identity,
            material.IncarnationId, revision, RuntimeModelLifecycle.Running, new("system", "initiator"), Now, at, JsonSerializer.SerializeToElement(new { waiting }), null);
        return new("commit-" + revision, new("checkpoint-" + revision, "ModelDecision", "execution", at, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
        {
            ExpectedFence = Lease.ToFence(),
            ModelState = new(revision == 1 ? null : revision - 1, state,
                new(state.Scope, state.ExecutionId, inputId ?? "input-" + revision, "test.input", "content-" + revision, revision, at,
                    null, null, JsonSerializer.SerializeToElement(new { status = "Accepted" })))
        };
    }

    private static EfRuntimeCheckpointCommitStore Store(RuntimeDbContext context, Access access) => new(context, access, new Clock(),
        new WorkflowExecutableRootWriteLeaseManager(new EfWorkflowExecutableStore(context, access), Options.Create(new WorkflowExecutableGarbageCollectionOptions()), new Clock()));
    private static EfRuntimeModelExecutionReader Reader(RuntimeDbContext context, Access access) => new(context, access, new Codec());
    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(scope);
        public string Hash { get; } = EfRelationalIdentity.Hash(scope.Value);
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Codec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public byte[] Decode(string purpose, string token) => throw new NotSupportedException();
    }
    private sealed class BeforeMarker(string scopeHash, bool fail) : DbCommandInterceptor
    {
        internal const string Failure = "Injected model final-marker failure.";
        internal Func<string, string> Delimit { get; set; } = null!;
        internal int[]? Counts { get; private set; }

        private async ValueTask ObserveAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (!command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) ||
                !command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.Ordinal)) return;
            var counts = new List<int>();
            foreach (var table in Tables)
            {
                await using var inspect = command.Connection!.CreateCommand(); inspect.Transaction = command.Transaction;
                inspect.CommandText = $"SELECT COUNT(*) FROM {Delimit(table)} WHERE {Delimit("ScopeKeyHash")}=@scope";
                var parameter = inspect.CreateParameter(); parameter.ParameterName = "@scope"; parameter.Value = scopeHash; inspect.Parameters.Add(parameter);
                counts.Add(Convert.ToInt32(await inspect.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture));
            }
            Counts = counts.ToArray();
            if (fail) throw new InvalidOperationException(Failure);
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { await ObserveAsync(command, cancellationToken); return result; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { await ObserveAsync(command, cancellationToken); return result; }
    }
}
