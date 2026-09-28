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
}
