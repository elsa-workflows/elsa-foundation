using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeIngressDeliveryStore
{
    public ValueTask<RuntimeCommittedIngressWait> ReadCommittedWaitAsync(RuntimeIngressWaitOrigin origin, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => ReadCommittedWaitAsyncCore(origin, cancellationToken), origin?.Scope.Value, origin?.SourceOutboxItemId);

    private async ValueTask<RuntimeIngressWaitDelivery?> PrepareWaitsAsync(RuntimeIngressAdmissionRequest request, CancellationToken token)
    {
        if (request.WaitCandidate is not { } candidate) return null;
        var source = request.Candidate.StandaloneIngress!;
        if (PreparedStandaloneOnly)
        {
            if (!PreparedStandaloneHttp.HasEmptyWaitObservation(source, candidate))
                throw new NotSupportedException("The selected HTTP source requires an empty ordinary wait cohort.");
            return new(RuntimeIngressWaitDelivery.CurrentVersion, source.OutboxItemId, source.Fingerprint, source.Scope,
                time.GetUtcNow(), candidate, [], []);
        }
        return await RequireLegacy().CaptureWaitsAsync(request, token);
    }

    private async ValueTask FenceWaitsAsync(RuntimeIngressAdmissionRequest request, CancellationToken token)
    {
        if (request.Candidate.SubscriptionDelivery!.WaitDelivery is not { } expected) return;
        if (PreparedStandaloneOnly)
        {
            // Refuse any matching physical row. This is the same indexed range as the legacy bookmark reader,
            // sampled in the source's serializable resource/recipient commit before any actor can start.
            if (!PreparedStandaloneHttp.HasVerifiedEmptyWaits(request.Candidate) ||
                await EfBookmarkStimulusEvidence.HasMatchingRowAsync(context, request.Scope.Persistence.Value,
                    expected.Candidate.StimulusType, expected.Candidate.StimulusHash, token))
                throw new TriggerOperationException("unavailable", "The standalone HTTP route has an ordinary wait registration that this owner cannot resume.");
            return;
        }
        await RequireLegacy().FenceWaitsAsync(request, expected, token);
    }

    public ValueTask<RuntimePostCommitOutboxItem> CommitWaitDispatchAsync(RuntimeDeliverySourceContext source, RuntimeIngressWaitDispatchSeal seal,
        CancellationToken cancellationToken = default)
    {
        RequireLegacy().RequireWaitBoundary();
        return MutateAsync(source, item =>
        {
            var target = RuntimeIngressAdmissionValidation.ProposedWait(item, seal);
            if (seal.CommittedAt > time.GetUtcNow()) throw new InvalidDataException("The wait commitment cannot be in the future.");
            RuntimeIngressWaitCommand.ValidateEnvelope(target, RuntimeIngressWaitCommand.ReadEnvelope(seal));
            var waits = item.SubscriptionDelivery!.WaitDelivery!;
            return item.WithSubscriptionDelivery(item.SubscriptionDelivery.WithWaitDelivery(waits.WithProgress(
                waits.Progress.Single(progress => progress.WaitId == seal.Identity.WaitId).CommitDispatch(seal))));
        }, cancellationToken);
    }

    private async ValueTask<RuntimeCommittedIngressWait> ReadCommittedWaitAsyncCore(RuntimeIngressWaitOrigin origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        RequireScope(origin.Scope, cancellationToken); RequireOwnedBoundary();
        RequireLegacy();
        var row = await LoadAsync(origin.Scope, origin.SourceOutboxItemId, false, cancellationToken) ??
            throw new InvalidDataException("The physical accepted wait source is missing.");
        return RuntimeIngressAdmissionValidation.CommittedWait(Read(row, origin.Scope, origin.SourceOutboxItemId), origin);
    }

    public ValueTask<RuntimePostCommitOutboxItem> RecordWaitReceiptAsync(RuntimeDeliverySourceContext source, RuntimeIngressWaitReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        RequireScope(source.Scope, cancellationToken); RequireOwnedBoundary();
        var ordinary = RequireLegacy();
        ordinary.RequireWaitBoundary();
        return ChangeObservedAsync(source.Scope, source.SourceOutboxItemId, async (current, token) =>
        {
            RuntimeIngressAdmissionValidation.CheckClaim(current, source, time.GetUtcNow());
            var target = RuntimeIngressAdmissionValidation.WaitReceiptTarget(current, receipt);
            if (current.SubscriptionDelivery!.WaitDelivery!.Progress.Single(progress => progress.WaitId == receipt.WaitId).Receipt is not null) return current;
            var observed = await ordinary.ObserveWaitAsync(target, token) ??
                throw new TriggerOperationException("unavailable", "The ordinary wait has no observed physical resume or positive retirement; delivery remains owed.");
            return RuntimeIngressAdmissionValidation.AcceptObservedWait(current, observed);
        }, cancellationToken, source);
    }

    public ValueTask<RuntimePostCommitOutboxItem> RecordWaitFailureAsync(RuntimeDeliverySourceContext source, string waitId,
        RuntimeRecipientRecoveryState recoveryState, string code, DateTimeOffset? retryAt, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() =>
        {
            RequireLegacy();
            return MutateCore(source,
                item => RuntimeIngressAdmissionValidation.RecordWaitFailure(item, waitId, recoveryState, code, retryAt), cancellationToken);
        }, source?.Scope.Value, source?.SourceOutboxItemId);
}
