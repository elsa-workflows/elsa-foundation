// DESIGN ARTIFACT ONLY: proposed Runtime.Core public contract for plan R2 review.
// Not included in a project; not implemented, compiled, approved ABI or provider proof.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models
{
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
        private readonly Elsa.Workflows.Runtime.Core.Contracts.IWorkflowPreparationOwnerLease lease;
        internal WorkflowPreparationOwnerSession(
            Elsa.Workflows.Runtime.Core.Contracts.IWorkflowPreparationOwnerLease lease,
            Action requireActiveCallback)
        { this.lease = lease; this.requireActiveCallback = requireActiveCallback; }
        public WorkflowPreparationOwnerStamp Owner
        { get { requireActiveCallback(); return lease.Owner; } }
        // Lease getter must refuse until native Join and canonical reads succeed.
        public WorkflowPreparationDecision? Decision
        { get { requireActiveCallback(); return lease.Decision; } }
        public Elsa.Workflows.Runtime.Core.Contracts.IWorkflowPreparationOwnerLease Lease
        { get { requireActiveCallback(); return lease; } }
        public IReadOnlyList<WorkflowPreparationRoot> SelectedRoots
        { get { requireActiveCallback(); return lease.SelectedRoots; } }
        public void EnsureActive() => requireActiveCallback();
    }

    public sealed class WorkflowPreparationException : Exception
    {
        public WorkflowPreparationException(
            string code, WorkflowPreparationId id, Exception? inner = null)
            : base("Preparation retention failed (" + code + ").", inner)
        { Code = code; Id = id; }
        public string Code { get; }
        public WorkflowPreparationId Id { get; }
    }
    public sealed class WorkflowPreparationOutcomeUnknownException : Exception
    {
        public WorkflowPreparationOutcomeUnknownException(
            WorkflowPreparationId id, Exception inner)
            : base("Preparation retention commit outcome is unknown.", inner)
        { Id = id; }
        public WorkflowPreparationId Id { get; }
    }
}

namespace Elsa.Workflows.Runtime.Core.Contracts
{
    // Replacement, scoped. Caller cannot supply an issuer/lease/commit context.
    public interface IWorkflowPreparationRetention
    {
        ValueTask<WorkflowPreparationWriteResult> RetainAsync(
            WorkflowPreparationRetain command, CancellationToken cancellationToken = default);
        ValueTask<WorkflowPreparationWriteResult> CompleteAsync(
            WorkflowPreparationComplete command, CancellationToken cancellationToken = default);
    }

    // Replacement, scoped; called by the existing configured publication
    // coordinator factory. The actual configured native provider integration
    // admits this exact issuer instance/tuple; arbitrary interface implementations
    // or a caller-created lease are not registrations.
    public interface IWorkflowPreparationRetentionFactory
    {
        IWorkflowPreparationRetention Bind(IWorkflowPreparationOwnerIssuer configuredIssuer);
    }

    // Operation-scoped, produced by the configured canonical publication store
    // and bound actual Workflow/Agent adapter. Not a source-kind switch/registry.
    public interface IWorkflowPreparationOwnerIssuer
    {
        ValueTask ExecuteFencedAsync(
            WorkflowPreparationOwnerStamp expected,
            IWorkflowPreparationCommitParticipant participant,
            Func<IWorkflowPreparationOwnerLease, CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default);
    }
    // Only the configured issuer creates this object; callback-scoped, never a DTO.
    public interface IWorkflowPreparationOwnerLease
    {
        WorkflowPreparationOwnerStamp Owner { get; }
        // Both getters refuse before successful native Join/canonical reads.
        // SelectedRoots come from the actual typed adapter's WINNING persisted
        // prepared projection. They are never taken from the Retain command.
        WorkflowPreparationDecision? Decision { get; }
        IReadOnlyList<WorkflowPreparationRoot> SelectedRoots { get; }
        // Same-physical: stage real owner CAS in the participant's owned transaction.
        // Independent: verify the already-held owner guard and actual distinct tuple.
        ValueTask JoinAsync(
            IWorkflowPreparationCommitContext commit,
            CancellationToken cancellationToken = default);
        void EnsureCurrent();
    }

    // Implemented by the actual selected source-reference provider.
    public interface IWorkflowPreparationCommitParticipant
    {
        ValueTask<WorkflowPreparationWriteResult> ExecuteAsync(
            WorkflowPreparationOwnerSession owner,
            Func<IWorkflowPreparationCommitContext, CancellationToken,
                ValueTask<WorkflowPreparationWriteResult>> stage,
            CancellationToken cancellationToken = default);
    }
    // Actual provider frame/session identities are checked, not just these properties.
    public interface IWorkflowPreparationCommitContext
    {
        WorkflowPreparationOwnerSession Owner { get; }
        void EnsureActive();
    }

    // Existing source-reference reader inherits this additive contract.
    // Default methods throw: no false absence/empty page on legacy providers.
    public interface IWorkflowPreparationReferenceReader
    {
        ValueTask<WorkflowPreparationSelection> InspectPreparationAsync(
            WorkflowPreparationSelectionQuery query,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<WorkflowPreparationSelection>(new NotSupportedException());
        ValueTask<WorkflowPreparationSnapshot?> FindPreparationAsync(
            WorkflowPreparationId id, CancellationToken cancellationToken = default)
            => ValueTask.FromException<WorkflowPreparationSnapshot?>(new NotSupportedException());
        ValueTask<RuntimeStorePage<WorkflowPreparationSnapshot>> ListPreparationPageAsync(
            WorkflowPreparationPageQuery query, CancellationToken cancellationToken = default)
            => ValueTask.FromException<RuntimeStorePage<WorkflowPreparationSnapshot>>(new NotSupportedException());
        // Checks retained AND terminal history, preventing reference-less legacy
        // admission of material whose only provenance is preparation.
        ValueTask<bool> HasPreparationMaterialAsync(
            WorkflowPreparationRoot material, CancellationToken cancellationToken = default)
            => ValueTask.FromException<bool>(new NotSupportedException());
    }

    // Existing source-reference writer inherits this additive provider contract.
    public interface IWorkflowPreparationReferenceWriter
    {
        bool CanRetainPreparations(
            IWorkflowExecutableStore artifacts, IExecutableActivityTemplateStore templates,
            IWorkflowPreparationOwnerIssuer configuredIssuer) => false;
        ValueTask<WorkflowPreparationWriteResult> StageRetainPreparationAsync(
            WorkflowPreparationRetain command,
            IReadOnlyList<WorkflowExecutableRootWriteLease> grants,
            IWorkflowPreparationCommitContext commit,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<WorkflowPreparationWriteResult>(new NotSupportedException());
        ValueTask<WorkflowPreparationWriteResult> StageCompletePreparationAsync(
            WorkflowPreparationComplete command,
            IReadOnlyList<WorkflowExecutableRootWriteLease> grants,
            IWorkflowPreparationCommitContext commit,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<WorkflowPreparationWriteResult>(new NotSupportedException());
    }
}
