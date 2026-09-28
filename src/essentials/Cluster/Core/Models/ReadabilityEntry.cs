namespace Elsa.Cluster.Core.Models;

/// <summary>
/// What this host can read of one schema family (FR-019): the family, the EF module that owns it, its readable set as
/// an ordered list of opaque version labels, and the per-database identity of the finalization record the host read
/// most recently. An entry that names no database identity counts for every database.
/// </summary>
public sealed record ReadabilityEntry
{
    public ReadabilityEntry(string family, string efModule, IEnumerable<string> readableVersions, string? databaseIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(efModule);
        ArgumentNullException.ThrowIfNull(readableVersions);
        if (databaseIdentity is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseIdentity);

        var versions = readableVersions.ToArray();
        if (versions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A readable version label must not be blank.", nameof(readableVersions));

        Family = family;
        EfModule = efModule;
        ReadableVersions = versions;
        DatabaseIdentity = databaseIdentity;
    }

    public string Family { get; }

    public string EfModule { get; }

    public IReadOnlyList<string> ReadableVersions { get; }

    public string? DatabaseIdentity { get; }

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
        ReadableVersions.SequenceEqual(other.ReadableVersions, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Family, EfModule, DatabaseIdentity, ReadableVersions.Count);
}
