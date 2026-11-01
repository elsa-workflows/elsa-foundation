using Elsa.Agent.Specialists.Core.Exceptions;
using Elsa.Agent.Specialists.Core.Models;

namespace Elsa.Agent.Specialists.Core.Contracts;

/// <summary>
/// Replacement contract: one explicitly selected journal backend per shell. Implementations must isolate the full resource scope,
/// preserve matching operation/deadline replay, and enforce expected state/fence/unexpired acceptance
/// in the database. A failed/unknown write must never be reported as a successful transition.
/// </summary>
public interface ISpecialistTurnStore
{
    /// <summary>Reports the configured storage profile without claiming live-provider execution.</summary>
    /// <exception cref="SpecialistJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<SpecialistJournalCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    /// <summary>Inserts or replays the identical native operation; conflicting requests change nothing.</summary>
    /// <exception cref="SpecialistJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<SpecialistTurnDocument> StageAsync(SpecialistTurnDocument candidate, CancellationToken cancellationToken);
    /// <summary>Reads only the exact handle and native identity in the caller's selected scope.</summary>
    /// <exception cref="SpecialistJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<SpecialistTurnDocument?> ReadAsync(string handle, SpecialistInvocationIdentity identity, CancellationToken cancellationToken);
    /// <summary>Reads conversation metadata only for the same verified workflow owner and exact specialist version.</summary>
    /// <exception cref="SpecialistJournalException">A storage, record or scope failure, retaining its cause.</exception>
    ValueTask<SpecialistConversationDocument?> ReadConversationAsync(string conversationId, SpecialistInvocationIdentity identity, CancellationToken cancellationToken);
    /// <summary>Claims a prepared, unexpired turn once and binds its native child and journal fence.</summary>
    /// <exception cref="SpecialistJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<SpecialistTurnDocument?> ClaimAsync(string handle, SpecialistInvocationIdentity identity, string childInvocationId, string fence, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Persists dispatch responsibility under the live claim before inference can begin.</summary>
    /// <exception cref="SpecialistJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<bool> DispatchAsync(string handle, SpecialistInvocationIdentity identity, string fence, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Reserves one physical model request before sending, under the live dispatch fence and finite limit.</summary>
    /// <exception cref="SpecialistJournalException">A storage, record or scope failure, retaining its cause.</exception>
    ValueTask<SpecialistModelRequestReservation?> ReserveModelRequestAsync(string handle, SpecialistInvocationIdentity identity, string fence,
        SpecialistModelRequestReservation reservation, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Records response evidence for the exact request set and freezes further physical admission.</summary>
    /// <exception cref="SpecialistJournalException">A storage, record or scope failure, retaining its cause.</exception>
    ValueTask<bool> CompleteModelResponseAsync(string handle, SpecialistInvocationIdentity identity, string fence, IReadOnlyList<string> requestIds,
        SpecialistModelResponseObservation observation, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Atomically accepts the protected result/session pair only for its live dispatch fence.</summary>
    /// <exception cref="SpecialistJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask<bool> AcceptAsync(string handle, SpecialistInvocationIdentity identity, string fence, string protectedResult, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Settles a matching nonterminal claim; accepted results and other fences are preserved.</summary>
    /// <exception cref="SpecialistJournalException">A storage, record or scope failure, retaining its cause. Cancellation propagates separately.</exception>
    ValueTask FailAsync(string handle, SpecialistInvocationIdentity identity, string fence, string code, bool outcomeUnknown, CancellationToken cancellationToken);
}
