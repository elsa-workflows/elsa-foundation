using Microsoft.Extensions.Primitives;

namespace Elsa.Persistence.Schema.SchemaFinalization;

/// <summary>
/// What the finalization gate needs from cluster membership, stated in the
/// gate's own terms so persistence takes no dependency on the membership contract. <c>Elsa.Cluster.Readability</c>
/// implements it over the host's one membership provider; a host that composes no implementation has a gate that
/// never finalizes a version past the one its record was created at, which only ever delays finalization.
/// </summary>
/// <remarks>
/// An implementation is the whole of the gate's view of the fleet, so it carries the rules a premature finalization
/// depends on: <see cref="PublishAsync"/> returns only once every later fresh read shows the report, and
/// <see cref="CountAsync"/> reads the fleet fresh and counts every live member whose report speaks for the database,
/// including a displaced-but-live one and one whose report is unknown.
/// </remarks>
public interface IEfSchemaFleet
{
    /// <summary>This process's member as it knows itself.</summary>
    EfSchemaFleetStanding GetLocalStanding();

    /// <summary>Publishes this member's readability report, and returns once every later fresh read shows it.</summary>
    ValueTask PublishAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the fleet fresh and answers "can every counted member read <paramref name="family"/> at
    /// <paramref name="version"/> in the database whose identity is <paramref name="databaseIdentity"/>?", listing each
    /// counted member that cannot. Throws when the fleet cannot be read in full; it never answers from part of it.
    /// </summary>
    ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the fleet fresh and answers the post-finalization backfill's settle condition:
    /// "does every counted member report, for <paramref name="family"/> in the database whose identity is
    /// <paramref name="databaseIdentity"/>, an observed finalized version among <paramref name="versions"/>?", listing
    /// each counted member that does not. A member that reports no observed version, or whose report is unknown, does
    /// not. Throws when the fleet cannot be read in full; it never answers from part of it.
    /// </summary>
    /// <param name="versions">The target version and every later one this host reads: the chain alone orders labels.</param>
    ValueTask<EfSchemaFleetAnswer> CountObservingAsync(
        string family,
        IReadOnlyList<string> versions,
        string databaseIdentity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How long a write a member began before it observed a newer finalized version can still be in flight once the
    /// member reports observing it: the membership expiry period plus the skew allowance.
    /// The floor for the backfill's settle margin: a configured margin may lengthen it and never shortens it.
    /// </summary>
    TimeSpan SettleMargin { get; }

    /// <summary>A token that fires the next time the fleet changes, so evaluation need not wait for its interval.</summary>
    IChangeToken GetChangeToken();
}

/// <summary>
/// This process's member: the host id and incarnation the record names it by, and whether it has concluded that its
/// membership lapsed, in which case it may not adopt a newer finalized version until it rejoins.
/// </summary>
public sealed record EfSchemaFleetStanding(SchemaFinalizationMember Member, bool HasLapsed);

/// <summary>
/// The answer to a counting query: whether every counted member reads the version, and a description of each counted
/// member that does not, as the gate's status reports it.
/// </summary>
public sealed record EfSchemaFleetAnswer(bool EveryCountedMemberReads, IReadOnlyList<string> Blockers);
