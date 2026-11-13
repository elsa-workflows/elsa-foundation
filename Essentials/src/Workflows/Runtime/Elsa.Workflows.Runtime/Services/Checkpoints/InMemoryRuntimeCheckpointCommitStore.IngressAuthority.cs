using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

public sealed partial class InMemoryRuntimeCheckpointCommitStore
{
    private void ValidateIngressWaitCheckpoint(RuntimeCheckpointCommit commit, PersistenceScope scope)
    {
        if (commit.IngressWaitOrigin is not { } origin) return;
        if (origin.Scope != scope) throw new InvalidDataException("The saved wait checkpoint changed physical scope.");
        RuntimePostCommitOutboxItem source;
        lock (_state.SyncRoot)
            source = _state.OutboxItems.GetValueOrDefault(origin.SourceOutboxItemId) ??
                throw new InvalidDataException("The actual accepted saved-wait source is unavailable.");
        NativeIngressCheckpointAuthority.ValidateWait(commit, RuntimeIngressAdmissionValidation.CommittedWait(source, origin));
    }

    private async ValueTask ValidateIngressExecutionAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        if (commit.StateChanges.WorkflowExecution is not { Operation: RuntimeStateChangeOperation.Upsert } change) return;
        var proposed = change.State;
        var executions = ExactResumeExecutions;
        var prior = executions is null ? null : await executions.FindAsync(proposed.WorkflowExecutionId, cancellationToken);
        if (prior?.IngressOrigin is not null || prior?.ModelWorkOrigin?.IngressOrigin is not null)
        {
            NativeIngressCheckpointAuthority.ValidateContinuation(proposed, prior);
            return;
        }
        if (proposed.ModelWorkOrigin?.IngressOrigin is not null)
            throw new NotSupportedException("The native checkpoint writer does not support initial ingress model-worker admission.");
        if (proposed.IngressOrigin is not { } origin) return;
        if (executions is null || !ReferenceEquals(executions.CheckpointState, _state))
            throw new NotSupportedException("Ingress workflow checkpoints require the actual selected native execution writer.");
        if (prior is not null || commit.Checkpoint.Name != RuntimeCheckpointNames.WorkflowStarted ||
            _persistenceAccessContextAccessor?.Current.RequireScope() != origin.Scope)
            throw new InvalidDataException("Ingress workflow authority requires its exact initial physical checkpoint.");
        RuntimePostCommitOutboxItem source;
        lock (_state.SyncRoot)
            source = _state.OutboxItems.GetValueOrDefault(origin.SourceOutboxItemId) ??
                throw new InvalidDataException("The actual accepted ingress source is unavailable.");
        var material = _retentionMaterial is null ? null : await _retentionMaterial.FindAsync(proposed.PinnedExecutable.ArtifactId, cancellationToken);
        if (material is null) throw new NotSupportedException("The ingress checkpoint requires its actual retained executable material.");
        NativeIngressCheckpointAuthority.ValidateInitialStart(commit, RuntimeIngressAdmissionValidation.Committed(source, origin), material);
    }
}
