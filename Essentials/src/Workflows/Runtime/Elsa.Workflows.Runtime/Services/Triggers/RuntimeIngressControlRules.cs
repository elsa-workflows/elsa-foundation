using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal static class RuntimeIngressControlRules
{
    internal static RuntimeIngressControlResult? Replay(RuntimePostCommitOutboxItem item, RuntimeIngressControlRequest request)
    {
        RuntimeIngressAdmissionValidation.CheckSource(item, request.Scope);
        var delivery = item.SubscriptionDelivery!;
        if (item.SourceFingerprint != request.SourceFingerprint || delivery.SelectionFingerprint != request.SelectionFingerprint ||
            !BelongsTo(item, request.TriggerId)) throw Conflict();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActorId); ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestFingerprint);
        ArgumentNullException.ThrowIfNull(request.Recipients);
        if (request.Recipients.Any(item => item is null))
            throw new TriggerOperationException("validation", "Control recipients must be known, distinct identities.");
        var facts = delivery.ControlFacts ?? new([], []);
        var old = facts.Receipts.SingleOrDefault(receipt => receipt.ActorId == request.ActorId && receipt.RequestId == request.RequestId);
        if (old is not null)
        {
            if (old.RequestFingerprint != request.RequestFingerprint || old.Kind != request.Kind || old.ContentFingerprint != ContentFingerprint(request)) throw Conflict();
            return new(item, old, true);
        }
        return null;
    }

    internal static RuntimeIngressControlResult Apply(RuntimePostCommitOutboxItem item, RuntimeIngressControlRequest request, DateTimeOffset now)
    {
        if (Replay(item, request) is { } replay) return replay;
        var delivery = item.SubscriptionDelivery!;
        var facts = delivery.ControlFacts ?? new([], []);
        if (!Enum.IsDefined(request.Kind) || request.Recipients.Count == 0 || request.Recipients.Count > 500 ||
            request.Recipients.Select(recipient => recipient.RecipientId).Distinct(StringComparer.Ordinal).Count() != request.Recipients.Count)
            throw new TriggerOperationException("validation", "Choose a nonempty, distinct recipient set.");
        foreach (var expected in request.Recipients)
        {
            var recipient = delivery.Recipients.SingleOrDefault(recipient => recipient.RecipientId == expected.RecipientId);
            var progress = delivery.Progress.SingleOrDefault(progress => progress.RecipientId == expected.RecipientId);
            if (recipient is null || progress is null || !BelongsTo(item, recipient, request.TriggerId) ||
                RuntimeIngressInspection.ProgressFingerprint(progress) != expected.ProgressFingerprint) throw Conflict();
        }
        var outcomes = new List<RuntimeIngressRecipientControlResult>();
        var retry = false;
        foreach (var expected in request.Recipients)
        {
            var progress = delivery.Progress.Single(progress => progress.RecipientId == expected.RecipientId);
            RuntimeIngressControlOutcome outcome;
            if (request.Kind == RuntimeIngressControlKind.CancelPendingRecipients)
            {
                outcome = progress.Phase switch
                {
                    RuntimeRecipientPhase.Pending or RuntimeRecipientPhase.CancelledBeforeDispatch => RuntimeIngressControlOutcome.Cancelled,
                    RuntimeRecipientPhase.TargetAccepted => RuntimeIngressControlOutcome.AlreadyAccepted,
                    _ => RuntimeIngressControlOutcome.DispatchCommitted
                };
                delivery = delivery.WithProgress(progress.CancelPending());
            }
            else if (progress.Phase == RuntimeRecipientPhase.TargetAccepted) outcome = RuntimeIngressControlOutcome.AlreadyAccepted;
            else if (progress.Phase == RuntimeRecipientPhase.CancelledBeforeDispatch) outcome = RuntimeIngressControlOutcome.Cancelled;
            else if (item.Status == RuntimePostCommitOutboxStatus.Delivering && item.DeliveryVisibleAfter > now)
                outcome = RuntimeIngressControlOutcome.DeliveryInProgress;
            else
            {
                retry = true; outcome = RuntimeIngressControlOutcome.RetryScheduled;
                delivery = delivery.WithProgress(new(progress.RecipientId, progress.Phase, progress.Seal, progress.Receipt,
                    progress.AttemptCount, lastFailureCode: progress.LastFailureCode, recoveryState: RuntimeRecipientRecoveryState.None));
            }
            outcomes.Add(new(expected.RecipientId, outcome));
        }
        var receipt = new RuntimeIngressControlReceipt(request.ActorId, request.RequestId, request.RequestFingerprint, request.Kind, now, outcomes.AsReadOnly())
        { ContentFingerprint = ContentFingerprint(request) };
        delivery = delivery.WithControlFacts(new(facts.Receipts.Append(receipt).ToArray(), facts.Transport));
        var replacement = item.WithSubscriptionDelivery(delivery);
        if (retry) replacement = Reopen(replacement, now);
        else if (item.StandaloneIngress is not null && delivery.Progress.All(progress => progress.Phase == RuntimeRecipientPhase.CancelledBeforeDispatch) &&
            (delivery.WaitDelivery?.Recipients.Count ?? 0) == 0) replacement = CancelSource(replacement);
        return new(replacement, receipt, false);
    }

    internal static RuntimePostCommitOutboxItem Observe(RuntimePostCommitOutboxItem item, TriggerScope scope,
        RuntimeIngressTransportObservation observation)
    {
        RuntimeIngressAdmissionValidation.CheckSource(item, scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.ExchangeId);
        if (observation.SourceOutboxItemId != item.OutboxItemId || !Enum.IsDefined(observation.State) ||
            observation.ObservedAt == default || observation.ObservedAt == DateTimeOffset.MaxValue ||
            observation.StatusCode is < 100 or > 599 || observation.ExchangeId.Length > 128)
            throw new ArgumentException("The transport observation is invalid.", nameof(observation));
        var delivery = item.SubscriptionDelivery!;
        var facts = delivery.ControlFacts ?? new([], []);
        var history = facts.Transport.Where(previous => previous.ExchangeId == observation.ExchangeId).ToArray();
        if (history.Any(previous => previous.State == observation.State && previous.StatusCode == observation.StatusCode)) return item;
        if (history.Any(previous => previous.State is not (RuntimeIngressTransportState.AwaitingResponse or RuntimeIngressTransportState.ReceiptUnknown))) return item;
        return item.WithSubscriptionDelivery(delivery.WithControlFacts(new(facts.Receipts, facts.Transport.Append(observation).ToArray())));
    }

    internal static bool BelongsTo(RuntimePostCommitOutboxItem item, string triggerId) => item.StandaloneIngress?.TriggerId == triggerId ||
        item.SubscriptionDelivery?.ResourceCaptures?.Any(capture => capture.TriggerId == triggerId) == true;
    internal static bool BelongsTo(RuntimePostCommitOutboxItem item, RuntimeSubscriptionRecipient recipient, string triggerId) =>
        item.StandaloneIngress?.TriggerId == triggerId || item.SubscriptionDelivery?.ResourceCaptures?.Any(capture =>
            capture.TriggerId == triggerId && capture.SubscriptionIds.Contains(recipient.SubscriptionId, StringComparer.Ordinal)) == true;

    internal static RuntimePostCommitOutboxItem CancelSource(RuntimePostCommitOutboxItem item) => Copy(item, RuntimePostCommitOutboxStatus.Cancelled,
        null, checked(item.DeliveryFencingToken + 1));
    private static RuntimePostCommitOutboxItem Reopen(RuntimePostCommitOutboxItem item, DateTimeOffset now) => Copy(item,
        RuntimePostCommitOutboxStatus.Pending, now, checked(item.DeliveryFencingToken + 1));

    private static string ContentFingerprint(RuntimeIngressControlRequest request) => request.ComputeContentFingerprint();

    private static RuntimePostCommitOutboxItem Copy(RuntimePostCommitOutboxItem item, RuntimePostCommitOutboxStatus status,
        DateTimeOffset? availableAt, long fence) => new(item.OutboxItemId, item.Intent, status, item.RecordedAt, availableAt,
        item.RetryPolicy, item.DeliveryAttemptCount, null, null, item.DeliveredAt, item.LastFailureMessage, item.Metadata,
        fence, null, item.StimulusDeliveryProtocolVersion, item.StimulusDeliverySourceScope, item.RecipientSnapshot,
        item.StandaloneIngress, item.SubscriptionDeliveryProtocolVersion, item.SubscriptionDelivery);
    private static TriggerOperationException Conflict() => new("conflict", "The delivery or reviewed recipient state changed; reload it.");
}
