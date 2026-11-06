using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed class StandaloneTriggerDeliveryHandler(IRuntimeIngressDeliveryStore store,
    IEnumerable<IStandaloneTriggerRuntimeTarget> targets, TimeProvider clock, IWorkflowExecutableStartPolicy policy,
    IRuntimeIngressWaitTarget? waitTarget = null) : IRuntimeDeliverySourceHandler
{
    private readonly IReadOnlyDictionary<string, IStandaloneTriggerRuntimeTarget> targets = targets.ToDictionary(x => x.ModelKey, StringComparer.Ordinal);

    public async ValueTask HandleAsync(RuntimeDeliverySourceContext source, CancellationToken cancellationToken = default)
    {
        var item = await store.ReadSourceAsync(source, cancellationToken);
        var selection = item.SubscriptionDelivery ?? throw new InvalidDataException("A standalone source has no accepted cohort.");
        var failures = new List<Exception>();
        foreach (var recipient in selection.Recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            item = await store.ReadSourceAsync(source, cancellationToken);
            var progress = item.SubscriptionDelivery!.Progress.Single(x => x.RecipientId == recipient.RecipientId);
            if (progress.Phase is RuntimeRecipientPhase.TargetAccepted or RuntimeRecipientPhase.CancelledBeforeDispatch) continue;
            try
            {
                if (!targets.TryGetValue(recipient.ModelKey, out var target))
                    throw new NotSupportedException("The accepted target's runtime adapter is unavailable.");
                if (progress.Phase == RuntimeRecipientPhase.Pending)
                {
                    await CheckPolicyAsync(recipient, cancellationToken);
                    var proposed = await target.PrepareDispatchAsync(item, recipient, cancellationToken);
                    item = await store.CommitDispatchAsync(source, proposed, cancellationToken);
                    progress = item.SubscriptionDelivery!.Progress.Single(x => x.RecipientId == recipient.RecipientId);
                    // A concurrent cancellation can win while command preparation is still side-effect free.
                    if (progress.Phase == RuntimeRecipientPhase.CancelledBeforeDispatch) continue;
                }
                var seal = progress.Seal ?? throw new InvalidDataException("Committed dispatch responsibility has no seal.");
                var receipt = await target.ReconcileAsync(item, recipient, seal, cancellationToken);
                if (receipt is null)
                {
                    await CheckPolicyAsync(recipient, cancellationToken);
                    await target.DispatchAsync(item, recipient, seal, cancellationToken);
                    receipt = await target.ReconcileAsync(item, recipient, seal, cancellationToken);
                }
                if (receipt is null) throw new IOException("The target has not yet provided physical acceptance evidence.");
                item = await store.RecordReceiptAsync(source, receipt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Elsa.Workflows.Runtime.Core.Exceptions.RuntimePostCommitOutboxStaleClaimException) { throw; }
            catch (Exception exception)
            {
                failures.Add(exception);
                await store.RecordRecipientFailureAsync(source, recipient.RecipientId,
                    exception is NotSupportedException ? RuntimeRecipientRecoveryState.BlockedCapability :
                        exception is TriggerOperationException { Code: "target-start-policy-denied" } ? RuntimeRecipientRecoveryState.BlockedPolicy : RuntimeRecipientRecoveryState.RetryWaiting,
                    exception is NotSupportedException ? "target-capability-unavailable" :
                        exception is TriggerOperationException { Code: "target-start-policy-denied" } ? "target-start-policy-denied" : "target-delivery-unconfirmed",
                    clock.GetUtcNow().AddSeconds(5), cancellationToken);
            }
        }
        // Ordinary waits were captured before any new root could create another matching wait.
        // Their responsibility survives cancellation or failure of every selected new root.
        foreach (var recipient in selection.WaitDelivery?.Recipients ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            item = await store.ReadSourceAsync(source, cancellationToken);
            var progress = item.SubscriptionDelivery!.WaitDelivery!.Progress.Single(x => x.WaitId == recipient.WaitId);
            if (progress.Phase == RuntimeIngressWaitPhase.TargetAccepted) continue;
            try
            {
                if (waitTarget is null) throw new NotSupportedException("The ordinary wait runtime adapter is unavailable.");
                if (progress.Phase == RuntimeIngressWaitPhase.Pending)
                {
                    var proposed = await waitTarget.PrepareDispatchAsync(item, recipient, cancellationToken);
                    item = await store.CommitWaitDispatchAsync(source, proposed, cancellationToken);
                    progress = item.SubscriptionDelivery!.WaitDelivery!.Progress.Single(x => x.WaitId == recipient.WaitId);
                }
                var seal = progress.Seal ?? throw new InvalidDataException("Committed ordinary wait responsibility has no seal.");
                var receipt = await waitTarget.ReconcileAsync(item, recipient, seal, cancellationToken);
                if (receipt is null)
                {
                    await waitTarget.DispatchAsync(item, recipient, seal, cancellationToken);
                    receipt = await waitTarget.ReconcileAsync(item, recipient, seal, cancellationToken);
                }
                if (receipt is null) throw new IOException("The ordinary wait has no physical resume acceptance or positive retirement evidence.");
                item = await store.RecordWaitReceiptAsync(source, receipt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Elsa.Workflows.Runtime.Core.Exceptions.RuntimePostCommitOutboxStaleClaimException) { throw; }
            catch (Exception exception)
            {
                failures.Add(exception);
                await store.RecordWaitFailureAsync(source, recipient.WaitId,
                    exception is NotSupportedException ? RuntimeRecipientRecoveryState.BlockedCapability : RuntimeRecipientRecoveryState.RetryWaiting,
                    exception is NotSupportedException ? "wait-capability-unavailable" : "wait-delivery-unconfirmed",
                    clock.GetUtcNow().AddSeconds(5), cancellationToken);
            }
        }
        if (failures.Count != 0)
            throw new TriggerOperationException("accepted-delivery-pending", "One or more accepted deliveries still require recovery.",
                innerException: new AggregateException(failures));
        item = await store.ReadSourceAsync(source, cancellationToken);
        if (item.SubscriptionDelivery?.IsSettled != true)
            throw new InvalidDataException("The source cannot complete before every accepted recipient is transferred or cancelled.");
    }

    private async ValueTask CheckPolicyAsync(RuntimeSubscriptionRecipient recipient, CancellationToken ct)
    {
        var decision = await policy.EvaluateAsync(new(recipient.Material, WorkflowExecutableStartAuthorityKind.StandaloneTrigger,
            recipient.Authority.SystemIdentity, recipient.Authority, WorkflowRunKind.PublishedRun, recipient.TenantId, recipient.Partition), ct);
        if (!decision.IsAllowed) throw new TriggerOperationException("target-start-policy-denied", "The current host policy blocks this accepted recipient.");
    }
}
