using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Provider capability required for a store to join an in-memory checkpoint transaction.</summary>
public interface IInMemoryCheckpointTransactionParticipant
{
    InMemoryCheckpointParticipantGate TransactionGate { get; }
    bool IsAffected(InMemoryCheckpointMutationPlan plan);
    object CaptureCheckpointState(InMemoryCheckpointMutationPlan plan);
    void RestoreCheckpointState(object snapshot);
}

/// <summary>Exposes nested in-memory stores mutated by one composite checkpoint operation.</summary>
public interface IInMemoryCheckpointTransactionSource
{
    IEnumerable<object?> GetCheckpointTransactionParticipants();
}

/// <summary>A named transaction root whose absence or incompatible provider must fail during preflight.</summary>
public sealed record InMemoryCheckpointTransactionRoot(string Name, object? Component, bool Required = true);

/// <summary>Exact identities a checkpoint transaction may mutate.</summary>
public sealed class InMemoryCheckpointMutationPlan
{
    public InMemoryCheckpointMutationPlan(
        RuntimeCheckpointCommit commit,
        IReadOnlyCollection<RuntimePostCommitOutboxItem>? outboxItems = null,
        IReadOnlyCollection<string>? commitIds = null)
    {
        ArgumentNullException.ThrowIfNull(commit);
        outboxItems ??= [];
        CommitId = commit.CommitId;
        CommitIds = Snapshot(commitIds ?? [commit.CommitId]);
        WorkflowExecutionId = commit.WorkflowExecutionId;
        MutatesWorkflowExecution = commit.StateChanges.WorkflowExecution is not null;
        MutatesScheduler = commit.StateChanges.Scheduler is not null;
        HasExpectedFence = commit.ExpectedFence is not null;
        HasActivityScopeCleanup = commit.StateChanges.ActivityScopeCleanups.Count > 0;
        ActivityExecutionStateIds = Snapshot(commit.StateChanges.ActivityExecutions.Select(x => x.StateId));
        ActivityInspectionIds = Snapshot(commit.StateChanges.ActivityExecutionInspections.Select(x => x.StateId));
        ActivityHierarchyIds = Snapshot(commit.StateChanges.ActivityExecutionInspections
            .Where(x => !string.IsNullOrWhiteSpace(x.State.ExecutionScopeId ?? x.State.Provenance.ExecutionScopeId))
            .Select(x => x.StateId));
        BookmarkIds = Snapshot(commit.StateChanges.Bookmarks.Select(x => x.StateId)
            .Concat(commit.StateChanges.ActivityScopeCleanups.SelectMany(x => x.BookmarkIds)));
        DurableValueIds = Snapshot(commit.StateChanges.DurableValues.Select(x => x.StateId));
        IncidentIds = Snapshot(commit.StateChanges.Incidents.Select(x => x.StateId));
        OperationalStateIds = Snapshot(commit.StateChanges.Operational.Select(x => x.StateId));
        SchedulerWorkItemIds = Snapshot(commit.StateChanges.ConsumedSchedulerWorkItems.Select(x => x.WorkItemId)
            .Concat(commit.StateChanges.ActivityScopeCleanups.SelectMany(x => x.SchedulerWorkItemIds)));
        TimerIds = Snapshot(commit.StateChanges.ActivityScopeCleanups.SelectMany(x => x.TimerIds));
        WorkflowDispatchIds = Snapshot(commit.StateChanges.WorkflowDispatches.Select(x => x.StateId)
            .Concat(commit.StateChanges.WorkflowDispatchCancellations.Select(x => x.DispatchId)));
        OutboxItemIds = Snapshot(outboxItems.Select(x => x.OutboxItemId));
        AlterationJobId = commit.StateChanges.AlterationJobTerminalChange?.JobId;
    }

