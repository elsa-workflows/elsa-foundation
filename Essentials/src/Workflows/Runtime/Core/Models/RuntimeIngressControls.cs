using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

public enum RuntimeIngressControlKind { RetryRecipients, CancelPendingRecipients }
public enum RuntimeIngressControlOutcome { RetryScheduled, DeliveryInProgress, Cancelled, DispatchCommitted, AlreadyAccepted }
public sealed record RuntimeIngressRecipientExpectation(string RecipientId, string ProgressFingerprint);
public sealed record RuntimeIngressRecipientControlResult(string RecipientId, RuntimeIngressControlOutcome Outcome);
public sealed record RuntimeIngressControlRequest(TriggerScope Scope, string SourceOutboxItemId, string SourceFingerprint,
    string SelectionFingerprint, string TriggerId, string ActorId, string RequestId, string RequestFingerprint,
    RuntimeIngressControlKind Kind, IReadOnlyList<RuntimeIngressRecipientExpectation> Recipients)
{
    public long? ExpectedTriggerRevision { get; init; }
    public string ComputeContentFingerprint() => RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-control.v1", new
    {
        Scope, SourceOutboxItemId, SourceFingerprint, SelectionFingerprint, TriggerId,
        ActorId, RequestId, ExpectedTriggerRevision, Kind,
        Recipients = Recipients.OrderBy(item => item.RecipientId, StringComparer.Ordinal).ToArray()
    });
}
public sealed record RuntimeIngressControlReceipt(string ActorId, string RequestId, string RequestFingerprint,
    RuntimeIngressControlKind Kind, DateTimeOffset CommittedAt, IReadOnlyList<RuntimeIngressRecipientControlResult> Recipients)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContentFingerprint { get; init; }
}
public sealed record RuntimeIngressControlResult(RuntimePostCommitOutboxItem Item, RuntimeIngressControlReceipt Receipt, bool Replayed);
public sealed record RuntimeIngressDeliveryQuery(string? TriggerId = null, int Limit = 25, string? After = null);
public sealed record RuntimeIngressDeliveryPage(IReadOnlyList<RuntimePostCommitOutboxItem> Items, string? Next);

/// <summary>Provider-written operation and exchange facts retained on the same accepted source; never executable authority.</summary>
public sealed class RuntimeIngressControlFacts
{
    public RuntimeIngressControlFacts(IReadOnlyList<RuntimeIngressControlReceipt> receipts,
        IReadOnlyList<RuntimeIngressTransportObservation> transport)
    {
        ArgumentNullException.ThrowIfNull(receipts); ArgumentNullException.ThrowIfNull(transport);
        if (receipts.Any(item => item is null) || transport.Any(item => item is null) ||
            receipts.Select(item => (item.ActorId, item.RequestId)).Distinct().Count() != receipts.Count ||
            transport.GroupBy(item => item.ExchangeId, StringComparer.Ordinal).Any(group =>
                group.Count(item => item.State is not (RuntimeIngressTransportState.AwaitingResponse or RuntimeIngressTransportState.ReceiptUnknown)) > 1))
            throw new ArgumentException("Control and exchange facts must have distinct immutable identities.");
        Receipts = Array.AsReadOnly(receipts.ToArray()); Transport = Array.AsReadOnly(transport.ToArray());
    }
    public IReadOnlyList<RuntimeIngressControlReceipt> Receipts { get; }
    public IReadOnlyList<RuntimeIngressTransportObservation> Transport { get; }

    public void EnsurePreserves(RuntimeIngressControlFacts previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (previous.Receipts.Any(item => !Receipts.Any(next => Same(next, item))) ||
            previous.Transport.Any(item => !Transport.Any(next => Same(next, item))))
            throw new InvalidOperationException("Source operation and exchange facts cannot be erased or replaced.");
    }
    private static bool Same<T>(T left, T right) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left), JsonSerializer.SerializeToElement(right));
}

public static class RuntimeIngressInspection
{
    public static string ProgressFingerprint(RuntimeRecipientProgress progress) =>
        RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-progress.v1", progress);
    public static string DeliveryFingerprint(RuntimePostCommitOutboxItem item) =>
        RuntimeDeliveryFingerprint.Compute("elsa.runtime.ingress-inspection.v1", item);
    public static long RecipientRevision(RuntimeRecipientProgress progress) => checked(1 + progress.AttemptCount * 4L + (long)progress.Phase);
    public static long DeliveryRevision(RuntimeSubscriptionDelivery delivery) => checked(1 + delivery.Progress.Sum(RecipientRevision) +
        (delivery.ControlFacts?.Receipts.Count ?? 0) + (delivery.ControlFacts?.Transport.Count ?? 0));
}
