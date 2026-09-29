using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Samples.Nuplane.Notes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Notes.Tests;

/// <summary>A note's tags as the proofs compare them: joined, so equal tags are equal values.</summary>
public sealed record NoteTags(string Text, string Tags);

/// <summary>
/// FR-022's three proofs for the notes family's one step, from 1.0.0 to 2.0.0, through the sample's own store: the upcast,
/// the old-format round trip, and the read of a 1.0.0 row and a 2.0.0 row as the same note.
/// </summary>
public sealed class NotesOneToTwoProof() : EfSchemaUpcasterProof<NotesOneToTwo, NoteTags>(NotesModule.Family, new NotesProofStore());

/// <summary>The store's half of FR-022's proofs: a fixture row is put in a SQLite database and read back through <see cref="NoteStore"/>.</summary>
internal sealed class NotesProofStore : IEfSchemaUpcasterProofStore<NoteTags>, IAsyncDisposable
{
    private const string Id = "note-1";
    private const string Text = "a note";
    private readonly TemporarySqliteDatabase database = new("notes-proof");
    private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();

    public NotesProofStore()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public EfSchemaChain Chain => NotesModule.Chain;

    public async Task<NoteTags> ReadAsync(EfSchemaUpcasterFixture _, string stamp, EfSchemaRowContent row)
    {
        await using (var context = NewContext())
        {
            var record = await context.Notes.SingleOrDefaultAsync(note => note.Id == Id);
            if (record is null)
                context.Notes.Add(record = new NoteRecord { Id = Id, Text = Text, CreatedAt = DateTimeOffset.UnixEpoch });
            record.SchemaVersion = stamp;
            record.TagsJson = row[nameof(NoteRecord.TagsJson)];
            await context.SaveChangesAsync();
        }

        await using var reading = NewContext();
        var note = (await new NoteStore(reading, services).ListWithTagsAsync()).Single();
        return new NoteTags(note.Text, string.Join(',', note.Tags));
    }

    public EfSchemaRowContent WriteAt(EfSchemaUpcasterFixture _, NoteTags value, string version) =>
        new(typeof(NoteRecord), (nameof(NoteRecord.TagsJson), Chain.IsAtOrAfter(version, NotesModule.TagsVersion)
            ? JsonSerializer.Serialize(value.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries))
            : null));

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }

    private NotesSqliteDbContext NewContext() =>
        new(new DbContextOptionsBuilder<NotesSqliteDbContext>().UseSqlite(database.ConnectionString).Options);
}
