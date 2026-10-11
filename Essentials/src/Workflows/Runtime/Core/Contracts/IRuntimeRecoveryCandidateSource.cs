using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Supplies recovery candidates the recovery sweep re-drives beside those its scanner finds, for the current
/// persistence scope. It is how an execution becomes a recovery candidate before its execution lease or heartbeat
/// times out.
/// </summary>
/// <remarks>
/// <para>
/// The distributed runtime implements it with the executions whose execution lease or heartbeat was held under a host
/// id it reclaimed, because that host id departed or because this process's predecessor held it (spec 184, FR-024 and
/// FR-027). The contract lives here so the core gains no reference to that leaf (framework constitution §2.7).
/// </para>
/// <para>
/// A candidate is an accelerator, never the record of what is owed: the execution lease is still what makes an
/// execution recoverable, and it becomes a scanner candidate on its own once it times out. A source may therefore keep
/// its candidates in memory. Re-driving a candidate acquires a strictly greater fencing token, so a candidate listed
/// for an execution whose owner is still alive costs repeated work, never a second commit.
/// </para>
/// </remarks>
public interface IRuntimeRecoveryCandidateSource
{
    /// <summary>Lists at most <paramref name="limit"/> candidates for the current persistence scope.</summary>
    ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ListAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Settles candidates the sweep has dealt with: re-driven into a mailbox or the durable transport, or found
    /// terminal. A candidate whose re-drive faulted or was rejected is not settled and is listed again.
    /// </summary>
    ValueTask SettleAsync(IReadOnlyCollection<string> workflowExecutionIds, CancellationToken cancellationToken = default);
}
