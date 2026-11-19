using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RetainedNativeRootPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    private static RuntimeDbContext Open(string connection, IInterceptor[] interceptors) => new RuntimePostgreSqlDbContext(
        new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).AddInterceptors(interceptors).Options);

    [SkippableTheory]
    [InlineData("unchanged")]
    [InlineData("missing")]
    [InlineData("hash")]
    [InlineData("revision")]
    [InlineData("delete")]
    [InlineData("stale-fence")]
    [InlineData("deleted-owner")]
    [InlineData("uncertain")]
    public Task PostgreSql_retained_native_root_preserves_lease_fence_replay_and_atomicity(string scenario) =>
        RetainedNativeRootProviderSmoke.RunAsync(fixture, Open, scenario);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RetainedNativeRootSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    private static RuntimeDbContext Open(string connection, IInterceptor[] interceptors) => new RuntimeSqlServerDbContext(
        new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).AddInterceptors(interceptors).Options);

    [SkippableTheory]
    [InlineData("unchanged")]
    [InlineData("missing")]
    [InlineData("hash")]
    [InlineData("revision")]
    [InlineData("delete")]
    [InlineData("stale-fence")]
    [InlineData("deleted-owner")]
    [InlineData("uncertain")]
    public Task SqlServer_retained_native_root_preserves_lease_fence_replay_and_atomicity(string scenario) =>
        RetainedNativeRootProviderSmoke.RunAsync(fixture, Open, scenario);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RetainedNativeRootMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    private static RuntimeDbContext Open(string connection, IInterceptor[] interceptors) => new RuntimeMySqlDbContext(
        new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).AddInterceptors(interceptors).Options);

    [SkippableTheory]
    [InlineData("unchanged")]
    [InlineData("missing")]
    [InlineData("hash")]
    [InlineData("revision")]
    [InlineData("delete")]
    [InlineData("stale-fence")]
    [InlineData("deleted-owner")]
    [InlineData("uncertain")]
    public Task MySql_retained_native_root_preserves_lease_fence_replay_and_atomicity(string scenario) =>
        RetainedNativeRootProviderSmoke.RunAsync(fixture, Open, scenario);
}

