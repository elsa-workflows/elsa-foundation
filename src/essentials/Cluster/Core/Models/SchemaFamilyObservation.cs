namespace Elsa.Cluster.Core.Models;

/// <summary>
/// One schema family's finalization as this host last observed it in one database (spec 181, FR-009, FR-010 and
/// FR-022; spec 186, FR-017): the versions this build reads, the finalized version the record carried, the version this
/// host writes, the completion version, each version above the finalized one with the holds that keep it, and any intent
/// in flight. It is what the shared dormancy check answers from, with no database round trip.
/// </summary>
/// <param name="ReadableVersions">The versions this build reads, oldest first; the chain alone orders version labels.</param>
/// <param name="FinalizedVersion">
/// The finalized version the record carried when this host last read it, or <see langword="null"/> before it has read
/// one. It may be later than <see cref="WriteVersion"/>, or outside <see cref="ReadableVersions"/> altogether.
/// </param>
/// <param name="WriteVersion">
/// The observed finalized version this host has adopted and writes (spec 181, FR-009), or <see langword="null"/> before
/// the family's module was admitted. A member that lapsed does not adopt a newer one until it has rejoined (FR-018).
/// </param>
/// <param name="WritesRefused">
/// True when the finalized version is one this host cannot read, so every write to the family is refused (spec 181,
/// FR-012). That is not dormancy: it is reported as critical.
/// </param>
/// <param name="CompletionVersion">
/// The version the family's finish record names (spec 186, FR-014), as this host last read it, or <see langword="null"/>
/// when none stands, for instance after the backfill's audit withdrew it.
/// </param>
/// <param name="Pending">
/// Each version this build reads above <see cref="FinalizedVersion"/>, with why it is not finalized. Its blockers are
/// filled only on an operator read (<see cref="Contracts.ISchemaDormancyCheck.ReadStatusAsync"/>).
/// </param>
/// <param name="ObservedAt">When this host last read the record, or <see langword="null"/> before it has.</param>
public sealed record SchemaFamilyObservation(
    string Family,
    string? Module,
    IReadOnlyList<string> ReadableVersions,
    string? FinalizedVersion,
    string? WriteVersion,
    bool WritesRefused,
    string? CompletionVersion,
    IReadOnlyList<SchemaPendingVersion> Pending,
    SchemaIntentObservation? Intent,
    DateTimeOffset? ObservedAt)
{
    /// <summary>The position of <paramref name="version"/> along <see cref="ReadableVersions"/>, or -1 for a version it does not name.</summary>
    public int PositionOf(string? version)
    {
        if (version is null)
            return -1;
        for (var position = 0; position < ReadableVersions.Count; position++)
        {
            if (StringComparer.Ordinal.Equals(ReadableVersions[position], version))
                return position;
        }

        return -1;
    }

    /// <summary>The pending entry for <paramref name="version"/>, or <see langword="null"/> when it is not above the finalized version.</summary>
    public SchemaPendingVersion? PendingOf(string version) =>
        Pending.FirstOrDefault(pending => StringComparer.Ordinal.Equals(pending.Version, version));
}

/// <summary>
/// A version above a family's finalized version and why it is not finalized (spec 181, FR-004 and FR-022): whether an
/// intent to finalize it is being confirmed, the holds that apply to it, and, on an operator read, the counted members
/// that cannot read it. <see cref="Blockers"/> is <see langword="null"/> when the fleet was not asked.
/// </summary>
public sealed record SchemaPendingVersion(
    string Version,
    bool ReadableEverywhere,
    IReadOnlyList<SchemaHoldObservation> HeldBy,
    IReadOnlyList<string>? Blockers);

/// <summary>An operator's hold on a family, or on one version and every later one (spec 181, FR-019).</summary>
public sealed record SchemaHoldObservation(string? Version, string Reason, string PlacedBy, DateTimeOffset PlacedAt);

/// <summary>A durable intent to finalize <paramref name="Version"/>, written by <paramref name="Member"/> at <paramref name="At"/> (spec 181, FR-006).</summary>
public sealed record SchemaIntentObservation(string Version, string Member, DateTimeOffset At);
