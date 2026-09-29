namespace Elsa.Cluster.Core.Models;

/// <summary>
/// The member report section that says which persisted-schema versions this host can read: one entry for each schema
/// family it has loaded (FR-019). Its source reads family declarations only, so no configuration can change it
/// (FR-020, MR-002).
/// </summary>
public sealed record ReadabilitySection : MemberReportSection
{
    public ReadabilitySection(IEnumerable<ReadabilityEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries.ToArray();
    }

    public IReadOnlyList<ReadabilityEntry> Entries { get; }

    public bool Equals(ReadabilitySection? other) => other is not null && Entries.SequenceEqual(other.Entries);

    public override int GetHashCode() => Entries.Count;

    /// <summary>
    /// Whether <paramref name="member"/>'s report meets a schema requirement on <paramref name="family"/>, for the database
    /// whose finalization record has <paramref name="databaseIdentity"/> (FR-015, FR-023): the entries that speak for the
    /// database are those for the family that name its identity or name none, and each must satisfy
    /// <paramref name="meets"/>. A member with no such entry meets it vacuously when counted, and not for placement. A
    /// member whose report is unknown, or has no readability section, cannot say what it reads or writes, and never meets it.
    /// </summary>
    internal static bool EveryEntryMeets(FleetMember member, string family, string? databaseIdentity, MemberQueryPurpose purpose, Func<ReadabilityEntry, bool> meets)
    {
        if (member.Report.IsUnknown || member.Report.Readability is not { } readability)
            return false;

        var applicable = readability.Entries
            .Where(entry => string.Equals(entry.Family, family, StringComparison.Ordinal) && entry.AppliesTo(databaseIdentity))
            .ToArray();
        return applicable.Length == 0 ? purpose == MemberQueryPurpose.Counting : applicable.All(meets);
    }
}
