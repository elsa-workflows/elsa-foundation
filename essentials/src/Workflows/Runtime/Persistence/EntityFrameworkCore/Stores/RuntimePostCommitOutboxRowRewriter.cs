using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Backfills through the outbox's checked read/write path and existing revision CAS.</summary>
internal sealed class RuntimePostCommitOutboxRowRewriter(RuntimeDbContext context) : IEfSchemaRowRewriter
{
    public async ValueTask<EfSchemaRewriteOutcome> RewriteAsync(EfSchemaRowToRewrite request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Entity != typeof(RuntimePostCommitOutboxEntity) || request.Key.Count != 1 || request.Key[0] is not string id ||
            request.TargetVersion != RuntimePostCommitOutboxEfModule.SchemaVersion)
            throw new ArgumentException("The outbox rewriter requires one exact outbox key and its current target.", nameof(request));
        var row = await context.RuntimePostCommitOutbox.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null) return EfSchemaRewriteOutcome.Missing;
        var scope = EfRelationalIdentity.Decode(row.ScopeKey);
        var item = EfRuntimePostCommitOutboxStore.ReadChecked(row, scope);
        if (row.SchemaVersion == request.TargetVersion) return EfSchemaRewriteOutcome.AlreadyCurrent;
        var version = RuntimePostCommitOutboxWriteVersion.For(context, item);
        if (version != request.TargetVersion)
            throw new EfSchemaWriteRefusedException(RuntimePostCommitOutboxEfModule.SchemaFamily, version, request.TargetVersion);
        EfRuntimePostCommitOutboxStore.Copy(row, item, scope, checked(row.Revision + 1), version);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return EfSchemaRewriteOutcome.Rewritten;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.Entry(row).State = EntityState.Detached;
            return EfSchemaRewriteOutcome.Conflict;
        }
        catch
        {
            context.Entry(row).State = EntityState.Detached;
            throw;
        }
    }
}
