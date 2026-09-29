using Microsoft.Extensions.Primitives;

namespace Elsa.Persistence.Schema.SchemaFinalization;

/// <summary>
/// What the finalization gate needs from cluster membership (spec 181, "Requirements on membership"), stated in the
/// gate's own terms so persistence takes no dependency on the membership contract. <c>Elsa.Cluster.Readability</c>
/// implements it over the host's one membership provider; a host that composes no implementation has a gate that
/// never finalizes a version past the one its record was created at, which only ever delays finalization.
/// </summary>
/// <remarks>
/// An implementation is the whole of the gate's view of the fleet, so it carries the rules a premature finalization
/// depends on: <see cref="PublishAsync"/> returns only once every later fresh read shows the report (MR-003), and
/// <see cref="CountAsync"/> reads the fleet fresh and counts every live member whose report speaks for the database,
/// including a displaced-but-live one and one whose report is unknown (MR-007; spec 183, FR-023).
/// </remarks>
public interface IEfSchemaFleet
{
    /// <summary>This process's member as it knows itself (MR-005).</summary>
    EfSchemaFleetStanding GetLocalStanding();

    /// <summary>Publishes this member's readability report, and returns once every later fresh read shows it (FR-013).</summary>
    ValueTask PublishAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the fleet fresh and answers "can every counted member read <paramref name="family"/> at
    /// <paramref name="version"/> in the database whose identity is <paramref name="databaseIdentity"/>?", listing each
    /// counted member that cannot (MR-007). Throws when the fleet cannot be read in full; it never answers from part of it.
    /// </summary>
    ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default);

    /// <summary>A token that fires the next time the fleet changes, so evaluation need not wait for its interval (FR-005).</summary>
    IChangeToken GetChangeToken();
}

/// <summary>
/// This process's member: the host id and incarnation the record names it by, and whether it has concluded that its
/// membership lapsed, in which case it may not adopt a newer finalized version until it rejoins (FR-018).
/// </summary>
public sealed record EfSchemaFleetStanding(SchemaFinalizationMember Member, bool HasLapsed);

/// <summary>
/// The answer to a counting query: whether every counted member reads the version, and a description of each counted
/// member that does not, as the gate's status reports it (FR-022).
/// </summary>
public sealed record EfSchemaFleetAnswer(bool EveryCountedMemberReads, IReadOnlyList<string> Blockers);
