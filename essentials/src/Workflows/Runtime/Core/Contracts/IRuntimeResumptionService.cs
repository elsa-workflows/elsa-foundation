using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Runs one system-wide resumption sweep: re-delivers pending/due-retryable post-commit outbox items,
/// discovers workflow executions with stranded scheduler-work backlog or recovery candidates, and
/// re-drives each one through its execution agent so the normal command→drain path completes the work.
/// </summary>
/// <remarks>
/// This is the second half of the durability contract: durable stores keep checkpoints, outbox items,
/// and queued scheduler work across a crash, and the sweep guarantees something eventually acts on them.
/// Re-driving goes through the agent mailbox (never draining directly), preserving the single-writer
/// discipline per workflow execution.
/// </remarks>
public interface IRuntimeResumptionService
{
    ValueTask<RuntimeResumptionSweepResult> SweepAsync(RuntimeResumptionSweepRequest request, CancellationToken cancellationToken = default);
}