internal static class RetainedNativeRootProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);
    private const string WorkflowId = "retained-native-root";

    public static async Task RunAsync(RuntimeBookmarksProviderFixture fixture, Func<string, IInterceptor[], RuntimeDbContext> open, string scenario)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = "retained-root-" + Guid.NewGuid().ToString("N");
        var access = new Access(scope);
        var codec = new Codec();
        var lease = new RuntimeExecutionLease("lease", WorkflowId, "owner", Now, Now.AddHours(1), 2);
        var state = new WorkflowExecutionState(WorkflowId,
            new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
            null, Now, Now, Now, null, null, null, scope, new Dictionary<string, string>());
        await using (var seed = open(fixture.ConnectionString, []))
        {
            await seed.Database.EnsureCreatedAsync();
            if (scenario != "missing")
                await new EfRuntimeCheckpointCommitStore(seed, access, new Clock(), new LeaseManager(false)).CommitAsync(Commit("seed", state), Immediate);
            await new EfExecutionLivenessStateStore(seed, access, codec).SaveAsync(new("ownership:" + WorkflowId, WorkflowId, lease, null, null, null));
            if (scenario == "deleted-owner")
                Assert.True(await new EfWorkflowExecutionStateStore(seed, access, codec).DeleteAsync(WorkflowId));
        }

        var id = EfWorkflowExecutionStateStore.CreateId(scope, WorkflowId);
        var race = new RowRace(id, scenario);
        var ack = new LostAcknowledgement(scenario == "uncertain");
        await using var context = open(fixture.ConnectionString, [race, ack]);
        var before = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id);
        var livenessId = EfRuntimeOperationalStoreSupport.CompositeId(scope, WorkflowId, "ownership:" + WorkflowId);
        var beforeFence = (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync(row => row.Id == livenessId)).Revision;
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        using var hint = sessions.Push(new RuntimeCoalescingSession(WorkflowId, new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions(), mode: new(PersistenceMode.Durable, RunHasRecord: true)));
        var manager = new LeaseManager(true);
        var writer = new EfRuntimeCheckpointCommitStore(context, access, new Clock(), manager) { CoalescingSessions = sessions };
        var update = state with { SystemMetadata = new Dictionary<string, string> { ["value"] = "updated" } };
        if (scenario == "hash") update = update with { PinnedExecutable = update.PinnedExecutable with { ArtifactHash = "changed" } };
        var commit = Commit("update", update) with { ExpectedFence = lease.ToFence() };
        if (scenario == "stale-fence") commit = commit with { ExpectedFence = new(lease.LeaseId, lease.OwnerId, 1) };
        var succeeds = scenario is "unchanged" or "uncertain";
        if (scenario is "missing" or "hash")
            await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() => writer.CommitAsync(commit, Immediate).AsTask());
        else if (scenario is "stale-fence" or "deleted-owner")
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => writer.CommitAsync(commit, Immediate).AsTask());
        else if (scenario is "revision" or "delete")
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => writer.CommitAsync(commit, Immediate).AsTask());
        else
        {
            await writer.CommitAsync(commit, Immediate);
            await writer.CommitAsync(commit, Immediate);
        }
        Assert.Equal(scenario is "missing" or "hash" ? 1 : 0, manager.Calls);
        if (scenario is "revision" or "delete") Assert.True(race.Executed);
        Assert.Equal(scenario == "uncertain", ack.Executed);
        await using var read = open(fixture.ConnectionString, []);
        var saved = await read.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id);
        if (succeeds)
        {
            Assert.Equal(before!.Revision + 1, saved!.Revision);
            Assert.Equal("updated", EfWorkflowExecutionStateStore.ReadChecked(saved, scope, WorkflowId).SystemMetadata["value"]);
        }
        else
        {
            Assert.Equal(before?.Revision, saved?.Revision);
            Assert.Equal(before?.ContentJson, saved?.ContentJson);
            Assert.Equal(beforeFence, (await read.ExecutionLivenessStates.AsNoTracking().SingleAsync(row => row.Id == livenessId)).Revision);
        }
        Assert.Equal(succeeds ? 1 : 0, await read.RuntimeCheckpointCommits.CountAsync(row =>
            row.ScopeKeyHash == EfRelationalIdentity.Hash(scope) && row.CommitId == EfRelationalIdentity.Encode("update")));
    }

    private static RuntimeCheckpointCommit Commit(string id, WorkflowExecutionState state) => new(id,
        new("checkpoint-" + id, "RetainedRoot", WorkflowId, Now, [], new Dictionary<string, string>()),
        new(new(WorkflowId, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>()), null, [], [], [], [], []),
        [], new Dictionary<string, string>());

    private sealed class Access(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Codec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public byte[] Decode(string purpose, string token) => throw new NotSupportedException();
    }
    private sealed class LeaseManager(bool refuse) : IWorkflowExecutableRootWriteLeaseManager
    {
        public int Calls { get; private set; }
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (refuse) throw new WorkflowExecutableRootWriteLeaseUnavailableException(artifactId, leaseId);
            return write(cancellationToken);
        }
    }
    private sealed class RowRace(string id, string scenario) : SaveChangesInterceptor
    {
        public bool Executed { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Executed && scenario is "revision" or "delete")
            {
                Executed = true;
                var rows = ((RuntimeDbContext)eventData.Context!).WorkflowExecutionStates.Where(row => row.Id == id);
                if (scenario == "delete") await rows.ExecuteDeleteAsync(cancellationToken);
                else await rows.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
            }
            return result;
        }
    }
    private sealed class LostAcknowledgement(bool armed) : DbTransactionInterceptor
    {
        public bool Executed { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (armed && !Executed)
            {
                Executed = true;
                throw new InvalidOperationException("Injected retained-root acknowledgement loss.");
            }
            return Task.CompletedTask;
        }
    }
}
