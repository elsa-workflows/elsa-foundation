using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Provider-neutral worker-facing access to checkpoint-admitted background activity work.</summary>
public interface IBackgroundActivityWorkStore
{
    ValueTask<BackgroundActivityWorkItem?> FindAsync(
        string workflowExecutionId,
        string workId,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically claims the earliest pending or expired item visible to the current persistence scope.</summary>
    ValueTask<BackgroundActivityWorkClaim?> ClaimNextAsync(
        string ownerId,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>Renews only the exact current, unexpired worker fence; returns null when it has been superseded.</summary>
    ValueTask<BackgroundActivityWorkClaim?> RenewClaimAsync(
        BackgroundActivityWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the first receipt only for the current unexpired claim. Replaying the identical receipt under the claim
    /// that recorded it succeeds after expiry; any different receipt is a conflict and cannot overwrite the row.
    /// </summary>
    ValueTask<BackgroundActivityWorkItem> RecordResultAsync(
        BackgroundActivityWorkClaim claim,
        BackgroundActivityWorkTerminalResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a bounded ordered repair-sweep view of ready receipts in the current persistence scope.</summary>
    ValueTask<IReadOnlyCollection<BackgroundActivityWorkItem>> ListResultReadyAsync(
        int limit,
        string? workflowExecutionId = null,
        CancellationToken cancellationToken = default);
}
