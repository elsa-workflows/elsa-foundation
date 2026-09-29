using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Persistence.EntityFramework.SchemaFinalization;

namespace Elsa.Cluster.Readability;

/// <summary>
/// What this container's EF finalization gates have observed (spec 181), as the shared dormancy check reads it (spec
/// 182, FR-003): each family's write version, its record as last read, with holds, intent and finish record, and a way to
/// refresh it or read its status on demand. It reads the gates live, so it follows every refresh without a restart or a
/// shell reload (FR-019).
/// </summary>
/// <remarks>
/// <para>
/// A container's gates are the ones its own module migrators admitted (<see cref="EfSchemaFinalizationGates"/>), so one of
/// these, resolved in a shell, sees exactly the databases that shell serves. A family whose module that container has
/// not admitted is observed as nothing, which the check reports as unmet rather than available.
/// </para>
/// <para>
/// It reads each gate through <see cref="IEfSchemaModuleGate"/>, which <c>Elsa.Persistence.Schema</c> defines and every
/// host shares, so it sees the gate of a module Nuplane loaded with a private copy of
/// <c>Elsa.Persistence.EntityFramework</c> as well as one compiled into the host, and takes no EF Core itself (#2143).
/// </para>
/// </remarks>
public sealed class EfObservedSchemaFinalization(EfSchemaFinalizationGates? gates = null) : IObservedSchemaFinalization
{
    public SchemaFamilyObservation? Find(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        return Gates().Select(gate => gate.Observe(family)).FirstOrDefault(status => status is not null) is { } status ? Map(status) : null;
    }

    public IReadOnlyList<SchemaFamilyObservation> Observe() => Gates().SelectMany(gate => gate.Observe()).Select(Map).ToArray();

    public async ValueTask RefreshAsync(string family, TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (Gates().FirstOrDefault(gate => gate.Observe(family) is not null) is { } owner)
            await owner.RefreshIfOlderThanAsync(maxAge, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default)
    {
        var statuses = new List<SchemaFamilyObservation>();
        foreach (var gate in Gates())
            statuses.AddRange((await gate.ReadStatusAsync(cancellationToken)).Select(Map));
        return statuses;
    }

    private IEnumerable<IEfSchemaModuleGate> Gates() => (gates?.All ?? []).OrderBy(gate => gate.Module, StringComparer.Ordinal);

    private static SchemaFamilyObservation Map(EfSchemaFamilyStatus status) =>
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
            status.ObservedAt);
}
