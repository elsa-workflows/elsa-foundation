using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

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
