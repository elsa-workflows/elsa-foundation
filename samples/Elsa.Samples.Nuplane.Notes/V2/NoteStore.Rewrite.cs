using System.Text.Json;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Samples.Nuplane.Notes;

public sealed partial class NoteStore
{
    /// <summary>Brings one note up to the version the post-finalization backfill asks for, at the version this host writes.</summary>
    public Task<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, CancellationToken cancellationToken = default) =>
        RewriteAsync(row, WriteVersion(), cancellationToken);

    /// <summary>
    /// Reads the note through the family's chain, so the version check and the upcaster apply, leaves it alone when it is
    /// already at the target, and otherwise writes its tags and <paramref name="writeVersion"/> back together. The write is
    /// a compare-and-set on what was read, the stamp and the tags: a note that changed since (a tag added, a host on
    /// another release restamping it) is <see cref="EfSchemaRewriteOutcome.Conflict"/>, and the backfill asks again. It is
    /// an <c>ExecuteUpdate</c>, which compares in the statement itself, because this table has no revision column to make
    /// <c>SaveChanges</c> do it; the write version comes from the gate, as it does for <see cref="AddAsync"/>.
    /// </summary>
    public async Task<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, string writeVersion, CancellationToken cancellationToken = default)
    {
        var id = (string)row.Key[0]!;
        var stored = await context.Notes.AsNoTracking().SingleOrDefaultAsync(note => note.Id == id, cancellationToken);
        if (stored is null)
            return EfSchemaRewriteOutcome.Missing;

        NotesModule.Chain.EnsureReadable(stored.SchemaVersion);
        if (NotesModule.Chain.IsAtOrAfter(stored.SchemaVersion, row.TargetVersion))
            return EfSchemaRewriteOutcome.AlreadyCurrent;

        var (stamp, tagsJson) = (stored.SchemaVersion, stored.TagsJson);
        var tags = JsonSerializer.Serialize(TagsOf(stored));
        var written = await context.Notes
            .Where(note => note.Id == id && note.SchemaVersion == stamp && note.TagsJson == tagsJson)
            .ExecuteUpdateAsync(set => set.SetProperty(note => note.TagsJson, tags).SetProperty(note => note.SchemaVersion, writeVersion), cancellationToken);
        return written == 1 ? EfSchemaRewriteOutcome.Rewritten : EfSchemaRewriteOutcome.Conflict;
    }
}
