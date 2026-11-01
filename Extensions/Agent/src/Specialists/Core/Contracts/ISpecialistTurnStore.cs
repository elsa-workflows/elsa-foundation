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
    ValueTask<SpecialistJournalCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    /// <summary>Inserts or replays the identical native operation; conflicting requests change nothing.</summary>
    ValueTask<SpecialistTurnDocument> StageAsync(SpecialistTurnDocument candidate, CancellationToken cancellationToken);
    /// <summary>Reads only the exact handle and native identity in the caller's selected scope.</summary>
    ValueTask<SpecialistTurnDocument?> ReadAsync(string handle, SpecialistInvocationIdentity identity, CancellationToken cancellationToken);
    /// <summary>Claims a prepared, unexpired turn once and binds its native child and journal fence.</summary>
    ValueTask<SpecialistTurnDocument?> ClaimAsync(string handle, SpecialistInvocationIdentity identity, string childInvocationId, string fence, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Persists dispatch responsibility under the live claim before inference can begin.</summary>
    ValueTask<bool> DispatchAsync(string handle, SpecialistInvocationIdentity identity, string fence, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Atomically accepts the protected result/session pair only for its live dispatch fence.</summary>
    ValueTask<bool> AcceptAsync(string handle, SpecialistInvocationIdentity identity, string fence, string protectedResult, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Settles a matching nonterminal claim; accepted results and other fences are preserved.</summary>
    ValueTask FailAsync(string handle, SpecialistInvocationIdentity identity, string fence, string code, bool outcomeUnknown, CancellationToken cancellationToken);
}
