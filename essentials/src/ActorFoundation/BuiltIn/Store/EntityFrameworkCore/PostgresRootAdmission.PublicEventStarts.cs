using System.Data;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    internal async Task SetPublicEventStartAsync(string tenant, string key, string wait, Contract.StartSpec? spec, CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootPublicEventStarts) throw new NotSupportedException("The public event start profile is unavailable.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var pin = await PinRootNamespaceAsync(db, loadIssuer: false, token) ??
            throw new StoreFormatException("Public event starts require the pinned PostgreSQL store.");
        await LockPublicEventStartTablesAsync(db, token);
        var qualified = tenant + "_" + key;
        await RootEventAdmission.LockPostgresKeysAsync(db, [qualified], token);
        await RootPublicEventStarts.SetAsync(db, tenant, qualified, wait, spec, sql => BindPublicEventStarts(sql, pin), token);
        await transaction.CommitAsync(token);
    }

    private static Task LockPublicEventStartTablesAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            LOCK TABLE ONLY "FoundationPublicEventStarts", ONLY "FoundationPublicEventTargets" IN ACCESS SHARE MODE
            """, token);

    private static FormattableString BindPublicEventStarts(FormattableString sql, PinnedRootSource pin) =>
        WithAdmissionSource(sql, pin, RootPublicEventStarts.Source, 2,
            """('"FoundationPublicEventStarts"'), ('"FoundationPublicEventTargets"')""", requirePermanentDirectRelations: true);
}
