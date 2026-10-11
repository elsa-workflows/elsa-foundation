using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Core.Contracts;

/// <summary>
/// What this host has observed of each schema family's finalization record in the databases this container serves (spec
/// 181, FR-009, FR-010 and FR-022): the source the shared dormancy check answers from. The persistence that runs the
/// finalization gate provides it; the check never reads a record itself.
/// </summary>
/// <remarks>
/// A container that composes none observes nothing, and the check then reports every requirement as unmet because it
/// cannot tell, which refuses rather than guesses.
/// </remarks>
[SchemaDormancyReplacementContract]
public interface IObservedSchemaFinalization
{
    /// <summary>The family as this host last observed it, without any I/O, or <see langword="null"/> when it has observed none.</summary>
    SchemaFamilyObservation? Find(string family);

    /// <summary>Every family this host has observed, without any I/O.</summary>
    IReadOnlyList<SchemaFamilyObservation> Observe();

    /// <summary>
    /// Reads <paramref name="family"/>'s record again and adopts what it says, as the gate's own refresh does (spec 181,
    /// FR-010), unless this host last read it less than <paramref name="maxAge"/> ago. Concurrent callers share one read,
    /// so a burst of refused requests costs at most one read per bound (spec 182, FR-014).
    /// </summary>
    ValueTask RefreshAsync(string family, TimeSpan maxAge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every family's status, read now from its record and the fleet, with the counted members that cannot read each
    /// pending version (spec 181, FR-022).
    /// </summary>
    ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default);
}
