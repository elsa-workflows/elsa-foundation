using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Services;

/// <summary>Rechecks the physically committed, scope-bound source before a Jobs sender effect.</summary>
internal sealed class JobsNativeSourceVerifier(IPostCommitOutboxLookupStore lookup, IPersistenceAccessContextAccessor access, TimeProvider clock)
{
    internal async ValueTask VerifyAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            access.Current.EnsureScope(source.Scope);
            var current = await lookup.FindDurableAsync(source.SourceOutboxItemId, cancellationToken)
                ?? throw new InvalidDataException("The physical Jobs sender source is unavailable.");
            if (current.OutboxItemId != source.SourceOutboxItemId || current.StimulusDeliverySourceScope != source.Scope || current.StimulusDeliveryProtocolVersion is not null ||
                RuntimeCheckpointCommitFingerprint.ComputeIntent(current.Intent) != source.IntentFingerprint) throw JobsNativeBoundary.Invalid();
            if (current.Status != RuntimePostCommitOutboxStatus.Delivering || current.DeliveringOwnerId != source.Claim.OwnerId ||
                current.DeliveryFencingToken != source.Claim.FencingToken || current.DeliveryStartedAt != source.Claim.ClaimedAt ||
                current.DeliveryVisibleAfter is not { } visibleAfter || visibleAfter <= clock.GetUtcNow())
                throw new RuntimePostCommitOutboxStaleClaimException(current.OutboxItemId, source.Claim.OwnerId,
                    source.Claim.FencingToken, current.DeliveringOwnerId, current.DeliveryFencingToken, current.Status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (Exception error)
        {
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, error, isUnavailable: true);
        }
    }
}
