using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class BackgroundActivityWorkExecutionDeletionTests
{
    private static readonly DateTimeOffset Now = new(2034, 5, 6, 7, 8, 9, TimeSpan.Zero);

    [Fact]
    public async Task Initial_workflow_upsert_and_background_admission_can_share_the_first_checkpoint()
    {
        await using var database = await TestDatabase.CreateAsync();
        var accessor = new FixedAccessor("tenant-a");
        await using var context = database.Open();
        var liveness = new EfExecutionLivenessStateStore(context, accessor, new NoopContinuationCodec());
        var owner = await new RuntimeExecutionOwnershipService(
            liveness,
            new FixedTimeProvider(Now),
            new RuntimeExecutionOwnershipOptions { OwnerId = "initial-owner", LeaseDuration = TimeSpan.FromHours(1) })
            .AcquireAsync("workflow-a");
        var request = Request("work-initial-upsert");
        var checkpointStore = new EfRuntimeCheckpointCommitStore(context, accessor, new FixedTimeProvider(Now), new PassThroughRootWriteLeaseManager());
        await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() =>
            checkpointStore.CommitAsync(AdmissionCommit("admit-missing-upsert", request, owner), Decision()).AsTask());
        var workflowState = new WorkflowExecutionState(
            "workflow-a",
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running,
            null,
            Now,
            Now,
            Now,
            null,
            null,
            null,
            "tenant-a",
            new Dictionary<string, string>());
        var commit = AdmissionCommit("admit-initial-upsert", request, owner) with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    "workflow-a", RuntimeStateChangeOperation.Upsert, workflowState, new Dictionary<string, string>()),
                null, [], [], [], [], []).WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: [request]))
        };

        await checkpointStore.CommitAsync(commit, Decision());

        Assert.NotNull(await ExecutionStore(context, accessor).FindAsync("workflow-a"));
        Assert.Equal(BackgroundActivityWorkStatus.Pending,
            (await new EfBackgroundActivityWorkStore(context, accessor).FindAsync("workflow-a", request.WorkId))!.Status);
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Deleting_execution_without_prior_owner_seeds_a_fencing_tombstone()
    {
        await using var database = await TestDatabase.CreateAsync();
        var accessor = new FixedAccessor("tenant-a");
        await using var context = database.Open();
        await new EfWorkflowExecutionStateStore(context, accessor, new NoopContinuationCodec(), new FixedTimeProvider(Now))
            .SaveAsync(new WorkflowExecutionState(
                "workflow-a",
                new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
                WorkflowExecutionStatus.Running,
                null,
                Now,
                Now,
                Now,
                null,
                null,
                null,
                "tenant-a",
                new Dictionary<string, string>()));

        Assert.True(await ExecutionStore(context, accessor).DeleteAsync("workflow-a"));
        var liveness = new EfExecutionLivenessStateStore(context, accessor, new NoopContinuationCodec());
        var tombstone = await liveness.FindAsync("workflow-a", RuntimeExecutionOwnershipStateId.For("workflow-a"));
        Assert.Null(tombstone!.ExecutionLease);
        Assert.Equal("0", tombstone.Metadata[Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.OwnershipFencingToken]);
        Assert.Equal("true", tombstone.Metadata[RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone]);

        var owner = await new RuntimeExecutionOwnershipService(
            liveness,
            new FixedTimeProvider(Now),
            new RuntimeExecutionOwnershipOptions { OwnerId = "owner-after-delete", LeaseDuration = TimeSpan.FromHours(1) })
            .AcquireAsync("workflow-a");
        Assert.Equal(1, owner.FencingToken);
        Assert.Null(await ExecutionStore(context, accessor).FindAsync("workflow-a"));
    }

    [Fact]
    public async Task Deleting_before_claim_tombstones_pending_work_and_preserves_the_fence_counter()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = Request("work-pending");
        RuntimeExecutionLease owner;
        await using (var context = database.Open())
        {
            var accessor = new FixedAccessor("tenant-a");
            owner = await BackgroundActivityWorkTestSupport.SeedLiveExecutionAsync(context, accessor, "workflow-a", "tenant-a", new FixedTimeProvider(Now));
            await AdmitAsync(context, accessor, owner, "admit-pending", request);
            Assert.True(await ExecutionStore(context, accessor).DeleteAsync("workflow-a"));
        }

        await using var verify = database.Open();
        var worker = new EfBackgroundActivityWorkStore(verify, new FixedAccessor("tenant-a"));
        var item = await worker.FindAsync("workflow-a", request.WorkId);
        Assert.Equal(BackgroundActivityWorkStatus.Cancelled, item!.Status);
        Assert.True(item.ClaimFence > 0);
        Assert.Empty(await worker.ListResultReadyAsync(10));
        Assert.Null(await worker.ClaimNextAsync("worker", Now, TimeSpan.FromMinutes(1)));
        var ownership = await new EfExecutionLivenessStateStore(verify, new FixedAccessor("tenant-a"), new NoopContinuationCodec())
            .FindAsync("workflow-a", RuntimeExecutionOwnershipStateId.For("workflow-a"));
        Assert.Null(ownership!.ExecutionLease);
        Assert.Equal(owner.FencingToken.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ownership.Metadata[Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.OwnershipFencingToken]);
    }

    [Fact]
    public async Task Deleting_while_claimed_fences_the_worker_and_hides_ready_receipts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = Request("work-claimed");
        BackgroundActivityWorkClaim claim;
        await using (var context = database.Open())
        {
            var accessor = new FixedAccessor("tenant-a");
            var owner = await BackgroundActivityWorkTestSupport.SeedLiveExecutionAsync(context, accessor, "workflow-a", "tenant-a", new FixedTimeProvider(Now));
            await AdmitAsync(context, accessor, owner, "admit-claimed", request);
            var worker = new EfBackgroundActivityWorkStore(context, accessor);
            claim = (await worker.ClaimNextAsync("worker", Now, TimeSpan.FromMinutes(1)))!;
            Assert.NotNull(claim);
            Assert.True(await ExecutionStore(context, accessor).DeleteAsync("workflow-a"));
            var cancelled = await worker.FindAsync("workflow-a", request.WorkId);
            Assert.Equal(BackgroundActivityWorkStatus.Cancelled, cancelled!.Status);
            Assert.True(cancelled.ClaimFence > claim.ClaimFence);
            Assert.Empty(await worker.ListResultReadyAsync(10));
        }

        await using var verify = database.Open();
        await Assert.ThrowsAsync<BackgroundActivityWorkConflictException>(() =>
            new EfBackgroundActivityWorkStore(verify, new FixedAccessor("tenant-a"))
                .RecordResultAsync(claim, Complete("{}"), Now.AddSeconds(1)).AsTask());
    }

    [Fact]
    public async Task Deletion_cancels_ready_receipts_but_leaves_accepted_receipts_immutable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var readyRequest = Request("work-ready");
        var acceptedRequest = Request("work-accepted");
        string readyDigest;
        RuntimeExecutionLease owner;
        await using (var context = database.Open())
        {
            var accessor = new FixedAccessor("tenant-a");
            owner = await BackgroundActivityWorkTestSupport.SeedLiveExecutionAsync(context, accessor, "workflow-a", "tenant-a", new FixedTimeProvider(Now));
            await AdmitAsync(context, accessor, owner, "admit-ready-and-accepted", readyRequest, acceptedRequest);
            var worker = new EfBackgroundActivityWorkStore(context, accessor);
            BackgroundActivityWorkItem ready = null!;
            BackgroundActivityWorkItem accepted = null!;
            foreach (var expected in new[] { readyRequest, acceptedRequest })
            {
                var claim = (await worker.ClaimNextAsync($"worker-{expected.WorkId}", Now.AddSeconds(1), TimeSpan.FromMinutes(1)))!;
                Assert.NotNull(claim);
                var receipt = await worker.RecordResultAsync(claim, Complete($"{{\"work\":\"{expected.WorkId}\"}}"), Now.AddSeconds(2));
                if (expected.WorkId == readyRequest.WorkId)
                    ready = receipt;
                else
                    accepted = receipt;
            }

            readyDigest = ready.ReceiptDigest!;
            var accept = Commit("accept-before-delete") with
            {
                ExpectedFence = owner.ToFence(),
                StateChanges = Commit("accept-before-delete").StateChanges.WithBackgroundActivityWork(
                    new BackgroundActivityWorkCheckpointChanges(acceptances:
                    [new BackgroundActivityWorkAcceptance("workflow-a", acceptedRequest.WorkId, accepted.ReceiptDigest!, Now.AddSeconds(3))]))
            };
            await new EfRuntimeCheckpointCommitStore(context, accessor).CommitAsync(accept, Decision());
            Assert.True(await ExecutionStore(context, accessor).DeleteAsync("workflow-a"));
        }

        await using var verify = database.Open();
        var work = new EfBackgroundActivityWorkStore(verify, new FixedAccessor("tenant-a"));
        var readyAfterDelete = await work.FindAsync("workflow-a", readyRequest.WorkId);
        var acceptedAfterDelete = await work.FindAsync("workflow-a", acceptedRequest.WorkId);
        Assert.Equal(BackgroundActivityWorkStatus.Cancelled, readyAfterDelete!.Status);
        Assert.Equal(readyDigest, readyAfterDelete.ReceiptDigest);
        Assert.Equal(BackgroundActivityWorkStatus.Accepted, acceptedAfterDelete!.Status);
        Assert.Empty(await work.ListResultReadyAsync(10));
    }

    [Fact]
    public async Task Deletion_is_scope_local_and_stale_or_new_owner_admission_cannot_create_orphan_work()
    {
        await using var database = await TestDatabase.CreateAsync();
        RuntimeExecutionLease tenantAOwner;
        RuntimeExecutionLease tenantBOwner;
        var tenantARequest = Request("work-scope-a");
        var tenantBRequest = Request("work-scope-b");

        await using (var context = database.Open())
        {
            var accessor = new FixedAccessor("tenant-a");
            tenantAOwner = await BackgroundActivityWorkTestSupport.SeedLiveExecutionAsync(context, accessor, "workflow-a", "tenant-a", new FixedTimeProvider(Now), ownerId: "owner-a");
            await AdmitAsync(context, accessor, tenantAOwner, "admit-scope-a", tenantARequest);
        }
        await using (var context = database.Open())
        {
            var accessor = new FixedAccessor("tenant-b");
            tenantBOwner = await BackgroundActivityWorkTestSupport.SeedLiveExecutionAsync(context, accessor, "workflow-a", "tenant-b", new FixedTimeProvider(Now), ownerId: "owner-b");
            await AdmitAsync(context, accessor, tenantBOwner, "admit-scope-b", tenantBRequest);
        }

        await using (var context = database.Open())
        {
            Assert.True(await ExecutionStore(context, new FixedAccessor("tenant-a")).DeleteAsync("workflow-a"));
            var tenantA = new EfBackgroundActivityWorkStore(context, new FixedAccessor("tenant-a"));
            Assert.Equal(BackgroundActivityWorkStatus.Cancelled, (await tenantA.FindAsync("workflow-a", tenantARequest.WorkId))!.Status);
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() =>
                new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(Now))
                    .CommitAsync(AdmissionCommit("admit-stale-after-delete", Request("work-stale"), tenantAOwner), Decision()).AsTask());
        }

        await using (var context = database.Open())
        {
            var accessor = new FixedAccessor("tenant-a");
            var replacementOwner = await new RuntimeExecutionOwnershipService(
                new EfExecutionLivenessStateStore(context, accessor, new NoopContinuationCodec()),
                new FixedTimeProvider(Now),
                new RuntimeExecutionOwnershipOptions { OwnerId = "replacement-owner", LeaseDuration = TimeSpan.FromHours(1) })
                .AcquireAsync("workflow-a");
            var replacementOwnership = await new EfExecutionLivenessStateStore(context, accessor, new NoopContinuationCodec())
                .FindAsync("workflow-a", RuntimeExecutionOwnershipStateId.For("workflow-a"));
            Assert.Equal("true", replacementOwnership!.Metadata[RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone]);
            await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() =>
                new EfRuntimeCheckpointCommitStore(context, accessor, new FixedTimeProvider(Now))
                    .CommitAsync(AdmissionCommit("admit-orphan-after-delete", Request("work-orphan"), replacementOwner), Decision()).AsTask());

            var recreatedExecution = new WorkflowExecutionState(
                "workflow-a",
                new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
                WorkflowExecutionStatus.Running,
                null,
                Now,
                Now,
                Now,
                null,
                null,
                null,
                "tenant-a",
                new Dictionary<string, string>());
            var recreateAndAdmit = AdmissionCommit("admit-orphan-with-upsert", Request("work-orphan-upsert"), replacementOwner) with
            {
                StateChanges = new RuntimeCheckpointStateChangeSet(
                    new RuntimeStateChange<WorkflowExecutionState>(
                        "workflow-a", RuntimeStateChangeOperation.Upsert, recreatedExecution, new Dictionary<string, string>()),
                    null, [], [], [], [], []).WithBackgroundActivityWork(
                    new BackgroundActivityWorkCheckpointChanges(admissions: [Request("work-orphan-upsert")]))
            };
            await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() =>
                new EfRuntimeCheckpointCommitStore(context, accessor, new FixedTimeProvider(Now), new PassThroughRootWriteLeaseManager())
                    .CommitAsync(recreateAndAdmit, Decision()).AsTask());
            Assert.Null(await ExecutionStore(context, accessor).FindAsync("workflow-a"));
            Assert.Null(await new EfBackgroundActivityWorkStore(context, accessor).FindAsync("workflow-a", "work-orphan-upsert"));
        }

        await using var tenantBContext = database.Open();
        var tenantB = new EfBackgroundActivityWorkStore(tenantBContext, new FixedAccessor("tenant-b"));
        Assert.Equal(BackgroundActivityWorkStatus.Pending, (await tenantB.FindAsync("workflow-a", tenantBRequest.WorkId))!.Status);
        Assert.NotNull(await tenantB.ClaimNextAsync("tenant-b-worker", Now, TimeSpan.FromMinutes(1)));
        Assert.Equal(tenantBOwner.FencingToken, (await new EfExecutionLivenessStateStore(
            tenantBContext, new FixedAccessor("tenant-b"), new NoopContinuationCodec())
            .FindAsync("workflow-a", RuntimeExecutionOwnershipStateId.For("workflow-a")))!.ExecutionLease!.FencingToken);
    }

    private static async Task AdmitAsync(
        RuntimeDbContext context,
        IPersistenceAccessContextAccessor accessor,
        RuntimeExecutionLease owner,
        string commitId,
        params BackgroundActivityWorkRequest[] requests) =>
        await new EfRuntimeCheckpointCommitStore(context, accessor, new FixedTimeProvider(Now)).CommitAsync(
            AdmissionCommit(commitId, requests[0], owner) with
            {
                StateChanges = Commit(commitId).StateChanges.WithBackgroundActivityWork(
                    new BackgroundActivityWorkCheckpointChanges(admissions: requests))
            },
            Decision());

    private static RuntimeCheckpointCommit AdmissionCommit(string commitId, BackgroundActivityWorkRequest request, RuntimeExecutionLease owner) =>
        Commit(commitId) with
        {
            ExpectedFence = owner.ToFence(),
            StateChanges = Commit(commitId).StateChanges.WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: [request]))
        };

    private static EfWorkflowExecutionStateStore ExecutionStore(RuntimeDbContext context, IPersistenceAccessContextAccessor accessor) =>
        new(context, accessor, new NoopContinuationCodec(), new FixedTimeProvider(Now));

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
        new RuntimeCheckpoint($"checkpoint-{commitId}", "BackgroundActivityWorkDeletion", "workflow-a", Now, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []), [], new Dictionary<string, string>());

    private static RuntimeCheckpointPersistenceDecision Decision() => new(RuntimeCheckpointPersistenceMode.Immediate);

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopContinuationCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }

    private sealed class PassThroughRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(
            string artifactId,
            string leaseId,
            Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) => write(cancellationToken);
    }

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:bg-delete-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(keeper).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper, connectionString);
        }

        public RuntimeSqliteDbContext Open() =>
            new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connectionString).Options);

        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }
}
