using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Primitives.Exceptions;
using Microsoft.Extensions.Options;

namespace Elsa.Cluster.Core.Services;

/// <summary>
/// The shared dormancy check (spec 182, FR-003): the one place the rule is applied, over what this host has observed of
/// each family's finalization record.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant.</b> A requirement on family F at version V is met only when this host writes F at V or later, which
/// it does only once V is finalized and it has adopted it (spec 181, FR-009 and FR-018), and, for a completeness
/// requirement, only when the finish record this host observed names V or later (spec 186, FR-017). Anything this host
/// cannot place along the family's chain — a family it has observed nothing of, a version its build does not read, a
/// finalized version it cannot read — leaves the requirement unmet. The check never guesses in the direction that would
/// let an operation run without the data it needs.
/// </para>
/// <para>
/// Every answer comes from memory. Only an unmet requirement whose observation is older than
/// <see cref="SchemaDormancyOptions.RefreshBound"/> makes the observation be refreshed before the answer is given (FR-014).
/// </para>
/// </remarks>
public sealed class SchemaDormancyCheck(IOptions<SchemaDormancyOptions>? options = null, IObservedSchemaFinalization? observed = null) : ISchemaDormancyCheck
{
    private readonly TimeSpan _refreshBound = options?.Value.RefreshBound ?? new SchemaDormancyOptions().RefreshBound;

    public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        var unmet = requirements
            .Distinct()
            .Select(requirement => Check(requirement, observed?.Find(requirement.Family)))
            .OfType<UnmetSchemaRequirement>()
            .ToArray();
        return unmet.Length == 0 ? SchemaAvailability.Available : new SchemaAvailability(unmet);
    }

    public async ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        var asked = requirements.ToArray();
        var availability = Evaluate(asked);
        if (availability.IsAvailable || observed is null)
            return availability;

        // FR-014: a stale view must not refuse what finalization already allows. The source refreshes only what it read
        // longer ago than the bound, so this costs nothing when the view is fresh.
        foreach (var family in availability.Unmet.Select(unmet => unmet.Requirement.Family).Distinct(StringComparer.Ordinal))
            await observed.RefreshAsync(family, _refreshBound, cancellationToken);
        return Evaluate(asked);
    }

    public async ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default)
    {
        var availability = await EvaluateAsync(requirements, cancellationToken);
        if (availability.IsAvailable)
            return;

        var first = availability.Unmet[0];
        throw new SchemaDormancyRefusedException(
            first.Requirement.Family,
            first.ObservedVersion ?? "(none)",
            first.Requirement.Version,
            featureId,
            availability.Reason);
    }

    public IReadOnlyList<SchemaFamilyObservation> Observe() => observed?.Observe() ?? [];

    public async ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) =>
        observed is null ? [] : await observed.ReadStatusAsync(cancellationToken);

    /// <summary>The one rule: <see langword="null"/> when <paramref name="requirement"/> is met, otherwise why it is not.</summary>
    internal static UnmetSchemaRequirement? Check(SchemaVersionRequirement requirement, SchemaFamilyObservation? family)
    {
        if (family?.WriteVersion is null)
            return Unmet(requirement, SchemaDormancyKind.NotObserved, family?.WriteVersion);

        var required = family.PositionOf(requirement.Version);
        if (required < 0)
            return Unmet(requirement, SchemaDormancyKind.UnknownVersion, family.WriteVersion);
        if (family.WritesRefused)
            return Unmet(requirement, SchemaDormancyKind.WritesRefused, family.WriteVersion);

        if (family.PositionOf(family.WriteVersion) < required)
        {
            if (family.PositionOf(family.FinalizedVersion) >= required)
                return Unmet(requirement, SchemaDormancyKind.NotYetAdopted, family.WriteVersion);
            var holds = family.PendingOf(requirement.Version)?.HeldBy ?? [];
            return holds.Count > 0
                ? Unmet(requirement, SchemaDormancyKind.Held, family.WriteVersion, holds)
                : Unmet(requirement, SchemaDormancyKind.WaitingForHosts, family.WriteVersion);
        }

        // A completion version this build cannot place lies before its readable set, so rows it names may remain (spec 186,
        // FR-020): incomplete, never complete.
        if (requirement.RequiresCompleteness && family.PositionOf(family.CompletionVersion) < required)
            return Unmet(requirement, SchemaDormancyKind.WaitingForCompleteness, family.WriteVersion);

        return null;
    }

    private static UnmetSchemaRequirement Unmet(
        SchemaVersionRequirement requirement,
        SchemaDormancyKind kind,
        string? observedVersion,
        IReadOnlyList<SchemaHoldObservation>? holds = null) =>
        new(requirement, kind, observedVersion, SchemaDormancyReasons.ForCaller(requirement, kind), holds ?? []);
}
