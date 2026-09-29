using Elsa.Persistence.EntityFramework;

namespace Elsa.Samples.Nuplane.Notes;

/// <summary>
/// The family's one step, from 1.0.0 to 2.0.0: a row written by release 1.0.0 has no tags column to hold, so its
/// <c>TagsJson</c> is null, and version 2.0.0 reads that as an empty list. It is pure and total, as every upcaster
/// must be: the same row always yields the same row, and a row that already has tags is returned as it is.
/// </summary>
[EfSchemaUpcaster(NotesModule.FirstVersion, NotesModule.TagsVersion)]
public sealed class NotesOneToTwo : IEfSchemaUpcaster
{
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) =>
        row[nameof(NoteRecord.TagsJson)] is null ? row.With(nameof(NoteRecord.TagsJson), "[]") : row;
}
