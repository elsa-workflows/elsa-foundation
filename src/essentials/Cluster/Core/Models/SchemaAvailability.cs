namespace Elsa.Cluster.Core.Models;

/// <summary>
/// Whether a set of dormancy requirements is met on this host (spec 182, Key Entities, "Availability"): available, or
/// dormant with one entry per unmet requirement saying why.
/// </summary>
public sealed record SchemaAvailability
{
    public SchemaAvailability(IReadOnlyList<UnmetSchemaRequirement> unmet)
    {
        ArgumentNullException.ThrowIfNull(unmet);
        Unmet = unmet;
    }

    /// <summary>Every requirement met.</summary>
    public static SchemaAvailability Available { get; } = new([]);

    /// <summary>The requirements that are not met, in the order they were asked about.</summary>
    public IReadOnlyList<UnmetSchemaRequirement> Unmet { get; }

    public bool IsAvailable => Unmet.Count == 0;

    public bool IsDormant => !IsAvailable;

    /// <summary>
    /// The caller-neutral reason, one sentence per unmet requirement (FR-008). It names no host and says nothing about the
    /// fleet's topology, so a domain API may return it (FR-011). Empty when available.
    /// </summary>
    public string Reason => string.Join(" ", Unmet.Select(unmet => unmet.Reason));
}

/// <summary>
/// One unmet dormancy requirement: the requirement, why it is unmet, the version this host may use for the family so
/// far, and a caller-neutral <see cref="Reason"/> (spec 182, FR-008 and FR-011).
/// </summary>
/// <param name="ObservedVersion">The version this host writes for the family, or <see langword="null"/> when it has observed none.</param>
/// <param name="Holds">The holds that keep the required version from finalizing, for <see cref="SchemaDormancyKind.Held"/>.</param>
public sealed record UnmetSchemaRequirement(
    SchemaVersionRequirement Requirement,
    SchemaDormancyKind Kind,
    string? ObservedVersion,
    string Reason,
    IReadOnlyList<SchemaHoldObservation> Holds);

/// <summary>Why a dormancy requirement is unmet (spec 182, FR-008 and Edge Cases).</summary>
public enum SchemaDormancyKind
{
    /// <summary>Not every host can read the version yet, so it is not finalized.</summary>
    WaitingForHosts,

    /// <summary>An operator holds the version, or an earlier one, from finalizing.</summary>
    Held,

    /// <summary>The version is finalized, but rows of the family below it may remain (FR-005).</summary>
    WaitingForCompleteness,

    /// <summary>The version is finalized, but this host has not adopted it yet: its membership lapsed and it has not rejoined (spec 181, FR-018).</summary>
    NotYetAdopted,

    /// <summary>This host has read no finalization record of the family, so it cannot tell whether the version is available.</summary>
    NotObserved,

    /// <summary>This build does not read the version the requirement names, so it can never use that version's data.</summary>
    UnknownVersion,

    /// <summary>The family's finalized version is one this host cannot read, so every write to it is refused (spec 181, FR-012).</summary>
    WritesRefused
}
