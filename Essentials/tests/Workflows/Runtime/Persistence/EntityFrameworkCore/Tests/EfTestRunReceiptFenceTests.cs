using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfTestRunReceiptFenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("open")]
    [InlineData("closed")]
    [InlineData("expired")]
    public async Task Receipt_upsert_and_exact_bookmark_consumption_share_the_test_scope_fence(string scopeState)
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(Start);
        const string testRunId = "test-run";
        const string workflowExecutionId = "execution";
        const string waitId = "wait";
        var expiresAt = scopeState == "expired" ? Start.AddMinutes(1) : Start.AddMinutes(30);
        var scope = new WorkflowTestScope(testRunId, expiresAt, "tenant-a",
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue));

        await using (var seed = database.Open())
        {
            var accessor = new FixedAccessor("tenant-a");
            var scopeStore = new EfWorkflowTestScopeStore(seed, accessor, new NoopContinuationCodec());
            await scopeStore.CreateAsync(scope, Start);
            var seedExecution = Execution(workflowExecutionId, scope);
            await new EfRuntimeCheckpointCommitStore(seed, accessor, clock, new PassThroughRootWriteLeaseManager())
                .CommitAsync(new RuntimeCheckpointCommit(
                    "seed-execution",
                    new RuntimeCheckpoint("seed-execution-checkpoint", "WorkflowStarted", workflowExecutionId, Start, [], new Dictionary<string, string>()),
                    new RuntimeCheckpointStateChangeSet(
                        new RuntimeStateChange<WorkflowExecutionState>(workflowExecutionId, RuntimeStateChangeOperation.Upsert,
                            seedExecution, new Dictionary<string, string>()),
                        null, [], [], [], [], []),
                    [], new Dictionary<string, string>()),
                    new(RuntimeCheckpointPersistenceMode.Immediate));
            await new EfBookmarkStateStore(seed, accessor).SaveAsync(Bookmark(waitId, workflowExecutionId));

            if (scopeState == "closed")
            {
                await scopeStore.CloseAsync(new WorkflowTestScopeCloseRequest(
                    testRunId, WorkflowTestScopeCloseReason.ExplicitTeardown, Start.AddMinutes(1)));
                clock.Advance(TimeSpan.FromMinutes(1));
            }
            else if (scopeState == "expired")
                clock.Advance(TimeSpan.FromMinutes(1));
        }

        var receipt = new TestRunDeliveryReceipt(testRunId, Guid.NewGuid().ToString("D"), waitId,
            TestRunDeliveryReceipt.Digest(null));
        var metadata = new Dictionary<string, string> { [TestRunDeliveryReceipt.MetadataKey] = receipt.DeliveryId };
        var executionWithReceipt = receipt.Record(Execution(workflowExecutionId, scope));
        var bookmark = Bookmark(waitId, workflowExecutionId);
        var commit = new RuntimeCheckpointCommit(
            $"consume-{scopeState}",
            new RuntimeCheckpoint($"checkpoint-{scopeState}", "BookmarkConsumed", workflowExecutionId, clock.GetUtcNow(), [], metadata),
            new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(workflowExecutionId, RuntimeStateChangeOperation.Upsert,
                    executionWithReceipt, metadata),
                null, [],
                [new RuntimeStateChange<BookmarkState>(waitId, RuntimeStateChangeOperation.Delete, bookmark, metadata)],
                [], [], []),
            [], metadata);

        await using (var writerContext = database.Open())
        {
            var writer = new EfRuntimeCheckpointCommitStore(writerContext, new FixedAccessor("tenant-a"), clock,
                new PassThroughRootWriteLeaseManager());
            if (scopeState == "open")
                await writer.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
            else
                await Assert.ThrowsAsync<TestScopeAdmissionException>(() =>
                    writer.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        }

        await using var verification = database.Open();
        var accessorForRead = new FixedAccessor("tenant-a");
        var persistedExecution = await new EfWorkflowExecutionStateStore(
            verification, accessorForRead, new NoopContinuationCodec()).FindAsync(workflowExecutionId);
        var persistedBookmark = await new EfBookmarkStateStore(verification, accessorForRead)
            .FindAsync(workflowExecutionId, waitId);

        if (scopeState == "open")
        {
            Assert.True(receipt.MatchesExisting(persistedExecution!));
            Assert.Null(persistedBookmark);
            Assert.Equal(2, await verification.RuntimeCheckpointCommits.CountAsync());
        }
        else
        {
            Assert.Null(receipt.MatchesExisting(persistedExecution!));
            Assert.NotNull(persistedBookmark);
            Assert.Single(await verification.RuntimeCheckpointCommits.ToArrayAsync());
        }
    }

    private static WorkflowExecutionState Execution(string id, WorkflowTestScope scope) => new(
        id,
        new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "hash"),
        WorkflowExecutionStatus.Suspended,
        null,
        Start,
        Start,
        Start,
        null,
        null,
        null,
        scope.TenantId,
        new Dictionary<string, string>())
    {
        RunKind = WorkflowRunKind.TestRun,
        Partition = scope.Partition,
        TestScope = scope
    };

    private static BookmarkState Bookmark(string id, string executionId) => new(
        id, executionId, "activity", "node", "resume", "Event", "stimulus", null,
        new Dictionary<string, string>(), Start, Start.AddHours(1));

    private sealed class TestDatabase(SqliteConnection connection) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var context = new RuntimeSqliteDbContext(
                new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection);
        }

        public RuntimeSqliteDbContext Open() => new(
            new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class NoopContinuationCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }

    private sealed class PassThroughRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => write(cancellationToken);
    }
}
