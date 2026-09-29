using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Notes;

/// <summary>A note as the base endpoints show it.</summary>
public sealed record Note(string Id, string Text, DateTimeOffset CreatedAt);

/// <summary>
/// Adds and lists notes. Every row it writes is stamped with the version this host may write, and every row it reads
/// has its stamp checked against what this build reads, so a row of a version no host here understands is refused
/// rather than misread.
/// </summary>
public sealed partial class NoteStore(NotesDbContext context, IServiceProvider services)
{
    public async Task<Note> AddAsync(string text, CancellationToken cancellationToken = default)
    {
        var writeVersion = WriteVersion();
        var record = new NoteRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Text = text,
            CreatedAt = DateTimeOffset.UtcNow,
            SchemaVersion = writeVersion
        };
        InitializeContent(record, writeVersion);
        context.Notes.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        return new Note(record.Id, record.Text, record.CreatedAt);
    }

    public async Task<IReadOnlyList<Note>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Notes.AsNoTracking()
            .Select(note => new { note.Id, note.Text, note.CreatedAt, note.SchemaVersion })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
            NotesModule.Chain.EnsureReadable(row.SchemaVersion);
        return [.. rows.OrderBy(row => row.CreatedAt).Select(row => new Note(row.Id, row.Text, row.CreatedAt))];
    }

    /// <summary>
    /// The version this host writes: the finalized version its finalization gate last observed. While a newer release
    /// is running beside an older one, that is still the older version, so rows this host writes stay readable by every
    /// host that shares the database. Before the gate has admitted the module, the oldest version this build reads.
    /// </summary>
    private string WriteVersion() =>
        services.GetService<EfSchemaFinalizationGates>()?.FindModuleGate(typeof(NotesDbContext))?.StateOf(NotesModule.Family)?.WriteVersion
        ?? NotesModule.Chain.ReadableVersions[0];

    /// <summary>Fills a new row's content columns in the format of <paramref name="writeVersion"/>.</summary>
    partial void InitializeContent(NoteRecord record, string writeVersion);
}
