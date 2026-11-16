using System.Collections.Concurrent;
using Elsa.Diagnostics.Persistence.Draining;

namespace Elsa.Diagnostics.Persistence.Tests.Fixtures;

public sealed class DiagnosticsOperationalException(string message) : Exception(message);

public sealed class DiagnosticsTargetState
{
    internal ConcurrentDictionary<DiagnosticsDrainBatchId, DiagnosticsDrainCommit<int>> Commits { get; } = new();
    public ConcurrentQueue<int> PersistedItems { get; } = new();
}

/// <summary>Reusable acknowledgement-loss, cancellation, restart, and operational-failure target.</summary>
public sealed class DiagnosticsFailureTarget : IDiagnosticsDrainTarget<int, int>
{
    private readonly DiagnosticsTargetState _state;
    private readonly ConcurrentDictionary<DiagnosticsDrainBatchId, byte> _lostAcknowledgements = new();
    private int _attempts;
    private int _retentionCalls;

    public DiagnosticsFailureTarget(DiagnosticsTargetState? state = null) => _state = state ?? new();

    public DiagnosticsTargetState State => _state;
    public bool LoseFirstAcknowledgement { get; set; }
    public bool IgnoreCancellation { get; set; }
    public int RetentionDeleted { get; set; }
    public Func<DiagnosticsDrainBatch<int>, int, Exception?>? Failure { get; set; }
    public Func<int, Exception?>? RetentionFailure { get; set; }
    public TaskCompletionSource CommitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource? CommitRelease { get; set; }
    public Action? CancellationCallback { get; set; }
    public int Attempts => Volatile.Read(ref _attempts);
    public int RetentionCalls => Volatile.Read(ref _retentionCalls);
    public ConcurrentQueue<DiagnosticsDrainBatchId> AttemptedBatchIds { get; } = new();

    public async ValueTask<DiagnosticsDrainCommit<int>> CommitAsync(
        DiagnosticsDrainBatch<int> batch,
        CancellationToken cancellationToken = default)
    {
        using var cancellationRegistration = cancellationToken.Register(() => CancellationCallback?.Invoke());
        var attempt = Interlocked.Increment(ref _attempts);
        AttemptedBatchIds.Enqueue(batch.Id);
        CommitEntered.TrySetResult();
        if (CommitRelease is { } release)
        {
            if (IgnoreCancellation)
                await release.Task;
            else
                await release.Task.WaitAsync(cancellationToken);
        }

        if (Failure?.Invoke(batch, attempt) is { } failure)
            throw failure;

        var commit = _state.Commits.GetOrAdd(batch.Id, _ =>
        {
            foreach (var item in batch.Items)
                _state.PersistedItems.Enqueue(item);
            return new(batch.Items.ToArray(), batch.Items.Count);
        });
        if (LoseFirstAcknowledgement && _lostAcknowledgements.TryAdd(batch.Id, 0))
            throw new DiagnosticsOperationalException("The commit succeeded but its acknowledgement was lost.");
        return commit;
    }

    public ValueTask<int> ApplyRetentionAsync(CancellationToken cancellationToken = default)
    {
        var call = Interlocked.Increment(ref _retentionCalls);
        if (RetentionFailure?.Invoke(call) is { } failure)
            throw failure;
        return ValueTask.FromResult(RetentionDeleted);
    }

    public DiagnosticsFailureTarget Restart() => new(_state);
}
