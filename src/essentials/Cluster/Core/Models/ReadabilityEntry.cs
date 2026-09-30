namespace Elsa.Cluster.Core.Models;

/// <summary>
/// What this host can read of one schema family (FR-019): the family, the EF module that owns it, its readable set as
/// an ordered list of opaque version labels, the per-database identity of the finalization record the host read
/// most recently, the finalized version the host observed in that record (spec 181, FR-010; spec 186, MR-001), and whether
/// the family's module is active in the host (spec 183, FR-019, amended 2026-09-30). An entry that names no database
/// identity counts for every database. <see cref="EfModule"/> is <see langword="null"/> for a family shared by no single
/// EF module (spec 180, FR-001).
/// </summary>
public sealed record ReadabilityEntry
{
    public ReadabilityEntry(
        string family,
        string? efModule,
        IEnumerable<string> readableVersions,
        string? databaseIdentity = null,
        string? observedFinalizedVersion = null,
        bool moduleActive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (efModule is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(efModule);
        ArgumentNullException.ThrowIfNull(readableVersions);
        if (databaseIdentity is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseIdentity);
        if (observedFinalizedVersion is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(observedFinalizedVersion);

        var versions = readableVersions.ToArray();
        if (versions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A readable version label must not be blank.", nameof(readableVersions));

        Family = family;
        EfModule = efModule;
        ReadableVersions = versions;
        DatabaseIdentity = databaseIdentity;
        ObservedFinalizedVersion = observedFinalizedVersion;
        ModuleActive = moduleActive;
    }

    public string Family { get; }

    /// <summary>The EF module that owns the family, as its <c>[EfModule]</c> declares its name, or
    /// <see langword="null"/> for a family shared by no single EF module.</summary>
    public string? EfModule { get; }

    public IReadOnlyList<string> ReadableVersions { get; }

    public string? DatabaseIdentity { get; }

    /// <summary>The family's finalized version as this host last read it from the finalization record, or
    /// <see langword="null"/> before it has read one.</summary>
    public string? ObservedFinalizedVersion { get; }

    /// <summary>
    /// Whether the family's module is active in this host: admitted by a finalization gate that has not stopped, so this
    /// host can write the family's rows. <see langword="false"/> for a family whose declaration is loaded and whose module
    /// no shell has activated, or no longer does: such a host writes none of the family's rows, so the backfill's settle
    /// condition does not wait for it (spec 186, FR-012; spec 183, FR-019, amended 2026-09-30). A loaded declaration
    /// credits <see cref="ReadableVersions"/> either way, so every readability count still counts the family (spec 183,
    /// FR-022). An entry built without saying is active, the direction that counts the member.
    /// </summary>
    public bool ModuleActive { get; }

    /// <summary>Whether this entry speaks for <paramref name="databaseIdentity"/>: it names it, it names none, or no
    /// database was asked about.</summary>
    public bool AppliesTo(string? databaseIdentity) =>
        databaseIdentity is null || DatabaseIdentity is null || string.Equals(DatabaseIdentity, databaseIdentity, StringComparison.Ordinal);

    public bool CanRead(string version) => ReadableVersions.Contains(version, StringComparer.Ordinal);

    public bool Equals(ReadabilityEntry? other) =>
        other is not null &&
        string.Equals(Family, other.Family, StringComparison.Ordinal) &&
        string.Equals(EfModule, other.EfModule, StringComparison.Ordinal) &&
        string.Equals(DatabaseIdentity, other.DatabaseIdentity, StringComparison.Ordinal) &&
        string.Equals(ObservedFinalizedVersion, other.ObservedFinalizedVersion, StringComparison.Ordinal) &&
        ModuleActive == other.ModuleActive &&
        ReadableVersions.SequenceEqual(other.ReadableVersions, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Family, EfModule, DatabaseIdentity, ObservedFinalizedVersion, ModuleActive, ReadableVersions.Count);
}
