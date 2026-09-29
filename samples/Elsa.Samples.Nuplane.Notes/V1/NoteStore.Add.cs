namespace Elsa.Samples.Nuplane.Notes;

public sealed partial class NoteStore
{
    public async Task<Note> AddAsync(string text, CancellationToken cancellationToken = default)
    {
        var record = new NoteRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Text = text,
            CreatedAt = DateTimeOffset.UtcNow,
            SchemaVersion = WriteVersion()
        };
        context.Notes.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        return new Note(record.Id, record.Text, record.CreatedAt);
    }
}
