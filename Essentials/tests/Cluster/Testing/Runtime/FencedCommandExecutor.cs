using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Cluster.Testing.Runtime;

/// <summary>
/// A command executor that models a drain committing one checkpoint under single-writer fencing (ADR 0078, invariant 2):
/// it acquires the execution lease, which always issues a strictly greater fencing token, and commits only if that token
/// is still current when the commit is reached. A drain can be held between the two, so a test can reclaim, re-drive and
/// commit elsewhere while this member is mid-drain. A committed start command records the execution's durable state,
/// pinned to its executable, as the real drain's first checkpoint does.
/// </summary>
public sealed class FencedCommandExecutor(IRuntimeExecutionOwnershipService ownership, IWorkflowExecutionStateStore executions) : IWorkflowExecutionCommandExecutor
{
    private readonly ConcurrentQueue<FencedCommit> _committed = new();
    private readonly ConcurrentQueue<RuntimeStaleFencingTokenException> _fenced = new();
    private DrainHold? _hold;

    /// <summary>The commits that passed the fence, in commit order.</summary>
    public IReadOnlyList<FencedCommit> Committed => _committed.ToArray();

    /// <summary>The commits the fence refused.</summary>
    public IReadOnlyList<RuntimeStaleFencingTokenException> Fenced => _fenced.ToArray();

    /// <summary>Holds the next drain after it acquires its execution lease and before it commits.</summary>
    public DrainHold HoldNextDrain() => _hold = new DrainHold();

    public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var hold = Interlocked.Exchange(ref _hold, null);
        RuntimeExecutionLease lease;
        try
        {
            lease = await ownership.AcquireAsync(envelope.WorkflowExecutionId, cancellationToken);
        }
        catch (Exception exception)
        {
            hold?.Started.TrySetException(exception);
            throw;
        }

        if (hold is not null)
        {
            hold.Started.TrySetResult(lease);
            await hold.Released.Task.WaitAsync(cancellationToken);
        }

        try
        {
            await ownership.EnsureCurrentAsync(envelope.WorkflowExecutionId, lease.FencingToken, cancellationToken);
            if (envelope.Command is { Kind: WorkflowExecutionCommandKind.Start, Payload: { } payload } &&
                payload.Deserialize<WorkflowExecutionStartCommandPayload>() is { } start)
            {
                var now = lease.AcquiredAt;
                await executions.SaveAsync(new WorkflowExecutionState(
                    envelope.WorkflowExecutionId, start.PinnedExecutable, WorkflowExecutionStatus.Running,
                    null, now, now, now, null, null, null, null, new Dictionary<string, string>()), cancellationToken);
            }

            _committed.Enqueue(new FencedCommit(envelope.WorkflowExecutionId, envelope.EnvelopeId, lease.FencingToken));
        }
        catch (RuntimeStaleFencingTokenException fenced)
        {
            _fenced.Enqueue(fenced);
        }

        return WorkflowExecutionCommandProcessResult.NoDrain;
    }
}

/// <summary>A commit that passed the fence, with the fencing token it held.</summary>
public sealed record FencedCommit(string WorkflowExecutionId, string EnvelopeId, long FencingToken);

/// <summary>A drain held mid-way: it has acquired its execution lease and waits to commit until released.</summary>
public sealed class DrainHold
{
    /// <summary>Completes with the execution lease once the held drain has acquired it.</summary>
    public TaskCompletionSource<RuntimeExecutionLease> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => Released.TrySetResult();
}
