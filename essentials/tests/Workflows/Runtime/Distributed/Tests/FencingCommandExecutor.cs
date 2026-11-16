using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// A command executor that models a real drain committing a checkpoint under W5 single-writer fencing: on each command
/// it acquires a fresh, strictly greater fencing token from this node's ownership service and then fences stale writers
/// at the commit funnel (<see cref="IRuntimeExecutionOwnershipService.EnsureCurrentAsync"/>) before recording the
/// durable effect. It is the acceptance harness's stand-in for <c>RuntimeCheckpointCommitter</c>: the distributed
/// provider consumes the fencing seam unchanged, so wiring the real ownership service here is what makes the two-node
/// kill test exercise genuine double-execution prevention rather than a mock.
/// </summary>
internal sealed class FencingCommandExecutor : IWorkflowExecutionCommandExecutor
{
    private readonly IRuntimeExecutionOwnershipService _ownership;
    private readonly Func<WorkflowExecutionCommandEnvelope, RuntimeExecutionLease, CancellationToken, ValueTask>? _commitEffect;
    private readonly ConcurrentQueue<string> _committed = new();

    public FencingCommandExecutor(
        IRuntimeExecutionOwnershipService ownership,
        Func<WorkflowExecutionCommandEnvelope, RuntimeExecutionLease, CancellationToken, ValueTask>? commitEffect = null)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        _ownership = ownership;
        _commitEffect = commitEffect;
    }

    /// <summary>Envelope IDs whose durable commit passed the fencing check on this node, in commit order.</summary>
    public IReadOnlyList<string> Committed => _committed.ToArray();

    public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // Acquire ownership for this drain (issues a strictly greater token than any prior owner) and then fence: a
        // superseded node that later tries to commit its stale token is rejected here, not by the transport.
        var lease = await _ownership.AcquireAsync(envelope.WorkflowExecutionId, cancellationToken);
        await _ownership.EnsureCurrentAsync(envelope.WorkflowExecutionId, lease.FencingToken, cancellationToken);
        if (_commitEffect is not null)
            await _commitEffect(envelope, lease, cancellationToken);
        _committed.Enqueue(envelope.EnvelopeId);

        // An accepted outcome so the pump acks the transport item; the drain itself performed no scheduler work here.
        return WorkflowExecutionCommandProcessResult.NoDrain;
    }
}
