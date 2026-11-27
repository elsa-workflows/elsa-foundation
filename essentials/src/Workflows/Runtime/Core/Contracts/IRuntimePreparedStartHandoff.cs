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
    ValueTask<RuntimeCommittedRecipient> CommitPreparedStartAsync(RuntimeDeliverySourceContext source, string recipientId,
        CancellationToken cancellationToken = default);
    ValueTask<(long Generation, bool Disarmed)?> ReadPreparedStartAttemptAsync(RuntimeCommittedRecipientAuthority authority,
        CancellationToken cancellationToken = default);
    ValueTask<long?> TryBindPreparedStartAsync(RuntimeDeliverySourceContext source, RuntimeCommittedRecipientAuthority authority,
        long previousGeneration, string tenant, string actorReference, string issuance, CancellationToken cancellationToken = default);
    ValueTask<RuntimePostCommitOutboxItem> ReconcilePreparedStartAsync(RuntimeDeliverySourceContext source,
        RuntimeCommittedRecipientAuthority authority, long generation, CancellationToken cancellationToken = default);
}