    public string CommitId { get; }
    public IReadOnlySet<string> CommitIds { get; }
    public string WorkflowExecutionId { get; }
    public bool MutatesWorkflowExecution { get; }
    public bool MutatesScheduler { get; }
    public bool HasExpectedFence { get; }
    public bool HasActivityScopeCleanup { get; }
    public IReadOnlySet<string> ActivityExecutionStateIds { get; }
    public IReadOnlySet<string> ActivityInspectionIds { get; }
    public IReadOnlySet<string> ActivityHierarchyIds { get; }
    public IReadOnlySet<string> BookmarkIds { get; }
    public IReadOnlySet<string> DurableValueIds { get; }
    public IReadOnlySet<string> IncidentIds { get; }
    public IReadOnlySet<string> OperationalStateIds { get; }
    public IReadOnlySet<string> SchedulerWorkItemIds { get; }
    public IReadOnlySet<string> TimerIds { get; }
    public IReadOnlySet<string> WorkflowDispatchIds { get; }
    public IReadOnlySet<string> OutboxItemIds { get; }
    public string? AlterationJobId { get; }

    private static IReadOnlySet<string> Snapshot(IEnumerable<string> values) =>
        values.ToHashSet(StringComparer.Ordinal);
}

/// <summary>Ranked async gate shared by every public operation of one in-memory participant.</summary>
public sealed class InMemoryCheckpointParticipantGate
{
    private static long _nextRank;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private Exception? _fault;

    public InMemoryCheckpointParticipantGate() => Rank = Interlocked.Increment(ref _nextRank);

    public long Rank { get; }
    public bool IsFaulted => Volatile.Read(ref _fault) is not null;

    // Deterministic test seam for the otherwise race-only branch between semaphore acquisition and the second
    // fail-closed check. It is internal and inert in production compositions.
    internal Exception? PostWaitFaultForTesting { get; set; }

