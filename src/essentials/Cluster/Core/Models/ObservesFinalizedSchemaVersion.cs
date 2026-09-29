namespace Elsa.Cluster.Core.Models;

/// <summary>
/// "Has observed schema family <see cref="Family"/> finalized at one of <see cref="Versions"/>", optionally for the
/// database whose finalization record has <see cref="DatabaseIdentity"/>: the settle condition of the post-finalization
/// backfill (spec 186, FR-012 and MR-001). Its versions are opaque labels, named rather than compared, because only a
/// family's chain orders them.
/// </summary>
/// <remarks>
/// <para>
/// The entries that speak for the database are those for the family that name its identity or name none. For
/// <see cref="MemberQueryPurpose.Counting"/>, a member whose report has such entries meets the requirement only when
/// every one of them reports an observed finalized version among <see cref="Versions"/>; one that reports none, because
/// the member has not read the record or reads databases that disagree, does not. A member with no such entry cannot
/// write the family's rows in that database and meets it vacuously. A member whose report is unknown, or has no
/// readability section, fails: a member that cannot say what it writes never lets a backfill verify.
/// </para>
/// <para>
/// For <see cref="MemberQueryPurpose.Placement"/>, a member meets it only when at least one entry speaks for the database
/// and every one that does reports a version among <see cref="Versions"/>.
/// </para>
/// </remarks>
public sealed record ObservesFinalizedSchemaVersion : MemberRequirement
{
    public ObservesFinalizedSchemaVersion(string family, IEnumerable<string> versions, string? databaseIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(versions);
        var labels = versions.ToArray();
        if (labels.Length == 0 || labels.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A settle condition names at least one version, and no blank one.", nameof(versions));
        if (databaseIdentity is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseIdentity);

        Family = family;
        Versions = labels;
        DatabaseIdentity = databaseIdentity;
    }

    public string Family { get; }

    /// <summary>The versions a member's observed finalized version may be: the target version and every later one.</summary>
    public IReadOnlyList<string> Versions { get; }

    public string? DatabaseIdentity { get; }

    internal override bool IsMetBy(FleetMember member, MemberQueryPurpose purpose) =>
        ReadabilitySection.EveryEntryMeets(member, Family, DatabaseIdentity, purpose,
            entry => entry.ObservedFinalizedVersion is { } observed && Versions.Contains(observed, StringComparer.Ordinal));

    public bool Equals(ObservesFinalizedSchemaVersion? other) =>
        other is not null &&
        string.Equals(Family, other.Family, StringComparison.Ordinal) &&
        string.Equals(DatabaseIdentity, other.DatabaseIdentity, StringComparison.Ordinal) &&
        Versions.SequenceEqual(other.Versions, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Family, StringComparer.Ordinal);
        hash.Add(DatabaseIdentity, StringComparer.Ordinal);
        foreach (var version in Versions)
            hash.Add(version, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"has observed {Family} finalized at one of [{string.Join(", ", Versions)}]{(DatabaseIdentity is null ? "" : $" for database {DatabaseIdentity}")}";
}
