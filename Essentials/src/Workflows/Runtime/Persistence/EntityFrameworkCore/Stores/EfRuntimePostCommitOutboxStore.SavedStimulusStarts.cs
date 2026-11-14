using Elsa.Persistence.EntityFramework;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfRuntimePostCommitOutboxStore
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
            EnsureCommittedSourceBoundary();
            if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Saved start admission needs a clean owned transaction.");
            var scope = accessContextAccessor.Current.RequireScope(); accessContextAccessor.Current.EnsureScope(authority.Scope);
            for (var attempt = 0; ; attempt++)
            {
                await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
                try
                {
                    var row = await LoadAsync(scope.Value, authority.SourceOutboxItemId, tracking: true, cancellationToken)
                        ?? throw new InvalidDataException("The saved source is missing.");
                    var current = ReadChecked(row, scope.Value, authority.SourceOutboxItemId);
                    var publisherRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(
                        item => item.Id == EfWorkflowExecutionStateStore.CreateId(scope.Value, current.RequireWorkflowIntent().WorkflowExecutionId), cancellationToken)
                        ?? throw new InvalidDataException("The saved publisher is missing.");
                    var publisher = EfWorkflowExecutionStateStore.ReadChecked(publisherRow, scope.Value, current.RequireWorkflowIntent().WorkflowExecutionId);
                    if (!NativeIngressCheckpointAuthority.HasBoundScope(publisher, scope.Value))
                        throw new InvalidDataException("The publisher has no protected source-bound scope.");
                    var material = await new EfWorkflowExecutableStore(context, accessContextAccessor).FindAsync(
                        candidate.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!.RequestedArtifactId, cancellationToken)
                        ?? throw new NotSupportedException("Saved starts need actual retained material.");
                    if (current.SubscriptionDeliveryProtocolVersion is not null)
                        RuntimeIngressAdmissionValidation.CheckSource(current, new(scope, publisher.TenantId));
                    var source = current.OpenSavedStartSource(authority, publisher, scope, timeProvider.GetUtcNow());
                    if (candidate.EnqueuedAt < source.Intent.RecordedAt || candidate.EnqueuedAt > timeProvider.GetUtcNow())
                        throw new InvalidDataException("The proposed saved start timestamp is outside its actual source delivery.");
                    SavedStimulusStartSeal.Validate(source, authority, candidate, StimulusAdapters, material, StimulusInputValidator);
                    if (current.SavedStimulusStarts?.ContainsKey(candidate.WorkflowExecutionId) == true)
                        return SavedStimulusStartSeal.Read(current, candidate.WorkflowExecutionId);
                    var installed = current.WithSavedStimulusStart(candidate);
                    Copy(row, installed, scope.Value, checked(row.Revision + 1), WriteVersion(installed));
                    await context.SaveChangesAsync(cancellationToken);
                    _ = current.OpenSavedStartSource(authority, publisher, scope, timeProvider.GetUtcNow());
                    cancellationToken.ThrowIfCancellationRequested();
                    accessContextAccessor.Current.EnsureScope(scope);
                    await transaction.CommitAsync(cancellationToken);
                    return SavedStimulusStartSeal.Read(installed, candidate.WorkflowExecutionId);
                }
                catch (DbUpdateConcurrencyException) when (attempt < 7)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    context.ChangeTracker.Clear();
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    context.ChangeTracker.Clear();
                    throw;
                }
            }
        }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        { throw new StimulusDeliveryBoundaryException(authority.SourceOutboxItemId, exception,
            EfRelationalExceptionClassifier.IsProviderFailure(exception) ||
            exception is not (InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException)); }
    }
}
