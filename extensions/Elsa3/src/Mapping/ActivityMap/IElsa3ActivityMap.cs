namespace Elsa3.Mapping.ActivityMap;

/// <summary>
/// The Elsa 3 → Elsa 4 activity map: one entry per Elsa 3 activity type. Single implementation per application; the
/// default is the built-in table. Any change to the map changes converter output.
/// </summary>
public interface IElsa3ActivityMap
{
    /// <summary>Every entry, in ordinal type-name order.</summary>
    IReadOnlyList<Elsa3ActivityMapEntry> Entries { get; }

    /// <summary>The entry for <paramref name="elsa3TypeName"/> (ordinal), or null when the type is not mapped.</summary>
    Elsa3ActivityMapEntry? Find(string elsa3TypeName);
}
