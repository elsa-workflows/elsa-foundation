using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

public sealed partial class InMemoryRuntimeCheckpointCommitStore
{
    internal IWorkflowExecutableInputValidator? StimulusInputValidator { get; set; }
    internal IEnumerable<IStimulusDeliveryIntentAdapter> StimulusAdapters { get; set; } = [];

    public async ValueTask<WorkflowExecutionCommandEnvelope> SealSavedStartAsync(WorkflowExecutableStimulusDeliveryAuthority authority,
        WorkflowExecutionCommandEnvelope candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority); ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await _state.ExecuteWriteGateAsync(async token =>
            {
                var access = _persistenceAccessContextAccessor?.Current ?? throw new NotSupportedException("Saved starts need explicit physical access.");
                var scope = access.RequireScope(); access.EnsureScope(authority.Scope);
                if (_retentionExecutions is null || !ReferenceEquals(_retentionExecutions.CheckpointState, _state))
                    throw new NotSupportedException("Saved starts need the actual physical publisher store.");
                RuntimePostCommitOutboxItem initial;
                lock (_state.SyncRoot)
                    initial = _state.OutboxItems.GetValueOrDefault(authority.SourceOutboxItemId) ?? throw new InvalidDataException("The saved source is missing.");
                var publisher = await _retentionExecutions.FindAsync(initial.RequireWorkflowIntent().WorkflowExecutionId, token)
                    ?? throw new InvalidDataException("The saved publisher is missing.");
                if (!NativeIngressCheckpointAuthority.HasBoundScope(publisher, scope.Value))
                    throw new InvalidDataException("The publisher has no protected source-bound scope.");
                var material = _retentionMaterial is null ? null : await _retentionMaterial.FindAsync(
                    candidate.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!.RequestedArtifactId, token);
                if (material is null) throw new NotSupportedException("Saved starts need actual retained material.");
                lock (_state.SyncRoot)
                {
                    var current = _state.OutboxItems.GetValueOrDefault(authority.SourceOutboxItemId) ?? throw new InvalidDataException("The saved source was removed.");
                    var source = current.OpenSavedStartSource(authority, publisher, scope, _timeProvider.GetUtcNow());
                    if (candidate.EnqueuedAt < source.Intent.RecordedAt || candidate.EnqueuedAt > _timeProvider.GetUtcNow())
                        throw new InvalidDataException("The proposed saved start timestamp is outside its actual source delivery.");
                    SavedStimulusStartSeal.Validate(source, authority, candidate, StimulusAdapters, material, StimulusInputValidator);
                    if (current.SavedStimulusStarts?.ContainsKey(candidate.WorkflowExecutionId) == true)
                        return SavedStimulusStartSeal.Read(current, candidate.WorkflowExecutionId);
                    token.ThrowIfCancellationRequested();
                    var installed = current.WithSavedStimulusStart(candidate);
                    _state.OutboxItems[authority.SourceOutboxItemId] = installed;
                    return SavedStimulusStartSeal.Read(installed, candidate.WorkflowExecutionId);
                }
            }, cancellationToken);
        }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        { throw new StimulusDeliveryBoundaryException(authority.SourceOutboxItemId, exception,
            exception is not (InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException)); }
    }
}
