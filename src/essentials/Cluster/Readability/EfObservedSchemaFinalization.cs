using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;

namespace Elsa.Cluster.Readability;

/// <summary>
/// What this container's EF finalization gates have observed (spec 181), as the shared dormancy check reads it (spec
/// 182, FR-003): each family's write version, its record as last read, with holds, intent and finish record, what the
/// module's post-finalization backfill reports of it (spec 186, FR-021), and a way to refresh it or read its status on
/// demand. It reads the gates live, so it follows every refresh without a restart or a
/// shell reload (FR-019).
/// </summary>
/// <remarks>
/// A container's gates are the ones its own module migrators admitted (<see cref="EfSchemaFinalizationGates"/>), so one of
/// these, resolved in a shell, sees exactly the databases that shell serves. A family whose module that container has
/// not admitted is observed as nothing, which the check reports as unmet rather than available.
/// </remarks>
public sealed class EfObservedSchemaFinalization(EfSchemaFinalizationGates? gates = null) : IObservedSchemaFinalization
{
    public SchemaFamilyObservation? Find(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        return Owner(family) is { } owner ? Observe(owner.Gate, owner.Chain) : null;
    }

    public IReadOnlyList<SchemaFamilyObservation> Observe() =>
        Gates().SelectMany(gate => gate.Families.Chains.Select(chain => Observe(gate, chain))).ToArray();

    public async ValueTask RefreshAsync(string family, TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (Owner(family) is { } owner)
            await owner.Gate.RefreshIfOlderThanAsync(maxAge, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default)
    {
        var statuses = new List<SchemaFamilyObservation>();
        foreach (var gate in Gates())
        foreach (var status in await gate.ReadStatusAsync(cancellationToken))
            statuses.Add(Map(status, gate.ObservedRecordOf(status.Family)?.At));
        return statuses;
    }

    private IEnumerable<EfSchemaModuleGate> Gates() => (gates?.All ?? []).OrderBy(gate => gate.Module, StringComparer.Ordinal);

    private (EfSchemaModuleGate Gate, EfSchemaChain Chain)? Owner(string family)
    {
        foreach (var gate in Gates())
        {
            if (gate.Families.Chains.FirstOrDefault(chain => StringComparer.Ordinal.Equals(chain.Family, family)) is { } chain)
                return (gate, chain);
        }

        return null;
    }

    private static SchemaFamilyObservation Observe(EfSchemaModuleGate gate, EfSchemaChain chain)
    {
        var observed = gate.ObservedRecordOf(chain.Family);
        var state = gate.StateOf(chain.Family);
        var status = EfSchemaFamilyStatus.Describe(chain, observed?.Record) with
        {
            WriteVersion = state?.WriteVersion,
            WritesRefused = state?.WritesRefused ?? false,
            Backfill = gate.Backfill?.StatusOf(chain.Family)
        };
        return Map(status, observed?.At);
    }

    private static SchemaFamilyObservation Map(EfSchemaFamilyStatus status, DateTimeOffset? observedAt) =>
        new(
            status.Family,
            status.Module,
            status.ReadableVersions,
            status.FinalizedVersion,
            status.WriteVersion,
            status.WritesRefused,
            status.Finish?.CompletionVersion,
            status.Pending
                .Select(pending => new SchemaPendingVersion(
                    pending.Version,
                    pending.State is SchemaFinalizationState.ReadableEverywhere,
                    pending.HeldBy.Select(hold => new SchemaHoldObservation(hold.Version, hold.Reason, hold.PlacedBy, hold.PlacedAt)).ToArray(),
                    pending.Blockers))
                .ToArray(),
            status.Intent is { } intent ? new SchemaIntentObservation(intent.Version, intent.Member.ToString(), intent.At) : null,
            observedAt,
            status.Backfill is { } backfill
                ? new SchemaBackfillObservation(
                    backfill.State.ToString(),
                    backfill.TargetVersion,
                    backfill.RowsRewritten,
                    backfill.SettleWaitingFor,
                    backfill.Blockers.Select(blocker => blocker.Detail).ToArray(),
                    backfill.BlockedByContentAddressedRows,
                    backfill.Detail)
                : null,
            status.Withdrawal is { } withdrawal
                ? new SchemaCompletionWithdrawal(withdrawal.Version, withdrawal.Actor.ToString(), withdrawal.At, withdrawal.Reason)
                : null);
}
