namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// A schema family's rewriter (spec 186, FR-004): it reads one row through the family's read path, so the version check,
/// the integrity clauses for the stamped version and the chain all apply (spec 180, FR-006 to FR-012), and writes it back
/// through the family's write path, so projections a newer version introduced are computed, the stamp is the host's
/// write version, every declared content column is written, and the write is a compare-and-set on the row's revision
/// (spec 180, FR-013 to FR-018). It lives in the family's own EF module assembly, beside its store code, and the family's
/// <see cref="EfSchemaFamilyAttribute.Rewriter"/> names it.
/// </summary>
/// <remarks>
/// <para>
/// The post-finalization backfill constructs it in a fresh service scope of the shell for every row, so unlike an
/// upcaster it may take injected services: the stores, serializers and payload codec the family's own write path uses.
/// A generic row copier would bypass the projections and integrity clauses a new version introduces, which are exactly
/// what a feature querying the new data reads (spec 186, Decisions).
/// </para>
/// <para>
/// A row is rewritten whole or not at all: one save writes every content column and the stamp together. A read that
/// meets a stamp outside the readable set throws <see cref="EfSchemaVersionSkewException"/>, and one whose upcaster
/// fails throws <see cref="InvalidDataException"/>; the backfill reports both and never skips them silently (FR-006). A
/// lost compare-and-set is <see cref="EfSchemaRewriteOutcome.Conflict"/>, or the save's own concurrency exception, and
/// the backfill asks again, so the rewriter reads the row afresh and decides again (FR-007).
/// </para>
/// <para>
/// It never rewrites a content-addressed row: the backfill never asks it to (FR-010a).
/// </para>
/// </remarks>
public interface IEfSchemaRowRewriter
{
    /// <summary>Reads <paramref name="row"/> through the family's read path and, when it is below the target, writes it back.</summary>
    ValueTask<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, CancellationToken cancellationToken = default);
}

/// <summary>
/// One row the backfill asks a rewriter to upgrade: the type the family's context maps to its table, its primary key
/// values in the order the model declares them, the stamp it was selected at, and the version it must reach.
/// </summary>
/// <param name="Stamp">
/// The stamp the row carried when it was selected. The rewriter reads the row again and decides from what it reads.
/// </param>
/// <param name="TargetVersion">
/// The version the row must reach: one read at it or after it is left alone (<see cref="EfSchemaRewriteOutcome.AlreadyCurrent"/>).
/// A row the rewriter does write carries the host's write version at the moment of the write, which is never before it
/// (spec 186, FR-003).
/// </param>
public sealed record EfSchemaRowToRewrite(Type Entity, IReadOnlyList<object?> Key, string? Stamp, string TargetVersion);

/// <summary>What a rewriter did with one row.</summary>
public enum EfSchemaRewriteOutcome
{
    /// <summary>The row was read below the target and written at the host's write version.</summary>
    Rewritten,

    /// <summary>The row was read at the target or after it, so nothing was written.</summary>
    AlreadyCurrent,

    /// <summary>The row no longer exists.</summary>
    Missing,

    /// <summary>The row changed between the read and the write, so its compare-and-set wrote nothing; ask again.</summary>
    Conflict
}
