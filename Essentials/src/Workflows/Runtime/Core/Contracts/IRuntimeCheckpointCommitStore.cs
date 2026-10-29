using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeCheckpointCommitStore
{
    /// <summary>Whether model state and accepted input share this checkpoint's atomic authority.</summary>
    bool SupportsModelCheckpoint => false;

    /// <summary>Qualifies the actual scoped reader used by the selected model checkpoint provider.</summary>
    bool CanCommitModelState(IRuntimeModelExecutionReader reader) => false;

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
    ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default);
}
