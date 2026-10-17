using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class BackgroundActivityWorkTests
{
    private static readonly DateTimeOffset Now = new(2031, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Fact]
    public async Task Expired_claim_is_reclaimed_and_old_worker_is_fenced()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = Request("work-fence");
        await using (var context = database.Open())
            await AdmitAsync(context, request, "admit-fence");

        await using var workerAContext = database.Open();
        await using var workerBContext = database.Open();
        var workerA = new EfBackgroundActivityWorkStore(workerAContext, new FixedAccessor("tenant-a"));
        var workerB = new EfBackgroundActivityWorkStore(workerBContext, new FixedAccessor("tenant-a"));
        var first = await workerA.ClaimNextAsync("worker-a", Now, TimeSpan.FromMinutes(1));
        var second = await workerB.ClaimNextAsync("worker-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(second!.ClaimFence > first!.ClaimFence);
        Assert.Null(await workerA.RenewClaimAsync(first, Now.AddMinutes(2), TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<BackgroundActivityWorkConflictException>(() => workerA.RecordResultAsync(
            first, Complete("{\"value\":1}"), Now.AddMinutes(2)).AsTask());

        var recorded = await workerB.RecordResultAsync(second, Complete("{\"value\":1}"), Now.AddMinutes(2).AddSeconds(1));
        var replay = await workerB.RecordResultAsync(second, Complete("{ \"value\" : 1 }"), Now.AddHours(1));
        Assert.Equal(BackgroundActivityWorkStatus.ResultReady, recorded.Status);
        Assert.Equal(recorded.ReceiptDigest, replay.ReceiptDigest);
        await Assert.ThrowsAsync<BackgroundActivityWorkConflictException>(() => workerB.RecordResultAsync(
            second, Complete("{\"value\":2}"), Now.AddHours(1)).AsTask());
    }

    [Fact]
    public async Task Failed_checkpoint_marker_rolls_back_background_admission()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = Request("work-rollback");
        var interceptor = new FailCheckpointMarkerInsertInterceptor();
        await using (var context = database.Open(interceptor))
        {
            interceptor.Arm();
            await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => AdmitAsync(context, request, "admit-rollback"));
        }

        await using var verification = database.Open();
        var store = new EfBackgroundActivityWorkStore(verification, new FixedAccessor("tenant-a"));
        Assert.Null(await store.FindAsync("workflow-a", request.WorkId));
        Assert.Empty(await verification.RuntimeCheckpointCommits.ToArrayAsync());
    }

    private static async Task AdmitAsync(RuntimeDbContext context, BackgroundActivityWorkRequest request, string commitId)
    {
        var commit = Commit(commitId);
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(
            commit with
            {
                StateChanges = commit.StateChanges.WithBackgroundActivityWork(
                    new BackgroundActivityWorkCheckpointChanges(admissions: [request]))
            },
            new(RuntimeCheckpointPersistenceMode.Immediate));
    }

    private static BackgroundActivityWorkRequest Request(string workId)
    {
        var attempt = new ActivityAttempt($"attempt-{workId}", $"invocation-{workId}", 1, ActivityAttemptReason.Initial, Now);
        return new BackgroundActivityWorkRequest(
            workId, "workflow-a", $"activity-{workId}", attempt,
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            "node", "contract-fingerprint",
            new ActivityInputSnapshot(attempt.InvocationId, "contract-fingerprint", "binding-fingerprint", new Dictionary<string, ValueEnvelope>(), Now),
            new RuntimeSchedulerWorkItem($"coordinator-{workId}", "workflow-a", $"command-{workId}", WorkflowExecutionCommandKind.ScheduleActivity,
                $"envelope-{workId}", $"idempotency-{workId}", Now, Now, 1));
    }

    private static BackgroundActivityWorkTerminalResult Complete(string resultJson) =>
        new(BackgroundActivityWorkResultKind.Complete, resultJson: resultJson);

    private static RuntimeCheckpointCommit Commit(string commitId) => new(
        commitId,
        new RuntimeCheckpoint($"checkpoint-{commitId}", "BackgroundActivityWork", "workflow-a", Now, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []), [], new Dictionary<string, string>());

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:bg-work-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(keeper).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper, connectionString);
        }

        public RuntimeSqliteDbContext Open(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connectionString);
            if (interceptors.Length > 0)
                builder.AddInterceptors(interceptors);
            return new RuntimeSqliteDbContext(builder.Options);
        }

        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class FailCheckpointMarkerInsertInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private int armed;

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        private void ThrowIfArmed(System.Data.Common.DbCommand command)
        {
            if (command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref armed, 0) == 1)
                throw new Microsoft.EntityFrameworkCore.DbUpdateException("Simulated checkpoint marker insert failure.");
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }
    }
}
