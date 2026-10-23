namespace Elsa3.Mapping.ActivityMap;

/// <summary>
/// The built-in Elsa 3 → Elsa 4 activity map: the decision record <c>specs/236-elsa3-activity-map/member-map.md</c> as
/// data. Pure and order-independent; lookups are ordinal dictionaries built once.
/// </summary>
internal sealed partial class Elsa3BuiltInActivityMap : IElsa3ActivityMap
{
    private static readonly IReadOnlyList<Elsa3ActivityMapEntry> AllEntries = BuildEntries()
        .OrderBy(entry => entry.Elsa3TypeName, StringComparer.Ordinal)
        .ToArray();

    private static readonly IReadOnlyDictionary<string, Elsa3ActivityMapEntry> EntriesByType = AllEntries
        .ToDictionary(entry => entry.Elsa3TypeName, StringComparer.Ordinal);

    public IReadOnlyList<Elsa3ActivityMapEntry> Entries => AllEntries;

    public Elsa3ActivityMapEntry? Find(string elsa3TypeName) =>
        EntriesByType.GetValueOrDefault(elsa3TypeName);
}
