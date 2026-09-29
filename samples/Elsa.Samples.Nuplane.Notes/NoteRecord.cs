namespace Elsa.Samples.Nuplane.Notes;

/// <summary>One stored note.</summary>
public sealed class NoteRecord
{
    public string Id { get; set; } = "";

    public string Text { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The persisted-schema version this row was written in: the family's write version at the time.</summary>
    public string SchemaVersion { get; set; } = null!;

#if DEMO_V2
    /// <summary>
    /// The note's tags as a JSON array, or null. Release 1.1.0 adds this nullable column: a row written by release 1.0.0
    /// has it null, and the family's upcaster reads that as no tags.
    /// </summary>
    public string? TagsJson { get; set; }
#endif
}
