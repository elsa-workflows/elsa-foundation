using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Core.Services;

/// <summary>
/// The one dormancy rule (spec 182, FR-003 to FR-005; ADR 0078, "modules check dormancy through one shared helper over
/// the finalized version"), over what a host has observed of each family. Every <see cref="Contracts.ISchemaDormancyCheck"/>
/// applies it, so the rule lives beside the contract rather than in any one implementation.
/// </summary>
/// <remarks>
/// <b>The invariant.</b> A requirement on family F at version V is met only when this host writes F at V or later, which it
/// does only once V is finalized and it has adopted it (spec 181, FR-009 and FR-018), and, for a completeness requirement,
/// only when the finish record it observed names V or later (spec 186, FR-017). Anything this host cannot place along the
/// family's chain — a family it has observed nothing of, a version its build does not read, a finalized version it cannot
/// read — leaves the requirement unmet. The rule never guesses in the direction that would let an operation run without
/// the data it needs.
/// </remarks>
public static class SchemaDormancyRule
{
    /// <summary>Whether <paramref name="requirements"/> are met, each checked against what <paramref name="find"/> observed of its family.</summary>
    public static SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements, Func<string, SchemaFamilyObservation?> find)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(find);
        var unmet = requirements
            .Distinct()
            .Select(requirement => Check(requirement, find(requirement.Family)))
            .OfType<UnmetSchemaRequirement>()
            .ToArray();
        return unmet.Length == 0 ? SchemaAvailability.Available : new SchemaAvailability(unmet);
    }

    /// <summary><see langword="null"/> when <paramref name="requirement"/> is met by <paramref name="family"/>, otherwise why it is not.</summary>
    public static UnmetSchemaRequirement? Check(SchemaVersionRequirement requirement, SchemaFamilyObservation? family)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        if (family?.WriteVersion is null)
            return Unmet(requirement, SchemaDormancyKind.NotObserved, null);

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
