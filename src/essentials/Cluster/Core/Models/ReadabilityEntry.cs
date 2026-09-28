namespace Elsa.Cluster.Core.Models;

/// <summary>
/// What this host can read of one schema family (FR-019): the family, the EF module that owns it, its readable set as
/// an ordered list of opaque version labels, the per-database identity of the finalization record the host read
/// most recently, and the finalized version the host observed in that record (spec 181, FR-010; spec 186, MR-001). An
/// entry that names no database identity counts for every database.
/// </summary>
public sealed record ReadabilityEntry
{
    public ReadabilityEntry(
        string family,
        string efModule,
        IEnumerable<string> readableVersions,
        string? databaseIdentity = null,
        string? observedFinalizedVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
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
    }

    public string Family { get; }

    public string EfModule { get; }

    public IReadOnlyList<string> ReadableVersions { get; }

    public string? DatabaseIdentity { get; }

    /// <summary>The family's finalized version as this host last read it from the finalization record, or
    /// <see langword="null"/> before it has read one.</summary>
    public string? ObservedFinalizedVersion { get; }

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
        ReadableVersions.SequenceEqual(other.ReadableVersions, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Family, EfModule, DatabaseIdentity, ObservedFinalizedVersion, ReadableVersions.Count);
}
