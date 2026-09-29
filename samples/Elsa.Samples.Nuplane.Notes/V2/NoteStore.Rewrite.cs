using System.Text.Json;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Samples.Nuplane.Notes;

public sealed partial class NoteStore
{
    /// <summary>
    /// Brings one note up to the version the post-finalization backfill asks for, at the write version the finalization
    /// gate keeps for this host.
    /// </summary>
    /// <exception cref="InvalidOperationException">The gate has not admitted the module, so there is no write version to restamp with.</exception>
    public async Task<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, CancellationToken cancellationToken = default) =>
        await RewriteAsync(
            row,
            AdmittedWriteVersion() ?? throw new InvalidOperationException("The notes module has not been admitted by its finalization gate, so there is no write version to restamp a row with."),
            cancellationToken);

    /// <summary>
    /// Reads the note through the family's chain, so the version check and the upcaster apply, leaves it alone when it is
    /// already at the target, and otherwise writes its tags and <paramref name="writeVersion"/> back together with
    /// <c>SaveChanges</c>, the family's write path, so the write gate checks the stamp like any other write. The save is a
    /// compare-and-set: the stamp and the tags are concurrency tokens, so a note that changed since it was read (a tag
    /// added, a host on another release restamping it) is <see cref="EfSchemaRewriteOutcome.Conflict"/>, and the backfill
    /// asks again.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="writeVersion"/> is before the row's target version, which a row written back must never be.</exception>
    internal async Task<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, string writeVersion, CancellationToken cancellationToken = default)
    {
        var id = (string)row.Key[0]!;
        var stored = await context.Notes.SingleOrDefaultAsync(note => note.Id == id, cancellationToken);
        if (stored is null)
            return EfSchemaRewriteOutcome.Missing;

        NotesModule.Chain.EnsureReadable(stored.SchemaVersion);
        if (NotesModule.Chain.IsAtOrAfter(stored.SchemaVersion, row.TargetVersion))
            return EfSchemaRewriteOutcome.AlreadyCurrent;
        if (!NotesModule.Chain.IsAtOrAfter(writeVersion, row.TargetVersion))
            throw new InvalidOperationException($"Note '{id}' must reach version {row.TargetVersion}, but this host may write only {writeVersion}.");

        stored.TagsJson = JsonSerializer.Serialize(TagsOf(stored));
        stored.SchemaVersion = writeVersion;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return EfSchemaRewriteOutcome.Rewritten;
        }
        catch (DbUpdateConcurrencyException)
        {
            return EfSchemaRewriteOutcome.Conflict;
        }
    }
}
