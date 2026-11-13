using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfRuntimeCheckpointCommitStore
{
    private async ValueTask<RuntimePostCommitOutboxItem> ReadIngressCheckpointSourceAsync(PersistenceScope scope,
        string sourceOutboxItemId, CancellationToken cancellationToken)
    {
        var id = EfRuntimePostCommitOutboxStore.RowId(scope.Value, sourceOutboxItemId);
        var row = await context.RuntimePostCommitOutbox.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ??
            throw new InvalidDataException("The actual immutable accepted ingress source is unavailable.");
        var source = EfRuntimePostCommitOutboxStore.ReadChecked(row, scope.Value, sourceOutboxItemId);
        // Join the source row's CAS with this transaction. A concurrent cleanup or progress write must retry.
        row.SchemaVersion = RuntimeSchemaWriteVersion.For(context, RuntimePostCommitOutboxEfModule.SchemaFamily, RuntimePostCommitOutboxEfModule.SchemaVersion, row.SchemaVersion);
        row.Revision = checked(row.Revision + 1);
        return source;
    }

    private async ValueTask ValidateIngressWaitCheckpointAsync(RuntimeCheckpointCommit commit, PersistenceScope scope,
        CancellationToken cancellationToken)
    {
        if (commit.IngressWaitOrigin is not { } origin) return;
        if (origin.Scope != scope) throw new InvalidDataException("The saved wait checkpoint changed physical scope.");
        var source = await ReadIngressCheckpointSourceAsync(scope, origin.SourceOutboxItemId, cancellationToken);
        NativeIngressCheckpointAuthority.ValidateWait(commit, RuntimeIngressAdmissionValidation.CommittedWait(source, origin));
    }

    private async ValueTask StageIngressExecutionAuthorityAsync(RuntimeCheckpointCommit commit, string scope,
        CancellationToken cancellationToken)
    {
        if (commit.StateChanges.WorkflowExecution is not { Operation: RuntimeStateChangeOperation.Upsert } change) return;
        NativeIngressCheckpointAuthority.ValidateOriginShape(change.State);
        var origin = change.State.IngressOrigin;
        var saved = change.State.StimulusDeliveryOrigin;
        var workerOrigin = change.State.ModelWorkOrigin?.IngressOrigin;
        if (origin is null && workerOrigin is null && saved is null) return;
        if ((saved?.Scope ?? origin?.Scope ?? workerOrigin!.Scope).Value != scope) throw new InvalidDataException("The ingress workflow changed physical scope.");
        var id = EfWorkflowExecutionStateStore.CreateId(scope, commit.WorkflowExecutionId);
        var row = await context.WorkflowExecutionStates.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is not null)
        {
            var prior = EfWorkflowExecutionStateStore.ReadChecked(row, scope, commit.WorkflowExecutionId);
            NativeIngressCheckpointAuthority.ValidateContinuation(change.State, prior);
            // Normal workflow staging updates this same tracked row and keeps its original CAS revision.
            return;
        }
        if (commit.Checkpoint.Name != RuntimeCheckpointNames.WorkflowStarted)
            throw new InvalidDataException("Ingress workflow authority requires its exact initial physical checkpoint.");
        if (saved is not null)
        {
            var retained = await ReadIngressCheckpointSourceAsync(saved.Scope, saved.SourceOutboxItemId, cancellationToken);
            var savedMaterial = await new EfWorkflowExecutableStore(context, accessContextAccessor).FindAsync(change.State.PinnedExecutable.ArtifactId, cancellationToken)
                ?? throw new NotSupportedException("Saved stimulus admission needs its actual retained native material.");
            SavedStimulusCheckpointAuthority.ValidateInitial(commit, retained, saved.Scope, savedMaterial);
            return;
        }
        if (origin is null)
        {
            if (commit.ExpectedModelExecution?.IngressOrigin?.IsEquivalentTo(workerOrigin) != true)
                throw new InvalidDataException("The native worker requires its exact source-bound parent model admission.");
            return; // The model participant checks the actual parent row later in this same transaction.
        }
        var source = await ReadIngressCheckpointSourceAsync(origin.Scope, origin.SourceOutboxItemId, cancellationToken);
        var material = await new EfWorkflowExecutableStore(context, accessContextAccessor).FindAsync(change.State.PinnedExecutable.ArtifactId, cancellationToken)
            ?? throw new NotSupportedException("The ingress checkpoint requires its actual retained executable material.");
        NativeIngressCheckpointAuthority.ValidateInitialStart(commit, RuntimeIngressAdmissionValidation.Committed(source, origin), material);
    }
}
