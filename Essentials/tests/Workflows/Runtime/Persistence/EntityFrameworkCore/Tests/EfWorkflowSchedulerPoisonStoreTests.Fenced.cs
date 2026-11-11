using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// #4032: a poison record written under the run's ownership fence. The fence check, the touch of the ownership row
/// and the record are one transaction, so a node that lost the run writes nothing.
/// </summary>
public sealed partial class EfWorkflowSchedulerPoisonStoreTests
{
    private const string OwnedWorkflow = "workflow-1";

    [Fact]
    public async Task Fenced_record_with_the_current_fence_inserts_and_replaces()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var store = FencedStore(fixture, clock);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        var before = await OwnershipRevisionAsync(database);

        Assert.True(store.CanRecordFenced);
        await store.RecordFencedAsync(Record(1), lease.ToFence());
        await store.RecordFencedAsync(Record(1, "replaced", failureCount: 2), lease.ToFence());

        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        await using var read = database.Open("tenant-a");
        var found = await read.Store.FindAsync(OwnedWorkflow, "work-1");
        Assert.NotNull(found);
        Assert.Equal("replaced", found!.Fault.Message);
        Assert.Equal(2, found.FailureCount);
        Assert.Equal(2, (await read.Context.WorkflowSchedulerPoisonRecords.AsNoTracking().SingleAsync()).Revision);
        // Each fenced record touches the ownership row once.
        Assert.Equal(before + 2, await OwnershipRevisionAsync(database));
        // The lease itself is unchanged, so the owner's next heartbeat still passes.
        Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Applied, (await Node(fixture, "node-a", clock).HeartbeatAsync(lease)).Status);
    }

    [Theory]
    [InlineData("taken-over", RuntimeFencingRejectionReason.StaleToken)]
    [InlineData("expired", RuntimeFencingRejectionReason.ExpiredLease)]
    [InlineData("released", RuntimeFencingRejectionReason.NoActiveLease)]
    [InlineData("never-owned", RuntimeFencingRejectionReason.NoActiveLease)]
    public async Task Fenced_record_with_a_stale_fence_writes_nothing_and_leaves_the_context_usable(
        string staleBecause,
        RuntimeFencingRejectionReason expectedReason)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var store = FencedStore(fixture, clock);
        var fence = new RuntimeExecutionFence("lease-never", "node-a", 1);
        if (staleBecause != "never-owned")
        {
            var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
            fence = lease.ToFence();
            if (staleBecause == "taken-over")
                await Node(fixture, "node-b", clock).AcquireAsync(OwnedWorkflow);
            else if (staleBecause == "expired")
                clock.Advance(TimeSpan.FromMinutes(2));
            else
                await Node(fixture, "node-a", clock).ReleaseAsync(lease);
        }
        var before = await OwnershipRevisionAsync(database);

        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => store.RecordFencedAsync(Record(1), fence).AsTask());

        Assert.Equal(expectedReason, refusal.Reason);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        Assert.Null(fixture.Context.Database.CurrentTransaction);
        // A later participant on the same context writes its own row and nothing of the refused record.
        fixture.Context.SchedulerStates.Add(SiblingRow());
        Assert.Equal(1, await fixture.Context.SaveChangesAsync());
        await using var read = database.Open("tenant-a");
        Assert.Null(await read.Store.FindAsync(OwnedWorkflow, "work-1"));
        Assert.Equal(before, await OwnershipRevisionAsync(database));
    }

    [Fact]
    public async Task Fenced_record_is_retried_when_the_owners_heartbeat_lands_between_the_fence_read_and_the_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        var race = new OwnershipRowRace();
        await using var fixture = database.Open("tenant-a", race);
        await using var otherConnection = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        var before = await OwnershipRevisionAsync(database);
        race.Change = async () => Assert.Equal(
            RuntimeExecutionOwnershipTransitionStatus.Applied,
            (await Node(otherConnection, "node-a", clock).HeartbeatAsync(lease)).Status);

        await FencedStore(fixture, clock).RecordFencedAsync(Record(1), lease.ToFence());

        Assert.Equal(1, race.Races);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        await using var read = database.Open("tenant-a");
        Assert.NotNull(await read.Store.FindAsync(OwnedWorkflow, "work-1"));
        // The heartbeat, then the fence touch of the attempt that succeeded.
        Assert.Equal(before + 2, await OwnershipRevisionAsync(database));
    }

    [Fact]
    public async Task Fenced_record_is_refused_when_another_node_takes_the_run_between_the_fence_read_and_the_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        var race = new OwnershipRowRace();
        await using var fixture = database.Open("tenant-a", race);
        await using var otherConnection = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        var before = await OwnershipRevisionAsync(database);
        race.Change = async () => await Node(otherConnection, "node-b", clock).AcquireAsync(OwnedWorkflow);

        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => FencedStore(fixture, clock).RecordFencedAsync(Record(1), lease.ToFence()).AsTask());

        Assert.Equal(RuntimeFencingRejectionReason.StaleToken, refusal.Reason);
        Assert.Equal(1, race.Races);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        await using var read = database.Open("tenant-a");
        Assert.Null(await read.Store.FindAsync(OwnedWorkflow, "work-1"));
        // Only the takeover wrote the ownership row.
        Assert.Equal(before + 1, await OwnershipRevisionAsync(database));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fenced_record_with_caller_staged_changes_does_not_retry_a_fence_conflict_and_says_whether_the_run_was_taken(bool takenOver)
    {
        // With other changes pending on the context the conflict is not retried. Before it leaves, the ownership row
        // is read once more: the owner's own heartbeat leaves as the write failure, a takeover leaves as stale. A
        // takeover that left as a write failure would be recorded without the fence by the drainer.
        await using var database = await TestDatabase.CreateAsync();
        var race = new OwnershipRowRace();
        await using var fixture = database.Open("tenant-a", race);
        await using var otherConnection = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        fixture.Context.SchedulerStates.Add(SiblingRow());
        race.Change = takenOver
            ? async () => await Node(otherConnection, "node-b", clock).AcquireAsync(OwnedWorkflow)
            : async () => await Node(otherConnection, "node-a", clock).HeartbeatAsync(lease);
        var store = FencedStore(fixture, clock);

        if (takenOver)
            Assert.Equal(RuntimeFencingRejectionReason.StaleToken, (await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
                () => store.RecordFencedAsync(Record(1), lease.ToFence()).AsTask())).Reason);
        else
            Assert.IsType<DbUpdateConcurrencyException>((await Assert.ThrowsAsync<EfPoisonStoreException>(
                () => store.RecordFencedAsync(Record(1), lease.ToFence()).AsTask())).InnerException);

        Assert.Equal(1, race.Races);
        await using var read = database.Open("tenant-a");
        Assert.Null(await read.Store.FindAsync(OwnedWorkflow, "work-1"));
        Assert.Empty(await read.Context.SchedulerStates.ToArrayAsync());
    }

    [Fact]
    public async Task Fenced_record_refuses_as_stale_a_takeover_that_lands_under_the_last_attempt()
    {
        // The owner's heartbeat lands under the first two attempts and another node takes the run under the third.
        // No attempt is left to read the fence again, so the conflict is checked before it leaves.
        await using var database = await TestDatabase.CreateAsync();
        var race = new OwnershipRowRace { MaxRaces = EfRuntimeCheckpointParticipantStaging.MaxFenceRenewalAttempts };
        await using var fixture = database.Open("tenant-a", race);
        await using var otherConnection = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        var changes = 0;
        race.Change = async () =>
        {
            if (++changes < EfRuntimeCheckpointParticipantStaging.MaxFenceRenewalAttempts)
                await Node(otherConnection, "node-a", clock).HeartbeatAsync(lease);
            else
                await Node(otherConnection, "node-b", clock).AcquireAsync(OwnedWorkflow);
        };

        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => FencedStore(fixture, clock).RecordFencedAsync(Record(1), lease.ToFence()).AsTask());

        Assert.Equal(RuntimeFencingRejectionReason.StaleToken, refusal.Reason);
        Assert.Equal(EfRuntimeCheckpointParticipantStaging.MaxFenceRenewalAttempts, race.Races);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        await using var read = database.Open("tenant-a");
        Assert.Null(await read.Store.FindAsync(OwnedWorkflow, "work-1"));
    }

    [Theory]
    [InlineData("taken-over")]
    [InlineData("still-owned")]
    [InlineData("second-look-fails")]
    [InlineData("second-look-cancelled")]
    [InlineData("timeout-reported-as-cancellation")]
    public async Task Fenced_record_looks_at_the_ownership_row_again_before_any_provider_failure_leaves(string then)
    {
        // Not every takeover shows as a conflict on the ownership row. Under a stricter isolation level it is a
        // serialization failure, and a lock timeout on that row has the same shape: a plain provider failure of the
        // save. Left as a write failure, it would be recorded without the fence by the drainer. So the row is read
        // once more before the failure leaves. A lease that is still this one leaves as the failure. When that read
        // fails too, the first failure is the one that leaves. Only a cancellation of this call's own token leaves
        // as a cancellation; a provider that reports a timeout as an OperationCanceledException has failed.
        await using var database = await TestDatabase.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var failure = new ProviderSaveFailure();
        await using var fixture = database.Open("tenant-a", failure);
        await using var otherConnection = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        switch (then)
        {
            case "taken-over":
                failure.AfterRollback = async () => await Node(otherConnection, "node-b", clock).AcquireAsync(OwnedWorkflow);
                break;
            case "second-look-fails":
                failure.SecondLookFailure = () => new SimulatedProviderFailure("Simulated failure of the ownership read.");
                break;
            case "second-look-cancelled":
                failure.SecondLookFailure = () =>
                {
                    cancellation.Cancel();
                    return new OperationCanceledException(cancellation.Token);
                };
                break;
            case "timeout-reported-as-cancellation":
                failure.SaveFailure = () => new TaskCanceledException(ProviderSaveFailure.Message);
                break;
        }
        failure.Armed = true;
        var store = FencedStore(fixture, clock);
        Func<Task> record = () => store.RecordFencedAsync(Record(1), lease.ToFence(), cancellation.Token).AsTask();

        if (then == "taken-over")
        {
            Assert.Equal(RuntimeFencingRejectionReason.StaleToken, (await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(record)).Reason);
        }
        else if (then == "second-look-cancelled")
        {
            await Assert.ThrowsAsync<OperationCanceledException>(record);
        }
        else
        {
            var wrapped = await Assert.ThrowsAsync<EfPoisonStoreException>(record);
            Assert.Equal(
                then == "timeout-reported-as-cancellation" ? typeof(TaskCanceledException) : typeof(DbUpdateException),
                wrapped.InnerException!.GetType());
            Assert.Contains(ProviderSaveFailure.Message, wrapped.InnerException.ToString());
        }

        Assert.Equal(then is "second-look-fails" or "second-look-cancelled" ? 1 : 0, failure.FailedSecondLooks);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        await using var read = database.Open("tenant-a");
        Assert.Null(await read.Store.FindAsync(OwnedWorkflow, "work-1"));
    }

    [Fact]
    public async Task Fenced_record_whose_commit_fails_leaves_the_callers_other_changes_pending()
    {
        // The save inside the record's transaction would mark everything the caller had pending as saved. A commit
        // that then fails rolls all of it back, so those changes must still be pending: the unfenced record that the
        // drainer falls back to saves them, as it did before there was a fenced record.
        await using var database = await TestDatabase.CreateAsync();
        var commit = new CommitFailure();
        await using var fixture = database.Open("tenant-a", commit);
        var clock = new FakeTimeProvider(Now);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        var sibling = SiblingRow();
        fixture.Context.SchedulerStates.Add(sibling);
        commit.Armed = true;

        var wrapped = await Assert.ThrowsAsync<EfPoisonStoreException>(
            () => FencedStore(fixture, clock).RecordFencedAsync(Record(1), lease.ToFence()).AsTask());

        Assert.IsType<SimulatedProviderFailure>(wrapped.InnerException);
        Assert.Equal(EntityState.Added, fixture.Context.Entry(sibling).State);
        Assert.Same(sibling, Assert.Single(fixture.Context.ChangeTracker.Entries()).Entity);
        await fixture.Store.RecordAsync(Record(1));
        await using var read = database.Open("tenant-a");
        Assert.NotNull(await read.Store.FindAsync(OwnedWorkflow, "work-1"));
        Assert.Single(await read.Context.SchedulerStates.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fenced_record_that_loses_a_race_on_the_poison_row_fails_at_the_store_boundary_and_writes_nothing(bool rowExists)
    {
        // Another writer of the same poison row: it replaced the row between this record's read and its save, or it
        // inserted the row first. Neither is a conflict on the ownership row, so neither is retried or taken for a
        // lost lease. Both leave as the store's persistence failure, around the provider's exception.
        await using var database = await TestDatabase.CreateAsync();
        var race = new PoisonRowRace();
        await using var fixture = database.Open("tenant-a", race);
        var clock = new FakeTimeProvider(Now);
        var store = FencedStore(fixture, clock);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        if (rowExists)
            await store.RecordFencedAsync(Record(1), lease.ToFence());
        var before = await OwnershipRevisionAsync(database);
        race.Armed = true;

        var failure = await Assert.ThrowsAsync<EfPoisonStoreException>(
            () => store.RecordFencedAsync(Record(1, "second", failureCount: 2), lease.ToFence()).AsTask());

        Assert.Equal(rowExists ? typeof(DbUpdateConcurrencyException) : typeof(DbUpdateException), failure.InnerException!.GetType());
        Assert.Equal($"{OwnedWorkflow}/work-1", failure.Identity);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        await using var read = database.Open("tenant-a");
        Assert.Equal(rowExists ? 1 : null, (await read.Store.FindAsync(OwnedWorkflow, "work-1"))?.FailureCount);
        Assert.Equal(before, await OwnershipRevisionAsync(database));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(5, false)]
    public async Task Fenced_record_runs_at_most_the_attempt_limit_when_the_owners_heartbeat_keeps_landing(int heartbeats, bool recorded)
    {
        // The limit counts attempts, the first included. A heartbeat under each of the first two leaves the third to
        // succeed; a heartbeat under every attempt ends the call with the conflict, wrapped at the store's boundary.
        await using var database = await TestDatabase.CreateAsync();
        var race = new OwnershipRowRace { MaxRaces = heartbeats };
        await using var fixture = database.Open("tenant-a", race);
        await using var otherConnection = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        race.Change = async () => await Node(otherConnection, "node-a", clock).HeartbeatAsync(lease);
        var store = FencedStore(fixture, clock);

        if (recorded)
            await store.RecordFencedAsync(Record(1), lease.ToFence());
        else
            Assert.IsType<DbUpdateConcurrencyException>((await Assert.ThrowsAsync<EfPoisonStoreException>(
                () => store.RecordFencedAsync(Record(1), lease.ToFence()).AsTask())).InnerException);

        Assert.Equal(Math.Min(heartbeats, EfRuntimeCheckpointParticipantStaging.MaxFenceRenewalAttempts), race.Races);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        await using var read = database.Open("tenant-a");
        Assert.Equal(recorded, await read.Store.FindAsync(OwnedWorkflow, "work-1") is not null);
    }

    [Fact]
    public async Task Fenced_record_inside_a_caller_transaction_is_refused_and_writes_nothing()
    {
        // The fence check and the record are a transaction of the store's own, as a checkpoint commit is. EF Core
        // does not begin one while the context has another; that refusal leaves as the store's persistence failure.
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var store = FencedStore(fixture, clock);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        var before = await OwnershipRevisionAsync(database);

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<EfPoisonStoreException>(() => store.RecordFencedAsync(Record(1), lease.ToFence()).AsTask());
            // Nothing is staged for the caller's commit to write.
            Assert.False(fixture.Context.ChangeTracker.HasChanges());
            await transaction.CommitAsync();
        }

        await using var read = database.Open("tenant-a");
        Assert.Null(await read.Store.FindAsync(OwnedWorkflow, "work-1"));
        Assert.Equal(before, await OwnershipRevisionAsync(database));
    }

    [Fact]
    public async Task Fenced_record_refuses_a_pending_change_to_the_runs_ownership_row_and_leaves_it_pending()
    {
        // Someone staged a change to the ownership row on the shared context and has not saved it. Checking the fence
        // against those unsaved values, and saving them with the record, would both be wrong.
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        var before = await OwnershipRevisionAsync(database);
        var pending = fixture.Context.Entry(await fixture.Context.ExecutionLivenessStates.SingleAsync());
        pending.State = EntityState.Modified;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => FencedStore(fixture, clock).RecordFencedAsync(Record(1), lease.ToFence()).AsTask());

        Assert.Equal(EntityState.Modified, pending.State);
        Assert.Null(fixture.Context.Database.CurrentTransaction);
        await using var read = database.Open("tenant-a");
        Assert.Null(await read.Store.FindAsync(OwnedWorkflow, "work-1"));
        Assert.Equal(before, await OwnershipRevisionAsync(database));
    }

    [Fact]
    public async Task Fenced_record_reads_the_fence_row_as_it_is_now_when_the_context_tracks_an_older_copy()
    {
        // A checkpoint commit leaves the ownership row tracked on the shared context. The owner's heartbeat then
        // moves the row on from another scope. The record must check the row, not that copy: saving against the
        // copy's revision would cost a failed attempt first, so one save is the evidence.
        await using var database = await TestDatabase.CreateAsync();
        var saves = new SaveCounter();
        await using var fixture = database.Open("tenant-a", saves);
        await using var otherConnection = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);
        _ = await fixture.Context.ExecutionLivenessStates.SingleAsync();
        await Node(otherConnection, "node-a", clock).HeartbeatAsync(lease);
        saves.Count = 0;

        await FencedStore(fixture, clock).RecordFencedAsync(Record(1), lease.ToFence());

        Assert.Equal(1, saves.Count);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());
        await using var read = database.Open("tenant-a");
        Assert.NotNull(await read.Store.FindAsync(OwnedWorkflow, "work-1"));
    }

    [Fact]
    public async Task Can_record_fenced_only_when_ownership_is_the_ef_store_over_the_same_context_and_scope()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await using var other = database.Open("tenant-a");
        var codec = new PassThroughCodec();
        bool CanRecordFencedWith(IExecutionLivenessStateStore? ownership) =>
            new EfWorkflowSchedulerPoisonStore(fixture.Context, fixture.Accessor, () => ownership).CanRecordFenced;

        Assert.True(CanRecordFencedWith(new EfExecutionLivenessStateStore(fixture.Context, fixture.Accessor, codec)));
        // The constructor that existed before the capability, and a composition with no ownership store.
        Assert.False(fixture.Store.CanRecordFenced);
        Assert.False(CanRecordFencedWith(null));
        // Ownership kept in memory, in another context (another database or unit of work), or under another access scope.
        Assert.False(CanRecordFencedWith(new InMemoryExecutionLivenessStateStore()));
        Assert.False(CanRecordFencedWith(new EfExecutionLivenessStateStore(other.Context, fixture.Accessor, codec)));
        Assert.False(CanRecordFencedWith(new EfExecutionLivenessStateStore(fixture.Context, new FixedAccessor("tenant-a"), codec)));
    }

    [Fact]
    public async Task The_ownership_store_is_not_looked_up_until_a_fenced_record_is_asked_for()
    {
        // Reading and unfenced recording must keep working in a composition whose ownership store cannot be resolved
        // where the poison store is used, as before the capability existed.
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var lookups = 0;
        var store = new EfWorkflowSchedulerPoisonStore(fixture.Context, fixture.Accessor, () =>
        {
            lookups++;
            throw new InvalidOperationException("The ownership store cannot be resolved here.");
        });

        await store.RecordAsync(Record(1));
        Assert.NotNull(await store.FindAsync(OwnedWorkflow, "work-1"));
        Assert.Single(await store.ListAsync(OwnedWorkflow));

        Assert.Equal(0, lookups);
    }

    [Fact]
    public async Task Fenced_record_on_a_store_that_cannot_check_the_fence_is_not_supported_and_writes_nothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var clock = new FakeTimeProvider(Now);
        var lease = await Node(fixture, "node-a", clock).AcquireAsync(OwnedWorkflow);

        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Store.RecordFencedAsync(Record(1), lease.ToFence()).AsTask());

        Assert.Null(await fixture.Store.FindAsync(OwnedWorkflow, "work-1"));
    }

    [Theory]
    [InlineData("ef")]
    [InlineData("in-memory")]
    [InlineData("none")]
    public async Task Registration_fences_poison_only_when_the_registered_ownership_store_is_the_ef_one(string ownership)
    {
        var connectionString = $"Data Source=file:ef-r23-fenced-registration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new FixedAccessor("tenant-a"));
        services.AddRuntimeSchedulerPoisonEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = connectionString });
        if (ownership == "ef")
            services.AddRuntimeOperationalStateEntityFrameworkCore(new()
            {
                Provider = "Sqlite",
                ConnectionString = connectionString,
                RecoveryContinuationSigningKey = "fenced-poison-recovery-signing-key-32-bytes"
            });
        else if (ownership == "in-memory")
            services.AddSingleton<IExecutionLivenessStateStore, InMemoryExecutionLivenessStateStore>();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var store = Assert.IsType<EfWorkflowSchedulerPoisonStore>(scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>());

        Assert.Equal(ownership == "ef", store.CanRecordFenced);
    }

    private static EfWorkflowSchedulerPoisonStore FencedStore(Fixture fixture, TimeProvider clock) =>
        new(fixture.Context, fixture.Accessor, () => new EfExecutionLivenessStateStore(fixture.Context, fixture.Accessor, new PassThroughCodec()), clock);

    private static RuntimeExecutionOwnershipService Node(Fixture fixture, string ownerId, TimeProvider clock) =>
        new(new EfExecutionLivenessStateStore(fixture.Context, fixture.Accessor, new PassThroughCodec()), clock,
            new RuntimeExecutionOwnershipOptions { OwnerId = ownerId });

    private static async Task<long?> OwnershipRevisionAsync(TestDatabase database)
    {
        await using var read = database.Open("tenant-a");
        return (await read.Context.ExecutionLivenessStates.AsNoTracking().SingleOrDefaultAsync())?.Revision;
    }

    private static SchedulerStateEntity SiblingRow() => new()
    {
        Id = "sibling-row",
        ScopeKey = EfRelationalIdentity.Encode("tenant-a"),
        ScopeKeyHash = EfRelationalIdentity.Hash("tenant-a"),
        WorkflowExecutionId = EfRelationalIdentity.Encode("workflow-sibling"),
        WorkflowExecutionIdHash = EfRelationalIdentity.Hash("workflow-sibling"),
        WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey("workflow-sibling", RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        Collection = "schedulerState",
        ContentJson = "{}",
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private sealed class PassThroughCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }

    // Another writer of the poison row. SQLite cannot run two write transactions at once, so neither race is a second
    // connection's commit. A replaced row is modelled as a superseded revision in this save. A row inserted first is
    // inserted for real, through a second context inside this record's own transaction, so the provider reports its
    // unique-key violation.
    private sealed class PoisonRowRace : ISaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var staged = eventData.Context!.ChangeTracker.Entries<WorkflowSchedulerPoisonEntity>().SingleOrDefault();
            if (!Armed || staged is null)
                return result;

            Armed = false;
            if (staged.State == EntityState.Modified)
            {
                staged.Property(row => row.Revision).OriginalValue = staged.Property(row => row.Revision).OriginalValue - 1;
                return result;
            }

            var database = eventData.Context.Database;
            await using var other = new RuntimeSqliteDbContext(
                new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(database.GetDbConnection()).Options);
            await other.Database.UseTransactionAsync(database.CurrentTransaction!.GetDbTransaction(), cancellationToken);
            var first = new WorkflowSchedulerPoisonEntity();
            other.Entry(first).CurrentValues.SetValues(staged.Entity);
            other.WorkflowSchedulerPoisonRecords.Add(first);
            await other.SaveChangesAsync(cancellationToken);
            return result;
        }
    }

    // A provider failure of the record's save that is not an optimistic-concurrency conflict, as a serialization failure
    // or a lock timeout is. Another node's write can then land once the failed attempt has rolled back, and the
    // store's next read of the ownership row can be made to fail as well.
    private sealed class ProviderSaveFailure : DbCommandInterceptor, IDbTransactionInterceptor
    {
        public const string Message = "Simulated serialization failure.";
        private bool failed;
        private bool rolledBack;
        public bool Armed { get; set; }
        public Func<Exception> SaveFailure { get; set; } = () => new DbUpdateException(Message);
        public Func<Exception>? SecondLookFailure { get; set; }
        public Func<Task>? AfterRollback { get; set; }
        public int FailedSecondLooks { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ThrowOnPoisonInsert(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            ThrowOnPoisonInsert(command);
            if (rolledBack && SecondLookFailure is not null && command.Transaction is null &&
                command.CommandText.Contains(RuntimeOperationalStateEfModule.ExecutionLivenessTableName, StringComparison.OrdinalIgnoreCase))
            {
                FailedSecondLooks++;
                throw SecondLookFailure();
            }
            return ValueTask.FromResult(result);
        }

        public async Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!failed || rolledBack)
                return;
            rolledBack = true;
            if (AfterRollback is not null)
                await AfterRollback();
        }

        private void ThrowOnPoisonInsert(DbCommand command)
        {
            if (!Armed || failed ||
                !command.CommandText.Contains("elsa_runtime_scheduler_poison", StringComparison.OrdinalIgnoreCase) ||
                !command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
                return;
            failed = true;
            throw SaveFailure();
        }
    }

    // Fails the commit of the record's transaction, after its save went through.
    private sealed class CommitFailure : IDbTransactionInterceptor
    {
        public bool Armed { get; set; }

        public ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!Armed)
                return ValueTask.FromResult(result);
            Armed = false;
            throw new SimulatedProviderFailure("Simulated failure of the commit.");
        }
    }

    private sealed class SimulatedProviderFailure(string message) : DbException(message);

    private sealed class SaveCounter : ISaveChangesInterceptor
    {
        public int Count { get; set; }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }

    // Another node's write to the ownership row that commits after this record read its fence. SQLite cannot run two
    // write transactions at once, so the first save is made to present a superseded revision and affects no row, and
    // the other node's write runs once that attempt has rolled back, as its commit would have landed by then.
    private sealed class OwnershipRowRace : ISaveChangesInterceptor, IDbTransactionInterceptor
    {
        private bool pending;
        public Func<Task>? Change { get; set; }
        public int MaxRaces { get; init; } = 1;
        public int Races { get; private set; }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var fence = eventData.Context!.ChangeTracker.Entries<ExecutionLivenessStateEntity>().SingleOrDefault(entry => entry.State == EntityState.Modified);
            if (Change is not null && Races < MaxRaces && fence is not null)
            {
                Races++;
                pending = true;
                fence.Property(row => row.Revision).OriginalValue = fence.Property(row => row.Revision).OriginalValue - 1;
            }
            return ValueTask.FromResult(result);
        }

        public async Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!pending)
                return;
            pending = false;
            await Change!();
        }
    }
}
