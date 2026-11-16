using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>The checked physical standalone ingress participant in the selected ordinary Runtime outbox family.</summary>
public interface IRuntimeIngressDeliveryStore
{
    /// <summary>True only for one coherent selected resource, publication, material, retention and outbox provider boundary.</summary>
    bool SupportsAdmission => false;
    bool SupportsInspection => false;
    bool SupportsControls => false;
    bool SupportsWorkflowCohort(string intentKind) => false;

    ValueTask<RuntimeIngressDeliveryPage> ListDeliveriesAsync(TriggerScope scope, RuntimeIngressDeliveryQuery query,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeIngressDeliveryPage>(new NotSupportedException("The selected provider cannot inspect physical ingress deliveries."));
    ValueTask<RuntimePostCommitOutboxItem?> FindDeliveryAsync(TriggerScope scope, string sourceOutboxItemId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimePostCommitOutboxItem?>(new NotSupportedException("The selected provider cannot inspect physical ingress deliveries."));
    ValueTask<RuntimeIngressControlResult> ControlAsync(RuntimeIngressControlRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeIngressControlResult>(new NotSupportedException("The selected provider cannot control physical ingress deliveries."));

    /// <summary>Reads a prior authorized source key before mutable resource/publication checks. Changed submitted intent conflicts.</summary>
    ValueTask<RuntimeIngressAdmissionReceipt?> FindAcceptedAsync(TriggerScope scope, string sourceOutboxItemId,
        string requestFingerprint, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeIngressAdmissionReceipt?>(new NotSupportedException("The selected provider cannot read checked ingress acceptance."));

    /// <summary>Atomically accepts one complete nonempty cohort after exact physical observations and root leases are fenced through commit.</summary>
    ValueTask<RuntimeIngressAdmissionReceipt> AdmitAsync(RuntimeIngressAdmissionRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeIngressAdmissionReceipt>(new NotSupportedException("The selected provider cannot atomically admit standalone ingress."));

    /// <summary>Freezes ordinary waits/embedded starts and independently managed subscriptions in one physical workflow source commit.</summary>
    ValueTask<PostCommitDeliveryContext> InstallWorkflowCohortAsync(PostCommitDeliveryContext source,
        StimulusDeliverySnapshot ordinary, RuntimeWorkflowSubscriptionCandidate subscriptions, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<PostCommitDeliveryContext>(new NotSupportedException("The selected provider cannot accept a complete combined workflow-source cohort."));

    /// <summary>Reads current physical source state under the current source owner/fence and an unexpired delivery lease.</summary>
    ValueTask<RuntimePostCommitOutboxItem> ReadSourceAsync(RuntimeDeliverySourceContext source,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimePostCommitOutboxItem>(new NotSupportedException("The selected provider cannot read checked ingress delivery."));

    /// <summary>Commits irrevocable enqueue responsibility before any target enqueue, while this source claim remains current.</summary>
    ValueTask<RuntimePostCommitOutboxItem> CommitDispatchAsync(RuntimeDeliverySourceContext source, RuntimeDispatchSeal seal,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimePostCommitOutboxItem>(new NotSupportedException("The selected provider cannot commit ingress dispatch."));

    /// <summary>Records only independently observed actual deterministic target acceptance under the current source claim.</summary>
    ValueTask<RuntimePostCommitOutboxItem> RecordReceiptAsync(RuntimeDeliverySourceContext source, RuntimeTargetAcceptanceReceipt receipt,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimePostCommitOutboxItem>(new NotSupportedException("The selected provider cannot verify ingress target acceptance."));

    /// <summary>Reads the immutable actual seal without relying on an expiring source claim or today's resource/publication state.</summary>
    ValueTask<RuntimeCommittedRecipient> ReadCommittedRecipientAsync(RuntimeCommittedRecipientAuthority authority,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeCommittedRecipient>(new NotSupportedException("The selected provider cannot read committed ingress authority."));

    /// <summary>Resolves a queued command's non-circular source origin against the actual immutable physical seal.</summary>
    ValueTask<RuntimeCommittedRecipient> ReadCommittedRecipientAsync(RuntimeIngressCommandOrigin origin,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeCommittedRecipient>(new NotSupportedException("The selected provider cannot read committed ingress command authority."));

    /// <summary>Records typed sanitized operational recovery without revoking a dispatch seal or changing the frozen selection.</summary>
    ValueTask<RuntimePostCommitOutboxItem> RecordRecipientFailureAsync(RuntimeDeliverySourceContext source, string recipientId,
        RuntimeRecipientRecoveryState recoveryState, string code, DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimePostCommitOutboxItem>(new NotSupportedException("The selected provider cannot record ingress recipient recovery."));

    ValueTask<RuntimePostCommitOutboxItem> CommitWaitDispatchAsync(RuntimeDeliverySourceContext source, RuntimeIngressWaitDispatchSeal seal,
        CancellationToken cancellationToken = default) => ValueTask.FromException<RuntimePostCommitOutboxItem>(new NotSupportedException("The selected provider cannot commit ingress wait dispatch."));
    ValueTask<RuntimePostCommitOutboxItem> RecordWaitReceiptAsync(RuntimeDeliverySourceContext source, RuntimeIngressWaitReceipt receipt,
        CancellationToken cancellationToken = default) => ValueTask.FromException<RuntimePostCommitOutboxItem>(new NotSupportedException("The selected provider cannot verify ingress wait acceptance."));
    ValueTask<RuntimeCommittedIngressWait> ReadCommittedWaitAsync(RuntimeIngressWaitOrigin origin,
        CancellationToken cancellationToken = default) => ValueTask.FromException<RuntimeCommittedIngressWait>(new NotSupportedException("The selected provider cannot read committed ingress wait authority."));
    ValueTask<RuntimePostCommitOutboxItem> RecordWaitFailureAsync(RuntimeDeliverySourceContext source, string waitId,
        RuntimeRecipientRecoveryState recoveryState, string code, DateTimeOffset? retryAt, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimePostCommitOutboxItem>(new NotSupportedException("The selected provider cannot record ingress wait recovery."));

    ValueTask<RuntimeIngressCancellationReceipt> CancelPendingAsync(RuntimeIngressCancellationRequest request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeIngressCancellationReceipt>(new NotSupportedException("The selected provider cannot cancel pending ingress recipients."));
}
