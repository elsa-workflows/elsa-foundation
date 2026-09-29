namespace Elsa.Cluster.Core.Models;

/// <summary>
/// "Reads schema family <see cref="Family"/> at version <see cref="Version"/>", optionally for the database whose
/// finalization record has <see cref="DatabaseIdentity"/> (FR-015, FR-023).
/// </summary>
/// <remarks>
/// <para>
/// The entries that speak for the database are those for the family that name its identity or name none. For
/// <see cref="MemberQueryPurpose.Counting"/>, a member whose report has such entries is counted, and it meets the
/// requirement only when every one of them can read the version. A member with no such entry is not counted for the
/// family and meets it vacuously. A member whose report is unknown, or has no readability section and so cannot say
/// what it reads, is counted and fails: a doubtful member never lets a version finalize.
/// </para>
/// <para>
/// For <see cref="MemberQueryPurpose.Placement"/>, a member meets the requirement only when its report shows it can read
/// the version: at least one entry speaks for the database, and every one that does can read it.
/// </para>
/// </remarks>
public sealed record ReadsSchemaVersion : MemberRequirement
{
    public ReadsSchemaVersion(string family, string version, string? databaseIdentity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (databaseIdentity is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseIdentity);

        Family = family;
        Version = version;
        DatabaseIdentity = databaseIdentity;
    }

    public string Family { get; }

    public string Version { get; }

    public string? DatabaseIdentity { get; }

    internal override bool IsMetBy(FleetMember member, MemberQueryPurpose purpose) =>
        ReadabilitySection.EveryEntryMeets(member, Family, DatabaseIdentity, purpose, entry => entry.CanRead(Version));

    public override string ToString() =>
        DatabaseIdentity is null
            ? $"reads {Family} at {Version}"
            : $"reads {Family} at {Version} for database {DatabaseIdentity}";
}
