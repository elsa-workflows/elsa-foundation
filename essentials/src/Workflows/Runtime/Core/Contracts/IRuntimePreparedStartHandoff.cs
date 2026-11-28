using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Physical Foundation evidence for one exact prepared actor.</summary>
public enum RuntimePreparedStartProof { Unresolved, Accepted, Disarmed }

/// <summary>Trusted host qualification port for reading evidence from the selected Foundation owner.</summary>
public interface IRuntimePreparedStartAttemptProof
{
    ValueTask<RuntimePreparedStartProof> ObserveAsync(TriggerScope scope, string actorReference, string issuance, CancellationToken cancellationToken);
}

/// <summary>Private host qualification for a sealed standalone recipient's prepared actor.</summary>
public interface IRuntimePreparedStartHandoff
{
    ValueTask<RuntimeCommittedRecipient?> CommitPreparedStartAsync(RuntimeDeliverySourceContext source, string recipientId,
        CancellationToken cancellationToken = default);
    ValueTask<(long Generation, bool Disarmed)?> ReadPreparedStartAttemptAsync(RuntimeCommittedRecipientAuthority authority,
        CancellationToken cancellationToken = default);
    ValueTask<long?> TryBindPreparedStartAsync(RuntimeDeliverySourceContext source, RuntimeCommittedRecipientAuthority authority,
        long previousGeneration, string tenant, string actorReference, string issuance, CancellationToken cancellationToken = default);
    ValueTask<RuntimePostCommitOutboxItem> ReconcilePreparedStartAsync(RuntimeDeliverySourceContext source,
        RuntimeCommittedRecipientAuthority authority, long generation, CancellationToken cancellationToken = default);
}

/// <summary>Opt-in source-owned delivery for a protected standalone root recipient.</summary>
public interface IRuntimePreparedStandaloneRecipient
{
    bool Selects(RuntimeStandaloneIngressSource source, RuntimeSubscriptionRecipient recipient);
    /// <summary>Validates new source admission while keeping the selected adapter stable for accepted sources.</summary>
    bool CanAdmit(RuntimeStandaloneIngressSource source, RuntimeSubscriptionRecipient recipient) => Selects(source, recipient);
    /// <summary>Completes a source-owned obligation even when every selected root was cancelled or already transferred.</summary>
    ValueTask PrepareSourceAsync(RuntimeDeliverySourceContext source, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    ValueTask<RuntimePostCommitOutboxItem> DeliverAsync(RuntimeDeliverySourceContext source, string recipientId, CancellationToken cancellationToken);
}

/// <summary>Reads the exact live execution named by a committed prepared standalone recipient receipt.</summary>
public interface IRuntimePreparedStandaloneRunReader
{
    ValueTask<WorkflowExecutionState?> ReadAsync(TriggerScope scope, RuntimeSubscriptionRecipient recipient,
        RuntimeTargetAcceptanceReceipt receipt, CancellationToken cancellationToken);
}
