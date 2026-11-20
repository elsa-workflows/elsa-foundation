using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeCheckpointCommitStore
{
    /// <summary>Whether a first fault atomically validates its accepted source, parked decision and ownership.</summary>
    bool SupportsInitialStartFaultAdmission => false;

    /// <summary>Whether history, its sequence head and lifecycle projections share the checkpoint's atomic boundary.</summary>
    bool SupportsHistory => false;

    /// <summary>Qualifies the actual scoped reader used by this atomic history writer, including shared backend/context/state ownership.</summary>
    bool CanCommitHistory(IRuntimeHistoryStore selectedStore) => false;

    /// <summary>Whether model state and accepted input share this checkpoint's atomic authority.</summary>
    bool SupportsModelCheckpoint => false;

    /// <summary>Qualifies the actual scoped reader used by the selected model checkpoint provider.</summary>
    bool CanCommitModelState(IRuntimeModelExecutionReader reader) => false;

    /// <summary>Whether initial model admission can atomically preserve and compare its exact live native parent wait.</summary>
    bool SupportsModelParentAdmission => false;

    /// <summary>Whether native admission can atomically compare its authorizing model execution snapshot.</summary>
    bool SupportsModelAdmission => false;

    /// <summary>Qualifies the physical model reader used to decide native worker admission.</summary>
    bool CanCommitModelAdmission(IRuntimeModelExecutionReader reader, IWorkflowExecutionStateStore executions,
        IActivityExecutionStateStore activities, IRuntimePostCommitOutboxStore outbox) => false;

    /// <summary>Whether this implementation can atomically enforce exact physical bookmark resume conditions.</summary>
    /// <remarks>Callers must qualify selected stores; legacy providers and decorators refuse by default.</remarks>
    bool SupportsExactBookmarkResume => false;

    /// <summary>Qualify the selected physical bookmark/execution/activity stores for exact resume checkpoints.</summary>
    bool CanCommitExactBookmarkResume(IBookmarkStateStore bookmarks, IWorkflowExecutionStateStore physicalExecutions,
        IActivityExecutionStateStore activities) => false;

    /// <summary>
    /// Commits one provider-facing checkpoint. An existing equivalent commit marker takes replay precedence. For a
    /// new commit, <see cref="RuntimeCheckpointCommit.ExpectedFence"/> must be validated atomically with every state
    /// change, outbox item, and create-only marker; stale fences expose no partial checkpoint outcome.
    /// </summary>
    /// <remarks>
    /// A non-null <see cref="RuntimeCheckpointCommit.ExpectedInitialStartFault"/> requires the actual parked initial work,
    /// accepted source and live ownership fence to be validated atomically with the first terminal state and incident.
    /// The default capability is false; unsupported providers must refuse this condition without writing.
    /// </remarks>
    /// <exception cref="Elsa.Workflows.Runtime.Core.Exceptions.RuntimeCheckpointCommitValidationException">The conditional first fault does not match its physical source or parked decision. The exception retains the cause and execution, work and commit identities.</exception>
    ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default);
}
