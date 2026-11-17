using Elsa.Agent.Core.Exceptions;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Core.Contracts;

/// <summary>
/// Replacement contract: one explicitly selected journal backend per shell. Implementations must isolate the full resource scope,
/// preserve matching operation/deadline replay, and enforce expected state/fence/unexpired acceptance
/// in the database. A failed/unknown write must never be reported as a successful transition.
/// </summary>
public interface IAgentTurnStore
{
    /// <summary>Reports the configured storage profile without claiming live-provider execution.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<AgentJournalCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    /// <summary>Inserts or replays the identical native operation; conflicting requests change nothing.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<AgentTurnDocument> StageAsync(AgentTurnDocument candidate, CancellationToken cancellationToken);
    /// <summary>Atomically refuses new operation creation when disabled, while retaining matching existing-operation replay and delivery registration.</summary>
    /// <exception cref="AgentJournalException">A storage, record, disabled-creation or scope failure, retaining its cause.</exception>
    ValueTask<AgentTurnDocument> StageAsync(AgentTurnDocument candidate, bool allowNewOperation, CancellationToken cancellationToken);
    /// <summary>Reads only the exact handle and native identity in the caller's selected scope.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<AgentTurnDocument?> ReadAsync(string handle, AgentInvocationIdentity identity, CancellationToken cancellationToken);
    /// <summary>Reads conversation metadata only for the same verified workflow owner and exact agent version.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause.</exception>
    ValueTask<AgentConversationDocument?> ReadConversationAsync(string conversationId, AgentInvocationIdentity identity, CancellationToken cancellationToken);
    /// <summary>Scans one bounded page of content-free retention metadata in an explicit ordinary tenant/partition scope.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause.</exception>
    ValueTask<AgentRetentionPage> QueryRetentionAsync(AgentRetentionQuery query, CancellationToken cancellationToken);
    /// <summary>Conditionally removes only protected content from the unchanged settled candidate; preserves all operation, history and cancellation metadata.</summary>
    /// <remarks>The runtime must first verify native retention eligibility and all outstanding reader/application responsibilities.</remarks>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause.</exception>
    ValueTask<bool> PurgeContentAsync(AgentRetentionCandidate expected, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Claims an unexpired prepared turn, or safely takes over an expired pre-dispatch claim for the same native child or explicitly registered delivery. Expired dispatch becomes unknown.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<AgentTurnDocument?> ClaimAsync(string handle, AgentInvocationIdentity identity, string childInvocationId, string fence, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Binds one registered native delivery to its exact child for immutable accepted-result replay. It never changes conversation history.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause.</exception>
    ValueTask<AgentTurnDocument?> ReplayAcceptedAsync(string handle, AgentInvocationIdentity identity, string childInvocationId, CancellationToken cancellationToken);
    /// <summary>Records one exact accepted native delivery's process application without changing conversation history. Equivalent replay is idempotent; conflicting sources refuse.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause.</exception>
    ValueTask<AgentTurnDocument?> ApplyAsync(string handle, AgentTurnApplication application, CancellationToken cancellationToken);
    /// <summary>Atomically records exact-native-parent cancellation, including before turn staging. Accepted history and other deliveries are preserved.</summary>
    /// <exception cref="AgentJournalException">A storage, record, source-conflict or scope failure, retaining its cause.</exception>
    ValueTask<AgentCancellationReceipt> CancelAsync(AgentTurnCancellation cancellation, CancellationToken cancellationToken);
    /// <summary>Renews a current live claim/dispatch lease without extending the original operation deadline.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause.</exception>
    ValueTask<AgentTurnDocument?> RenewAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Persists dispatch responsibility under the live claim before inference can begin.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<bool> DispatchAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Reserves one physical model request before sending, under the live dispatch fence and finite limit.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause.</exception>
    ValueTask<AgentModelRequestReservation?> ReserveModelRequestAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease,
        AgentModelRequestReservation reservation, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Records response evidence for the exact request set and freezes further physical admission.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause.</exception>
    ValueTask<bool> CompleteModelResponseAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, IReadOnlyList<string> requestIds,
        AgentModelResponseObservation observation, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Atomically accepts the protected result/session pair only for its live dispatch fence.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<bool> AcceptAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, string protectedResult, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Settles a matching nonterminal claim; accepted results and other fences are preserved.</summary>
    /// <exception cref="AgentJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask FailAsync(string handle, AgentInvocationIdentity identity, AgentTurnLeaseToken lease, string code, bool outcomeUnknown, CancellationToken cancellationToken);
}
