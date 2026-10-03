using Elsa.Persistence.EntityFramework.SchemaBackfill;

namespace Elsa.Samples.Nuplane.Renewals;

/// <summary>
/// The family's rewriter. Once <c>2.0.0</c> is finalized and every host reads it, the host's post-finalization backfill
/// asks it, one row at a time, to bring the rows still stamped <c>1.0.0</c> up to <c>2.0.0</c>, so the family can be
/// recorded complete at that version. It is constructed for every row in a fresh scope of the shell, so it takes the
/// store, and the store does the work: it reads the row through the family's chain and writes it back, through the
/// family's write path, at the host's write version.
/// </summary>
public sealed class RenewalsRewriter(RenewalStore store) : IEfSchemaRowRewriter
{
    public ValueTask<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, CancellationToken cancellationToken = default) =>
        new(store.RewriteAsync(row, cancellationToken));
}
