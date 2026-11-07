namespace Elsa.Jobs.Core;

/// <summary>Logical pause/capacity target inside an exact JobScope. Workflow gates require an ordinal model kind; queue/type gates require null. Providers validate the complete key.</summary>
public sealed record JobGateKey(
    JobGateKind Kind,
    string Name,
    string? ModelKind = null);

/// <summary>One independently released durable hold; timestamps are authority-owned.</summary>
public sealed record JobHold(
    JobGateKey Gate,
    string HoldId,
    long Revision,
    string Actor,
    string Reason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ReleasedAt,
    string? ReleasedBy);

/// <summary>Adds one hold without replacing concurrent holds; pause orders atomically against start/materialization permissions.</summary>
public sealed record JobPauseRequest(
    JobScope Scope,
    JobGateKey Gate,
    string HoldId,
    JobOperation Operation);

/// <summary>Releases only the addressed hold, preserving every other applicable hold.</summary>
public sealed record JobReleaseHoldRequest(
    JobScope Scope,
    JobGateKey Gate,
    string HoldId,
    long ExpectedRevision,
    JobOperation Operation);

/// <summary>Deployment-wide live authorized-lease limit, never an unconditional physical-handler bound.</summary>
public sealed record JobCapacity(
    JobGateKey Gate,
    int? Limit,
    long Revision);

/// <summary>Revision-checked capacity change; zero revision creates a policy, null limit means unlimited.</summary>
public sealed record JobSetCapacityRequest(
    JobScope Scope,
    JobGateKey Gate,
    int? Limit,
    long ExpectedRevision,
    JobOperation Operation);

/// <summary>Idempotent cancellation request; physical exit remains a separate observation.</summary>
public sealed record JobCancelRequest(
    JobScope Scope,
    string JobId,
    JobOperation Operation);

/// <summary>Cancellation-before-admission tombstone for an exact trusted origin and accepted request digest.</summary>
public sealed record JobCancelOriginRequest(
    JobScope Scope,
    JobOrigin Origin,
    string RequestSha256,
    JobOperation Operation);

/// <summary>Atomic terminal-revision check and linked new invocation; an old association is never silently reattached.</summary>
public sealed record JobRetryAsNewRunRequest(
    JobScope Scope,
    string JobId,
    long ExpectedTerminalRevision,
    string ProposedNewJobId,
    string IdempotencyKey,
    JobOperation Operation);

/// <summary>All applicable scopes compose; logical type/definition names cover every version.</summary>
public enum JobGateKind
{
    Queue = 0,
    JobType = 1,
    WorkflowInstance = 2,
    WorkflowDefinition = 3
}
