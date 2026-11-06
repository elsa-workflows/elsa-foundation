using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

// OwnerAddress is the canonical length-framed hash of the actual issuing
// SourceScope (partition/tenant/kind/authority) + SourceId, not a display name.
public sealed record WorkflowPreparationId(
    PersistenceScope? Scope, string OwnerAddress, string OperationId);

// State/revision belong to the canonical publication operation, not a worker claim.
public sealed record WorkflowPreparationOwnerStamp(
    WorkflowPreparationId Id, string LedgerIncarnation,
    long OperationRevision, string OperationState, string RequestFingerprint,
    string SnapshotId, string SourceFingerprint, string SourceProfile,
    string CompilerProfile, string PreparedProfile, string PreparedContentFingerprint);

public enum WorkflowPreparationExecutableKind { Native, Model }
public abstract record WorkflowPreparationRoot;
public sealed record WorkflowPreparationExecutableRoot(
    WorkflowPreparationExecutableKind Kind, WorkflowExecutableIdentity Identity)
    : WorkflowPreparationRoot;
public sealed record WorkflowPreparationTemplateRoot(
    ExecutableActivityTemplateIdentity Identity) : WorkflowPreparationRoot;

public abstract record WorkflowPreparationMaterialPin;
public sealed record WorkflowPreparationExecutablePin(
    WorkflowPreparationExecutableKind Kind, WorkflowExecutableIdentity Identity,
    string IncarnationId) : WorkflowPreparationMaterialPin;
// Revision is a historical CAS observation. Stable selection identity excludes it;
// each write attempt must check a fresh revision under the native fence.
public sealed record WorkflowPreparationTemplatePin(
    ExecutableActivityTemplateIdentity Identity, string IncarnationId,
    string HashClaimIncarnationId, long Revision) : WorkflowPreparationMaterialPin;

public sealed record WorkflowPreparationSelectionQuery(
    WorkflowPreparationId Id, string SnapshotId, string SourceFingerprint,
    string SourceProfile, string CompilerProfile,
    string PreparedProfile, string PreparedContentFingerprint,
    IReadOnlyList<WorkflowPreparationRoot> Roots);
public sealed record WorkflowPreparationSelection(
    string SnapshotId, string SourceFingerprint, string SourceProfile,
    string CompilerProfile, string PreparedProfile, string PreparedContentFingerprint,
    IReadOnlyList<WorkflowPreparationRoot> Roots,
    IReadOnlyList<WorkflowPreparationMaterialPin> Closure, string Fingerprint);

public sealed record WorkflowPreparationToken(
    WorkflowPreparationId Id, string IncarnationId, long Revision,
    string SelectionFingerprint);
public enum WorkflowPreparationState { Retained, Transferred, RejectedReleased }
public enum WorkflowPreparationDecisionKind { Published, Rejected }
public sealed record WorkflowPreparationDecision(
    WorkflowPreparationDecisionKind Kind, WorkflowPreparationOwnerStamp VerifiedAt,
    string CanonicalDecisionId, string DecisionFingerprint,
    string SelectionFingerprint, IReadOnlyList<string> PublishedReferenceIds);

public sealed record WorkflowPreparationSnapshot(
    WorkflowPreparationId Id, WorkflowPreparationSelection Selection,
    WorkflowPreparationOwnerStamp AcceptedOwner, WorkflowPreparationState State,
    WorkflowPreparationToken Token, WorkflowPreparationToken? ClosedFrom,
    WorkflowPreparationDecision? TerminalDecision);
public sealed record WorkflowPreparationRetain(
    WorkflowPreparationId Id, WorkflowPreparationSelection Selection,
    WorkflowPreparationOwnerStamp ExpectedOwner);
public sealed record WorkflowPreparationComplete(
    WorkflowPreparationId Id, WorkflowPreparationToken Expected,
    WorkflowPreparationOwnerStamp ExpectedOwner);
public enum WorkflowPreparationWriteStatus
{
    Retained, AlreadyRetained, Released, AlreadyReleased,
    Pending, Conflict, Unsupported
}
public sealed record WorkflowPreparationWriteResult(
    WorkflowPreparationWriteStatus Status,
    WorkflowPreparationToken? Confirmation, string? Code = null);

public sealed record WorkflowPreparationPageQuery : RuntimeStorePageRequest
{
    public WorkflowPreparationPageQuery(
        int limit = DefaultLimit, string? continuationToken = null)
        : base(limit, continuationToken) { }
}

// Opaque invocation object, minted only by the Runtime coordinator when the
// configured issuer calls back. No public constructor or serialization path.
public sealed class WorkflowPreparationOwnerSession
{
    private readonly Action requireActiveCallback;
    private readonly IWorkflowPreparationOwnerLease lease;
    internal WorkflowPreparationOwnerSession(
        IWorkflowPreparationOwnerLease lease,
        Action requireActiveCallback)
    {
        this.lease = lease;
        this.requireActiveCallback = requireActiveCallback;
    }

    public WorkflowPreparationOwnerStamp Owner
    {
        get { requireActiveCallback(); return lease.Owner; }
    }

    // The lease refuses decision/selected-root reads until native Join and canonical reads succeed.
    public WorkflowPreparationDecision? Decision
    {
        get { requireActiveCallback(); return lease.Decision; }
    }

    public IWorkflowPreparationOwnerLease Lease
    {
        get { requireActiveCallback(); return lease; }
    }

    public IReadOnlyList<WorkflowPreparationRoot> SelectedRoots
    {
        get { requireActiveCallback(); return lease.SelectedRoots; }
    }

    public void EnsureActive() => requireActiveCallback();
}

public sealed class WorkflowPreparationException : Exception
{
    public WorkflowPreparationException(
        string code, WorkflowPreparationId id, Exception? inner = null)
        : base("Preparation retention failed (" + code + ").", inner)
    {
        Code = code;
        Id = id;
    }

    public string Code { get; }
    public WorkflowPreparationId Id { get; }
}
public sealed class WorkflowPreparationOutcomeUnknownException : Exception
{
    public WorkflowPreparationOutcomeUnknownException(
        WorkflowPreparationId id, Exception inner)
        : base("Preparation retention commit outcome is unknown.", inner)
    {
        Id = id;
    }

    public WorkflowPreparationId Id { get; }
}
