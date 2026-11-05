using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Verifies this module's captured source against the physical provider before observations or effects.</summary>
internal static class WorkflowDispatchHistorySource
{
    internal static async ValueTask<PostCommitDeliveryContext> ValidateAsync(PostCommitDeliveryContext source,
        IServiceProvider? services, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (source.Intent.HistoryCausation is not { } cause) return source;
        try
        {
            if (services is null) throw new NotSupportedException("The captured source receiver is unavailable.");
            if (string.IsNullOrWhiteSpace(cause.WorkflowExecutionId) || string.IsNullOrWhiteSpace(cause.HistoryEventId) || cause.DeliveryId != source.SourceOutboxItemId)
                throw new InvalidDataException("The captured delivery requires its exact stamped source origin.");
            var access = (services.GetService<IPersistenceAccessContextAccessor>() ??
                throw new NotSupportedException("The captured source requires explicit persistence access.")).Current;
            if (access.RequireScope() != source.Scope) throw new InvalidDataException("The captured source changed physical scope.");
            var physical = await (services.GetService<IPostCommitOutboxLookupStore>() ??
                throw new NotSupportedException("The captured delivery requires a durable source reader."))
                .FindDurableAsync(source.SourceOutboxItemId, cancellationToken)
                ?? throw new InvalidDataException("The exact committed dispatch source is missing.");
            if (physical.OutboxItemId != source.SourceOutboxItemId || physical.StimulusDeliverySourceScope != source.Scope ||
                RuntimeCheckpointCommitFingerprint.ComputeIntent(physical.Intent) != source.IntentFingerprint)
                throw new InvalidDataException("The captured dispatch source differs from its committed intent or scope.");
            if (physical.Status != RuntimePostCommitOutboxStatus.Delivering || physical.DeliveringOwnerId != source.Claim.OwnerId ||
                physical.DeliveryFencingToken != source.Claim.FencingToken || physical.DeliveryVisibleAfter is null ||
                physical.DeliveryVisibleAfter <= clock.GetUtcNow())
                throw new RuntimePostCommitOutboxStaleClaimException(physical.OutboxItemId, source.Claim.OwnerId,
                    source.Claim.FencingToken, physical.DeliveringOwnerId, physical.DeliveryFencingToken, physical.Status);
            return new(new(physical, source.Claim.OwnerId, source.Claim.FencingToken,
                physical.DeliveryStartedAt ?? throw new InvalidDataException("The committed dispatch source has no claim start."),
                physical.DeliveryVisibleAfter.Value), source.Scope);
        }
        catch (RuntimePostCommitSourceBoundaryException) { throw; }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException)
        { throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, exception, false); }
        catch (Exception exception)
        { throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, exception, true); }
    }
}
