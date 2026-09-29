using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Samples.Nuplane.Notes;

/// <summary>A note as the tags endpoints show it.</summary>
public sealed record NoteWithTags(string Id, string Text, DateTimeOffset CreatedAt, IReadOnlyList<string> Tags);

/// <summary>What release 1.1.0 adds to the store: reading and writing a note's tags.</summary>
public sealed partial class NoteStore
{
    public async Task<IReadOnlyList<NoteWithTags>> ListWithTagsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Notes.AsNoTracking().ToListAsync(cancellationToken);
        return [.. rows.OrderBy(row => row.CreatedAt).Select(ToNoteWithTags)];
    }

    /// <summary>Adds <paramref name="tags"/> to a note, or returns null when there is no such note.</summary>
    public async Task<NoteWithTags?> AddTagsAsync(string id, IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        var row = await context.Notes.SingleOrDefaultAsync(note => note.Id == id, cancellationToken);
        if (row is null)
            return null;

        // The row is read through the chain first: a row still at 1.0.0 is upcast, and the write below restamps it 2.0.0.
        row.TagsJson = JsonSerializer.Serialize(TagsOf(row).Concat(tags).Distinct(StringComparer.Ordinal));
        row.SchemaVersion = NotesModule.TagsVersion;
        await context.SaveChangesAsync(cancellationToken);
        return ToNoteWithTags(row);
    }

    private static NoteWithTags ToNoteWithTags(NoteRecord row) => new(row.Id, row.Text, row.CreatedAt, TagsOf(row));

    /// <summary>
    /// The tags of a row, as the family's chain upcasts it. Whatever the row's version, what comes out of the chain has
    /// its tags, so a missing value here means the upcaster or the row is broken, and it is not read as no tags.
    /// </summary>
    private static string[] TagsOf(NoteRecord row)
    {
        var content = NotesModule.Chain.Upcast<NoteRecord>(row.SchemaVersion, (nameof(NoteRecord.TagsJson), row.TagsJson));
        var json = content[nameof(NoteRecord.TagsJson)]
            ?? throw new InvalidOperationException($"Note '{row.Id}' has no tags after it was upcast from version {row.SchemaVersion}. The notes upcaster or the row is broken.");
        return JsonSerializer.Deserialize<string[]>(json) ?? [];
    }
}
