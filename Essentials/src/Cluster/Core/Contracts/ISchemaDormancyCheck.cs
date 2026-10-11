using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Core.Contracts;

/// <summary>
/// Declares a schema dormancy contract (<see cref="ISchemaDormancyCheck"/>, <see cref="IObservedSchemaFinalization"/>) as
/// a replacement contract (framework constitution §2.6.2): one implementation is active per container, and composing a
/// second one fails when it is composed, never resolved by last-write-wins.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class SchemaDormancyReplacementContractAttribute : Attribute;

/// <summary>
/// The shared dormancy check (spec 182, FR-003; ADR 0078, "Features that need the new data wait for finalization"): the
/// one component every module asks "is schema family F at version V yet?". A feature whose data only a newer schema
/// version can hold stays dormant until this host observes that version as finalized and, where the feature needs it,
/// the family as complete; while it is dormant, an operation that needs the data is refused, never answered without it.
/// </summary>
/// <remarks>
/// <para>
/// It answers from the host's observed finalized version (spec 181, FR-009 and FR-010) and observed completion version
/// (spec 186, FR-017), through <see cref="IObservedSchemaFinalization"/>, with no database round trip on the success path.
/// Modules do not read the finalization record or compare versions themselves: an operation, request field, query or
/// background task that needs new-version data asks here, where it accepts that data and before any write or other side
/// effect (FR-002, FR-016, FR-018).
/// </para>
/// <para>
/// Nothing about dormancy changes composition (FR-006), and nothing here depends on a restart or a shell reload: the
/// observation follows the finalization gate's refresh, so the next call after this host observes a finalization is
/// served (FR-019).
/// </para>
/// </remarks>
[SchemaDormancyReplacementContract]
public interface ISchemaDormancyCheck
{
    /// <summary>
    /// Whether <paramref name="requirements"/> are met, from what this host has observed, without any I/O. For a
    /// background task that asks each time it runs and skips its work while dormant (FR-018).
    /// </summary>
    SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements);

    /// <summary>
    /// Whether <paramref name="requirements"/> are met. When one is not, and this host's observation of its family is
    /// older than a short, rate-limited bound, the observation is refreshed first and the requirements evaluated again,
    /// so a stale view never reports dormancy that finalization already ended (FR-014).
    /// </summary>
    ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns when <paramref name="requirements"/> are met, as <see cref="EvaluateAsync"/> decides, and otherwise raises
    /// the dormancy refusal (FR-012, FR-013): a <c>SchemaDormancyRefusedException</c> naming <paramref name="featureId"/>,
    /// the first unmet requirement's family and versions, and a caller-neutral reason for every unmet one. Call it where
    /// the operation accepts the data, before any row changes.
    /// </summary>
    /// <exception cref="Elsa.Primitives.Exceptions.SchemaDormancyRefusedException">A requirement is not met.</exception>
    ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default);

    /// <summary>Every family this host has observed, as it observed it, without any I/O.</summary>
    IReadOnlyList<SchemaFamilyObservation> Observe();

    /// <summary>
    /// Every family's finalization status, read now, with the counted members that cannot read each pending version
    /// (spec 181, FR-022). For operator surfaces only, which may name members (spec 182, FR-011); it reads the
    /// finalization records and the fleet.
    /// </summary>
    ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default);
}
