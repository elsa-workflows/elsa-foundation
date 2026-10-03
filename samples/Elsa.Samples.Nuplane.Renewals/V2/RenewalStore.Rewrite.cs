using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Samples.Nuplane.Renewals;

public sealed partial class RenewalStore
{
    public async Task<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, CancellationToken cancellationToken = default) =>
        await RewriteAsync(
            row,
            AdmittedWriteVersion() ?? throw new InvalidOperationException("The renewals module has not been admitted by its finalization gate, so there is no write version to restamp a row with."),
            cancellationToken);

    internal async Task<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite row, string writeVersion, CancellationToken cancellationToken = default)
    {
        var id = (string)row.Key[0]!;
        var stored = await context.Renewals.SingleOrDefaultAsync(renewal => renewal.Id == id, cancellationToken);
        if (stored is null)
            return EfSchemaRewriteOutcome.Missing;

        RenewalsModule.Chain.EnsureReadable(stored.SchemaVersion);
        if (RenewalsModule.Chain.IsAtOrAfter(stored.SchemaVersion, row.TargetVersion))
            return EfSchemaRewriteOutcome.AlreadyCurrent;
        if (!RenewalsModule.Chain.IsAtOrAfter(writeVersion, row.TargetVersion))
            throw new InvalidOperationException($"Renewal '{id}' must reach version {row.TargetVersion}, but this host may write only {writeVersion}.");

        // The nullable scalar needs no content rewrite. Restamping is the complete backfill operation.
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
