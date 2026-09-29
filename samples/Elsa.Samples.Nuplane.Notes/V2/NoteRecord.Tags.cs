namespace Elsa.Samples.Nuplane.Notes;

public sealed partial class NoteRecord
{
    /// <summary>
    /// The note's tags as a JSON array. Release 1.1.0 adds this nullable column: a row written by release 1.0.0 has it
    /// null, and the family's upcaster reads that as no tags, so a row read through the chain always has it.
    /// </summary>
    public string? TagsJson { get; set; }
}
