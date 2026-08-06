using System.Reflection;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class InMemoryCheckpointTransactionCoordinatorTests
{
    [Fact]
    public async Task Begin_validates_null_plan_null_roots_and_null_root_entry()
    {
        var coordinator = new InMemoryCheckpointTransactionCoordinator();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            coordinator.BeginAsync(null!, default, Array.Empty<object?>()).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            coordinator.BeginAsync(Plan(), (IEnumerable<InMemoryCheckpointTransactionRoot>)null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            coordinator.BeginAsync(Plan(), [null!]).AsTask());
    }

    [Fact]
    public async Task Params_overload_discovers_and_deduplicates_optional_roots()
    {
        var events = new List<string>();
        var participant = new TestParticipant("participant", events);
        var coordinator = new InMemoryCheckpointTransactionCoordinator();

        await using var transaction = await coordinator.BeginAsync(Plan(), default, participant, null, participant);
        transaction.Commit();

        Assert.Equal(["capture:participant"], events);
    }

    [Fact]
    public async Task Discovery_ignores_optional_root_null_deduplicates_and_traverses_sources()
    {
        var events = new List<string>();
        var participant = new TestParticipant("participant", events);
        var source = new TestSource(participant, participant);
        var coordinator = new InMemoryCheckpointTransactionCoordinator();

        await using var transaction = await coordinator.BeginAsync(
            Plan(),
            [new("optional", null, Required: false), new("source", source), new("duplicate", participant)]);
        transaction.Commit();

        Assert.Equal(["capture:participant"], events);
    }

    [Fact]
    public async Task Gates_are_acquired_by_monotonic_rank_and_restored_in_reverse_order()
    {
        var events = new List<string>();
        var first = new TestParticipant("first", events);
        var second = new TestParticipant("second", events);
        var coordinator = new InMemoryCheckpointTransactionCoordinator();

        await using var transaction = await coordinator.BeginAsync(Plan(), [new("second", second), new("first", first)]);
        var primary = new InvalidOperationException("apply failed");
        var returned = transaction.Rollback(primary);

        Assert.Same(primary, returned);
        Assert.Equal(["capture:first", "capture:second", "restore:second", "restore:first"], events);
        Assert.True(first.TransactionGate.Rank < second.TransactionGate.Rank);
    }

    [Fact]
    public async Task Required_missing_nonparticipant_or_nested_null_component_fails_before_capture()
    {
        var coordinator = new InMemoryCheckpointTransactionCoordinator();

        var missing = await Assert.ThrowsAsync<InMemoryCheckpointTransactionCompositionException>(async () =>
            await coordinator.BeginAsync(Plan(), [new("missing", null)]));
        var mixed = await Assert.ThrowsAsync<InMemoryCheckpointTransactionCompositionException>(async () =>
            await coordinator.BeginAsync(Plan(), [new("custom", new object())]));
        var nested = await Assert.ThrowsAsync<InMemoryCheckpointTransactionCompositionException>(async () =>
            await coordinator.BeginAsync(Plan(), [new("source", new TestSource([null]))]));

        Assert.Equal("missing", missing.ComponentName);
        Assert.Equal("custom", mixed.ComponentName);
        Assert.Equal("source[0]", nested.ComponentName);
    }

    [Fact]
    public async Task Null_source_enumeration_fails_composition_before_capture()
    {
        var exception = await Assert.ThrowsAsync<InMemoryCheckpointTransactionCompositionException>(() =>
            new InMemoryCheckpointTransactionCoordinator()
                .BeginAsync(Plan(), [new("null-source", new NullTestSource())])
                .AsTask());

        Assert.Equal("null-source", exception.ComponentName);
    }

    [Fact]
    public async Task Nested_transaction_over_the_same_ambient_participants_is_reentrant()
    {
        var events = new List<string>();
        var participant = new TestParticipant("participant", events);
        var coordinator = new InMemoryCheckpointTransactionCoordinator();

        await using var outer = await coordinator.BeginAsync(Plan(), [new("participant", participant)]);
        participant.Write(1);
        await using (var nested = await coordinator.BeginAsync(Plan(), [new("participant", participant)]))
        {
            participant.Write(2);
            nested.Commit();
        }
        outer.Commit();

        Assert.Equal(2, participant.Read());
        Assert.Equal(["capture:participant", "capture:participant"], events);
    }

    [Fact]
    public async Task Nested_transaction_rejects_participant_expansion_without_releasing_outer_lease()
    {
        var first = new TestParticipant("first", []);
        var second = new TestParticipant("second", []);
        var coordinator = new InMemoryCheckpointTransactionCoordinator();

        await using var outer = await coordinator.BeginAsync(Plan(), [new("first", first)]);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.BeginAsync(Plan(), [new("first", first), new("second", second)]));

        first.Write(7);
        outer.Commit();
        Assert.Equal(7, first.Read());
    }

    [Fact]
    public async Task Capture_failure_cleans_up_every_acquired_gate_and_ambient_lease()
    {
        var first = new TestParticipant("first", []);
        var second = new TestParticipant("second", []) { CaptureFailure = new InvalidOperationException("capture failed") };
        var coordinator = new InMemoryCheckpointTransactionCoordinator();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.BeginAsync(Plan(), [new("first", first), new("second", second)]));

        using var firstLease = first.TransactionGate.Enter();
        using var secondLease = second.TransactionGate.Enter();
    }

    [Fact]
    public async Task Cancellation_during_partial_acquire_releases_already_acquired_gates()
    {
        var first = new TestParticipant("first", []);
        var second = new TestParticipant("second", []);
        var coordinator = new InMemoryCheckpointTransactionCoordinator();
        using var secondLease = second.TransactionGate.Enter();
        using var cancellation = new CancellationTokenSource();
        var begin = coordinator.BeginAsync(
            Plan(),
            [new("first", first), new("second", second)],
            cancellation.Token).AsTask();
        await Task.Delay(50);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => begin);

        using var firstLease = first.TransactionGate.Enter();
    }

    [Fact]
    public async Task Concurrent_read_blocks_until_commit_and_never_observes_partial_value()
    {
        var participant = new TestParticipant("participant", []);
        var coordinator = new InMemoryCheckpointTransactionCoordinator();
        await using var transaction = await coordinator.BeginAsync(Plan(), [new("participant", participant)]);
        participant.Write(42);

        Task<int> read;
        using (ExecutionContext.SuppressFlow())
            read = Task.Run(participant.Read);
        await Task.Delay(50);
        Assert.False(read.IsCompleted);

        transaction.Commit();
        Assert.Equal(42, await read.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Overlapping_transactions_complete_without_deadlock()
    {
        var participant = new TestParticipant("participant", []);
        var coordinator = new InMemoryCheckpointTransactionCoordinator();
        await using var first = await coordinator.BeginAsync(Plan(), [new("participant", participant)]);

        Task<InMemoryCheckpointTransactionCoordinator.Transaction> waiting;
        using (ExecutionContext.SuppressFlow())
            waiting = Task.Run(async () => await coordinator.BeginAsync(Plan(), [new("participant", participant)]));
        await Task.Delay(50);
        Assert.False(waiting.IsCompleted);

        first.Commit();
        await using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        second.Commit();
    }

    [Fact]
    public async Task Rollback_is_best_effort_preserves_primary_failure_and_faults_all_enlisted_gates()
    {
        var events = new List<string>();
        var first = new TestParticipant("first", events) { RestoreFailure = new InvalidOperationException("restore first") };
        var second = new TestParticipant("second", events) { RestoreFailure = new InvalidOperationException("restore second") };
        var coordinator = new InMemoryCheckpointTransactionCoordinator();
        await using var transaction = await coordinator.BeginAsync(Plan(), [new("first", first), new("second", second)]);
        var primary = new InvalidOperationException("primary");

        var failure = Assert.IsType<InMemoryCheckpointRollbackException>(transaction.Rollback(primary));

        Assert.Same(primary, failure.PrimaryFailure);
        Assert.Equal(2, failure.RollbackFailures.Count);
        Assert.Equal(["capture:first", "capture:second", "restore:second", "restore:first"], events);
        Assert.Throws<InMemoryCheckpointParticipantFaultedException>(() => first.Read());
        Assert.Throws<InMemoryCheckpointParticipantFaultedException>(() => second.Read());
    }

    [Fact]
    public async Task Exact_participant_isolation_skips_unaffected_capabilities()
    {
        var events = new List<string>();
        var affected = new TestParticipant("affected", events);
        var unaffected = new TestParticipant("unaffected", events) { Affected = false };
        var coordinator = new InMemoryCheckpointTransactionCoordinator();

        await using var transaction = await coordinator.BeginAsync(Plan(), [new("affected", affected), new("unaffected", unaffected)]);
        transaction.Commit();

        Assert.Equal(["capture:affected"], events);
    }

    [Fact]
    public async Task Duplicate_completion_is_rejected_and_rollback_after_completion_preserves_primary()
    {
        var participant = new TestParticipant("participant", []);
        var coordinator = new InMemoryCheckpointTransactionCoordinator();
        await using var transaction = await coordinator.BeginAsync(Plan(), [new("participant", participant)]);
        transaction.Commit();

        Assert.Throws<InvalidOperationException>(transaction.Commit);
        var primary = new InvalidOperationException("late rollback");
        Assert.Same(primary, transaction.Rollback(primary));
    }

    [Fact]
    public async Task Rollback_rejects_null_primary_without_completing_transaction()
    {
        var participant = new TestParticipant("participant", []);
        await using var transaction = await new InMemoryCheckpointTransactionCoordinator()
            .BeginAsync(Plan(), [new("participant", participant)]);

        Assert.Throws<ArgumentNullException>(() => transaction.Rollback(null!));
        transaction.Commit();
    }

    [Fact]
    public async Task Implicit_dispose_rolls_back_and_surfaces_restore_failure()
    {
        var restored = new TestParticipant("restored", []);
        await using (var transaction = await new InMemoryCheckpointTransactionCoordinator()
                         .BeginAsync(Plan(), [new("restored", restored)]))
            restored.Write(9);
        Assert.Equal(0, restored.Read());

        var faulted = new TestParticipant("faulted", []) { RestoreFailure = new InvalidOperationException("restore failed") };
        var failing = await new InMemoryCheckpointTransactionCoordinator()
            .BeginAsync(Plan(), [new("faulted", faulted)]);
        var failure = await Assert.ThrowsAsync<InMemoryCheckpointRollbackException>(() => failing.DisposeAsync().AsTask());

        Assert.Single(failure.RollbackFailures);
        Assert.Throws<InMemoryCheckpointParticipantFaultedException>(() => faulted.TransactionGate.Enter());
        await Assert.ThrowsAsync<InMemoryCheckpointParticipantFaultedException>(async () =>
            await faulted.TransactionGate.EnterAsync());
    }

    [Fact]
    public async Task Public_gate_sync_async_and_ambient_noop_paths_share_one_exclusion_boundary()
    {
        var participant = new TestParticipant("participant", []);
        var coordinator = new InMemoryCheckpointTransactionCoordinator();
        await using var transaction = await coordinator.BeginAsync(Plan(), [new("participant", participant)]);

        participant.Write(12); // Ambient synchronous transaction path is a no-op gate lease.
        await using (var ambientAsyncLease = await participant.TransactionGate.EnterAsync())
            Assert.Equal(12, participant.RawValue);
        Task<int> blockedRead;
        using (ExecutionContext.SuppressFlow())
            blockedRead = Task.Run(async () =>
            {
                await using var lease = await participant.TransactionGate.EnterAsync();
                return participant.RawValue;
            });
        await Task.Delay(50);
        Assert.False(blockedRead.IsCompleted);

        transaction.Commit();
        Assert.Equal(12, await blockedRead.WaitAsync(TimeSpan.FromSeconds(2)));
        using var syncLease = participant.TransactionGate.Enter();
    }

    [Fact]
    public async Task Public_gate_leases_are_idempotently_disposable()
    {
        var gate = new InMemoryCheckpointParticipantGate();

        var syncLease = gate.Enter();
        syncLease.Dispose();
        syncLease.Dispose();
        var asyncLease = await gate.EnterAsync();
        await asyncLease.DisposeAsync();
        await asyncLease.DisposeAsync();

        using var finalLease = gate.Enter();
    }

    [Fact]
    public void Sync_gate_releases_semaphore_when_fault_is_detected_after_wait()
    {
        var gate = PostWaitFaultingGate();

        Assert.Throws<InMemoryCheckpointParticipantFaultedException>(() => gate.Enter());

        Assert.Equal(1, AvailableLeaseCount(gate));
    }

    [Fact]
    public async Task Async_gate_releases_semaphore_when_fault_is_detected_after_wait()
    {
        var gate = PostWaitFaultingGate();

        await Assert.ThrowsAsync<InMemoryCheckpointParticipantFaultedException>(async () =>
            await gate.EnterAsync());

        Assert.Equal(1, AvailableLeaseCount(gate));
    }

    [Fact]
    public async Task Transaction_acquire_releases_semaphore_when_fault_is_detected_after_wait()
    {
        var participant = new TestParticipant("participant", []);
        SetPostWaitFault(participant.TransactionGate);

        await Assert.ThrowsAsync<InMemoryCheckpointParticipantFaultedException>(() =>
            new InMemoryCheckpointTransactionCoordinator()
                .BeginAsync(Plan(), [new("participant", participant)])
                .AsTask());

        Assert.Equal(1, AvailableLeaseCount(participant.TransactionGate));
    }

    private static InMemoryCheckpointMutationPlan Plan()
    {
        var checkpoint = new RuntimeCheckpoint(
            "checkpoint-transaction",
            "TransactionCheckpoint",
            "workflow-transaction",
            DateTimeOffset.UnixEpoch,
            [],
            new Dictionary<string, string>());
        var changes = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []);
        return new InMemoryCheckpointMutationPlan(new RuntimeCheckpointCommit(
            "commit-transaction",
            checkpoint,
            changes,
            [],
            new Dictionary<string, string>()));
    }

    private sealed class TestSource(params object?[] participants) : IInMemoryCheckpointTransactionSource
    {
        public IEnumerable<object?> GetCheckpointTransactionParticipants() => participants;
    }

    private sealed class NullTestSource : IInMemoryCheckpointTransactionSource
    {
        public IEnumerable<object?> GetCheckpointTransactionParticipants() => null!;
    }

    private static InMemoryCheckpointParticipantGate PostWaitFaultingGate()
    {
        var gate = new InMemoryCheckpointParticipantGate();
        SetPostWaitFault(gate);
        return gate;
    }

    private static void SetPostWaitFault(InMemoryCheckpointParticipantGate gate) =>
        typeof(InMemoryCheckpointParticipantGate)
            .GetProperty("PostWaitFaultForTesting", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(gate, new InvalidOperationException("fault after wait"));

    private static int AvailableLeaseCount(InMemoryCheckpointParticipantGate gate) =>
        ((SemaphoreSlim)typeof(InMemoryCheckpointParticipantGate)
            .GetField("_semaphore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(gate)!).CurrentCount;

    private sealed class TestParticipant(string name, ICollection<string> events) : IInMemoryCheckpointTransactionParticipant
    {
        private int _value;
        public InMemoryCheckpointParticipantGate TransactionGate { get; } = new();
        public bool Affected { get; init; } = true;
        public Exception? RestoreFailure { get; init; }
        public Exception? CaptureFailure { get; init; }
        public int RawValue => _value;
        public bool IsAffected(InMemoryCheckpointMutationPlan plan) => Affected;

        public object CaptureCheckpointState(InMemoryCheckpointMutationPlan plan)
        {
            events.Add($"capture:{name}");
            if (CaptureFailure is not null)
                throw CaptureFailure;
            return _value;
        }

        public void RestoreCheckpointState(object snapshot)
        {
            events.Add($"restore:{name}");
            if (RestoreFailure is not null)
                throw RestoreFailure;
            _value = (int)snapshot;
        }

        public int Read()
        {
            using var gate = TransactionGate.Enter();
            return _value;
        }

        public void Write(int value)
        {
            using var gate = TransactionGate.Enter();
            _value = value;
        }
    }
}
