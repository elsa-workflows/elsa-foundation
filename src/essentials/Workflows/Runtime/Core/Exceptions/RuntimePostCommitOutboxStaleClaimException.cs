using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Signals that an outbox completion was presented by a claimant that no longer owns the current fence.</summary>
public sealed class RuntimePostCommitOutboxStaleClaimException : InvalidOperationException
{
    public RuntimePostCommitOutboxStaleClaimException(
        string outboxItemId,
        string presentedOwnerId,
        long presentedFencingToken,
        string? currentOwnerId,
        long currentFencingToken,
        RuntimePostCommitOutboxStatus currentStatus)
        : base(
            $"Post-commit outbox claim for '{outboxItemId}' owned by '{presentedOwnerId}' at fence " +
            $"{presentedFencingToken} is stale or not owned by the claimant. The current state is " +
            $"'{currentStatus}', owner '{currentOwnerId ?? "<none>"}', fence {currentFencingToken}.")
    {
        OutboxItemId = outboxItemId;
        PresentedOwnerId = presentedOwnerId;
        PresentedFencingToken = presentedFencingToken;
        CurrentOwnerId = currentOwnerId;
        CurrentFencingToken = currentFencingToken;
        CurrentStatus = currentStatus;
    }

    public string OutboxItemId { get; }
    public string PresentedOwnerId { get; }
    public long PresentedFencingToken { get; }
    public string? CurrentOwnerId { get; }
    public long CurrentFencingToken { get; }
    public RuntimePostCommitOutboxStatus CurrentStatus { get; }
}
