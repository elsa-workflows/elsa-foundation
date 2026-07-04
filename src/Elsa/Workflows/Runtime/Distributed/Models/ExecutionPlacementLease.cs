namespace Elsa.Workflows.Runtime.Distributed.Models;

/// <summary>
/// A best-effort routing claim asserting that one node currently hosts the workflow-execution actor for a workflow
/// execution id. Placement governs <b>routing</b> (which node drains an execution) so at most one node drains at a
/// time; it is deliberately <b>not</b> the correctness backstop. Double durable-execution is prevented by W5 single-writer
/// fencing (<c>IRuntimeExecutionOwnershipService</c> / <c>RuntimeCheckpointCommitter</c>), which rejects a superseded
/// writer's commit even if placement routing is momentarily wrong. See the package README for the full
/// placement=routing / fencing=safety argument.
/// </summary>
/// <remarks>
/// The placement token is a monotonically increasing counter per workflow execution, bumped on every grant/renewal.
/// It is used only to make release idempotent (release only when the caller still holds the exact token) and to detect
/// take-over; it is not a fencing token and is never consulted at checkpoint commit.
/// </remarks>
public sealed class ExecutionPlacementLease
{
    public ExecutionPlacementLease(
        string workflowExecutionId,
        string ownerId,
        long placementToken,
        DateTimeOffset acquiredAt,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        if (placementToken <= 0)
            throw new ArgumentOutOfRangeException(nameof(placementToken), "Placement token must be greater than zero.");

        if (expiresAt <= acquiredAt)
            throw new ArgumentException("Placement lease expiration must be later than acquisition time.", nameof(expiresAt));

        WorkflowExecutionId = workflowExecutionId;
        OwnerId = ownerId;
        PlacementToken = placementToken;
        AcquiredAt = acquiredAt;
        ExpiresAt = expiresAt;
    }

    public string WorkflowExecutionId { get; }
    public string OwnerId { get; }
    public long PlacementToken { get; }
    public DateTimeOffset AcquiredAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;
}