    public IDisposable Enter()
    {
        ThrowIfFaulted();
        if (InMemoryCheckpointTransactionCoordinator.HasAmbientLease(this))
            return NoopLease.Instance;
        _semaphore.Wait();
        try
        {
            ApplyPostWaitFaultForTesting();
            ThrowIfFaulted();
            return new GateLease(this);
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    public async ValueTask<IAsyncDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfFaulted();
        if (InMemoryCheckpointTransactionCoordinator.HasAmbientLease(this))
            return NoopLease.Instance;
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            ApplyPostWaitFaultForTesting();
            ThrowIfFaulted();
            return new GateLease(this);
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    internal async ValueTask AcquireForTransactionAsync(CancellationToken cancellationToken)
    {
        ThrowIfFaulted();
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            ApplyPostWaitFaultForTesting();
            ThrowIfFaulted();
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    internal void ReleaseForTransaction() => _semaphore.Release();

    internal void Fault(Exception exception) => Interlocked.CompareExchange(ref _fault, exception, null);

    private void ApplyPostWaitFaultForTesting()
    {
        if (PostWaitFaultForTesting is { } fault)
            Fault(fault);
    }

    private void ThrowIfFaulted()
    {
        if (Volatile.Read(ref _fault) is { } fault)
            throw new InMemoryCheckpointParticipantFaultedException(Rank, fault);
    }

    private sealed class GateLease(InMemoryCheckpointParticipantGate gate) : IDisposable, IAsyncDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate._semaphore.Release();
        }
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoopLease : IDisposable, IAsyncDisposable
    {
        public static NoopLease Instance { get; } = new();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Thrown before reservation when a required affected component cannot join the in-memory transaction.</summary>
public sealed class InMemoryCheckpointTransactionCompositionException(string componentName) : InvalidOperationException(
    $"Checkpoint component '{componentName}' is missing or does not implement the in-memory transaction participant capability. Replace the checkpoint store with the same provider when replacing an affected constituent store.")
{
    public string ComponentName { get; } = componentName;
}

/// <summary>Thrown after a failed restore. The original apply failure and every restore failure remain available.</summary>
public sealed class InMemoryCheckpointRollbackException(
    Exception primaryFailure,
    IReadOnlyList<Exception> rollbackFailures) : Exception(
    "The in-memory checkpoint failed and one or more participants could not be restored; the affected provider is faulted.",
    primaryFailure)
{
    public Exception PrimaryFailure { get; } = primaryFailure;
    public IReadOnlyList<Exception> RollbackFailures { get; } = rollbackFailures;
}

/// <summary>Thrown when a failed rollback has faulted a participant and subsequent access must fail closed.</summary>
public sealed class InMemoryCheckpointParticipantFaultedException(long participantRank, Exception fault) : InvalidOperationException(
    $"In-memory checkpoint participant {participantRank} is faulted because rollback did not complete.", fault)
{
    public long ParticipantRank { get; } = participantRank;
}

/// <summary>
/// Directly testable strict two-phase-locking coordinator. It validates roots, orders and acquires exact affected
/// participant gates, installs a controlled ambient lease, captures mementos, and retains every gate until commit or
/// rollback finishes.
/// </summary>
public sealed class InMemoryCheckpointTransactionCoordinator
{
    private static readonly AsyncLocal<AmbientLeaseHolder?> Ambient = new();

    public ValueTask<Transaction> BeginAsync(
        InMemoryCheckpointMutationPlan plan,
        CancellationToken cancellationToken = default,
        params object?[] roots) => BeginAsync(
            plan,
            roots.Select((root, index) => new InMemoryCheckpointTransactionRoot($"root[{index}]", root, Required: false)),
            cancellationToken);

    public ValueTask<Transaction> BeginAsync(
        InMemoryCheckpointMutationPlan plan,
        IEnumerable<InMemoryCheckpointTransactionRoot> roots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(roots);
        var holder = Ambient.Value ??= new AmbientLeaseHolder();
        return BeginCoreAsync(plan, roots, holder, cancellationToken);
    }

    private static async ValueTask<Transaction> BeginCoreAsync(
        InMemoryCheckpointMutationPlan plan,
        IEnumerable<InMemoryCheckpointTransactionRoot> roots,
        AmbientLeaseHolder holder,
        CancellationToken cancellationToken)
    {

        var participants = Discover(roots)
            .Where(x => x.IsAffected(plan))
            .Distinct((IEqualityComparer<IInMemoryCheckpointTransactionParticipant>)ReferenceEqualityComparer.Instance)
            .OrderBy(x => x.TransactionGate.Rank)
            .ToArray();

        var ambient = holder.Lease;
        if (ambient is not null)
        {
            var missing = participants.FirstOrDefault(x => !ambient.Gates.Contains(x.TransactionGate));
            if (missing is not null)
                throw new InvalidOperationException("A nested in-memory transaction cannot acquire a participant outside its ambient lease.");
            var nestedEntries = participants.Select(x => new Entry(x, x.CaptureCheckpointState(plan))).ToArray();
            return new Transaction(nestedEntries, [], holder, ambient, ownsAmbient: false);
        }

        var acquired = new List<InMemoryCheckpointParticipantGate>(participants.Length);
        try
        {
            foreach (var participant in participants)
            {
                await participant.TransactionGate.AcquireForTransactionAsync(cancellationToken);
                acquired.Add(participant.TransactionGate);
            }

            var lease = new AmbientLease(acquired.ToHashSet((IEqualityComparer<InMemoryCheckpointParticipantGate>)ReferenceEqualityComparer.Instance));
            holder.Lease = lease;
            try
            {
                var entries = participants.Select(x => new Entry(x, x.CaptureCheckpointState(plan))).ToArray();
                return new Transaction(entries, acquired, holder, lease, ownsAmbient: true);
            }
            catch
            {
                holder.Lease = null;
                throw;
            }
        }
        catch
        {
            for (var index = acquired.Count - 1; index >= 0; index--)
                acquired[index].ReleaseForTransaction();
            throw;
        }
    }

    internal static bool HasAmbientLease(InMemoryCheckpointParticipantGate gate) =>
        Ambient.Value?.Lease?.Gates.Contains(gate) == true;

    private static IReadOnlyCollection<IInMemoryCheckpointTransactionParticipant> Discover(
        IEnumerable<InMemoryCheckpointTransactionRoot> roots)
    {
        var participants = new List<IInMemoryCheckpointTransactionParticipant>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var root in roots)
        {
            ArgumentNullException.ThrowIfNull(root);
            if (root.Component is null)
            {
                if (root.Required)
                    throw new InMemoryCheckpointTransactionCompositionException(root.Name);
                continue;
            }
            Visit(root.Name, root.Component, root.Required, visited, participants);
        }
        return participants;
    }

    private static void Visit(
        string name,
        object candidate,
        bool required,
        ISet<object> visited,
        ICollection<IInMemoryCheckpointTransactionParticipant> participants)
    {
        if (!visited.Add(candidate))
            return;
        var recognized = false;
        if (candidate is IInMemoryCheckpointTransactionParticipant participant)
        {
            participants.Add(participant);
            recognized = true;
        }
        if (candidate is IInMemoryCheckpointTransactionSource source)
        {
            recognized = true;
            var index = 0;
            var nestedParticipants = source.GetCheckpointTransactionParticipants() ??
                                     throw new InMemoryCheckpointTransactionCompositionException(name);
            foreach (var nested in nestedParticipants)
            {
                if (nested is null)
                    throw new InMemoryCheckpointTransactionCompositionException($"{name}[{index}]");
                Visit($"{name}[{index}]", nested, required: true, visited, participants);
                index++;
            }
        }
        if (required && !recognized)
            throw new InMemoryCheckpointTransactionCompositionException(name);
    }

    internal sealed class AmbientLeaseHolder
    {
        public AmbientLease? Lease { get; set; }
    }

    internal sealed record AmbientLease(IReadOnlySet<InMemoryCheckpointParticipantGate> Gates);
    internal sealed record Entry(IInMemoryCheckpointTransactionParticipant Participant, object Snapshot);

    public sealed class Transaction : IAsyncDisposable
    {
        private readonly IReadOnlyList<Entry> _entries;
        private readonly IReadOnlyList<InMemoryCheckpointParticipantGate> _acquired;
        private readonly AmbientLeaseHolder? _holder;
        private readonly AmbientLease _lease;
        private readonly bool _ownsAmbient;
        private int _completed;

        internal Transaction(
            IReadOnlyList<Entry> entries,
            IReadOnlyList<InMemoryCheckpointParticipantGate> acquired,
            AmbientLeaseHolder? holder,
            AmbientLease lease,
            bool ownsAmbient)
        {
            _entries = entries;
            _acquired = acquired;
            _holder = holder;
            _lease = lease;
            _ownsAmbient = ownsAmbient;
        }

        public void Commit()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                throw new InvalidOperationException("The in-memory checkpoint transaction is already complete.");
            Release();
        }

        public Exception Rollback(Exception primaryFailure)
        {
            ArgumentNullException.ThrowIfNull(primaryFailure);
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return primaryFailure;

            var failures = new List<Exception>();
            for (var index = _entries.Count - 1; index >= 0; index--)
            {
                try
                {
                    _entries[index].Participant.RestoreCheckpointState(_entries[index].Snapshot);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            if (failures.Count > 0)
            {
                var providerFault = new AggregateException("One or more in-memory checkpoint participants could not be restored.", failures);
                foreach (var entry in _entries)
                    entry.Participant.TransactionGate.Fault(providerFault);
            }
            Release();
            return failures.Count == 0 ? primaryFailure : new InMemoryCheckpointRollbackException(primaryFailure, failures);
        }

        public ValueTask DisposeAsync()
        {
            if (Volatile.Read(ref _completed) == 0)
            {
                var failure = Rollback(new InvalidOperationException("An uncommitted in-memory checkpoint transaction was disposed."));
                if (failure is InMemoryCheckpointRollbackException rollbackFailure)
                    return ValueTask.FromException(rollbackFailure);
            }
            return ValueTask.CompletedTask;
        }

        private void Release()
        {
            if (_ownsAmbient && ReferenceEquals(_holder?.Lease, _lease))
                _holder.Lease = null;
            for (var index = _acquired.Count - 1; index >= 0; index--)
                _acquired[index].ReleaseForTransaction();
        }
    }
}
