namespace Elsa.Samples.Nuplane.Notes;

public sealed partial class NoteStore
{
    public async Task<Note> AddAsync(string text, CancellationToken cancellationToken = default)
    {
        var writeVersion = WriteVersion();
        var record = new NoteRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Text = text,
            CreatedAt = DateTimeOffset.UtcNow,
            SchemaVersion = writeVersion,
            // A row is written in the format of the version this host may write. Before 2.0.0 is finalized that is 1.0.0,
            // whose rows have no tags to hold, so it stays null; from then on a new row starts with an empty list.
            TagsJson = writeVersion == NotesModule.TagsVersion ? "[]" : null
        };
        context.Notes.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        return new Note(record.Id, record.Text, record.CreatedAt);
    }
}
