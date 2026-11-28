using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // The coordinated migration's transactional data changes and its format marker land together.
    internal Task<bool> AdvanceStoreFormatAsync(StoreFormat expected, StoreFormat next,
        Func<KernelDb, CancellationToken, Task>? migrateData = null, CancellationToken cancellationToken = default) =>
        AdvanceStoreFormatAsync(expected, next, verifySource: null, migrateData, cancellationToken);

    private async Task<bool> AdvanceStoreFormatAsync(StoreFormat expected, StoreFormat next,
        Func<KernelDb, CancellationToken, Task>? verifySource, Func<KernelDb, CancellationToken, Task>? migrateData,
        CancellationToken cancellationToken)
    {
        if (next == expected || next.StoreSchema < expected.StoreSchema || next.MessageProtocol < expected.MessageProtocol ||
            next.JournalCodec < expected.JournalCodec || next is { StoreSchema: < 1 } or { MessageProtocol: < 1 } or { JournalCodec: < 1 })
            throw new ArgumentException("A store format migration must advance at least one family.", nameof(next));

        await using var db = create();
        await using var transaction = await BeginReadCommittedAsync(db, cancellationToken);
        var stored = await ReadStoreFormatAsync(db, FormatLock.Exclusive, cancellationToken);
        if (stored == next) return false;    // a resumed caller can observe an advance that already landed
        if (stored != expected)
            throw new StoreFormatException($"Cannot advance the kernel store from {expected}. The store records {stored}.");

        if (verifySource is not null) await verifySource(db, cancellationToken);

        var live = await db.Nodes.FromSqlRaw(Q($"""SELECT * FROM "Nodes" WHERE "LeaseUntil" >= {Now}""")).AsNoTracking()
            .OrderBy(node => node.Id).ToListAsync(cancellationToken);
        foreach (var node in live)
        {
            if (!Includes(node.ReadableStoreSchema, next.StoreSchema) ||
                !Includes(node.ReadableMessageProtocol, next.MessageProtocol) ||
                !Includes(node.ReadableJournalCodec, next.JournalCodec))
                throw new StoreFormatException(
                    $"Live node '{node.Id}' cannot read the proposed kernel store format {next}. " +
                    $"It advertises schema=[{node.ReadableStoreSchema}], message protocol=[{node.ReadableMessageProtocol}], journal codec=[{node.ReadableJournalCodec}].");
        }

        if (migrateData is not null) await migrateData(db, cancellationToken);
        var changed = await db.StoreVersions.Where(row => row.Id == StoreVersionEntity.MarkerId)
            .ExecuteUpdateAsync(row => row.SetProperty(value => value.StoreSchema, next.StoreSchema)
                .SetProperty(value => value.MessageProtocol, next.MessageProtocol).SetProperty(value => value.JournalCodec, next.JournalCodec),
                cancellationToken);
        if (changed != 1) throw new StoreFormatException("The kernel store version marker changed during migration.");
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // Only the exact previous shipped profile can advance. Existing checkpoints, jobs, and physical definitions retain
    // their encoding. The next build can read those checkpoints before publishing the first task-specific message.
    internal Task<bool> UpgradeShippedRootFormatAsync(CancellationToken cancellationToken) =>
        AdvanceStoreFormatAsync(StoreFormat.RootShippedBeforeUserTasks, StoreFormat.RootShipped, VerifySourceAsync,
            AdvanceRootAuthoritiesAsync, cancellationToken);

    private static async Task VerifySourceAsync(KernelDb db, CancellationToken token)
    {
        if (db.RequiredFormat != StoreFormat.RootShippedBeforeUserTasks)
            throw new InvalidOperationException("The coordinated task upgrade requires the previous shipped context.");

        if (db.UsesSqliteRootRuntime)
        {
            await RequireSqliteRootAdmissionAsync(db, db.SqliteRuntimeDefinitions!, token);
            await RequireSqliteFoundationShapeAsync(db, partitions: 1, create: false, token);
        }
        else if (db.Database.IsNpgsql())
            await PostgresRootAdmission.VerifyUserTaskUpgradeSourceAsync(db, token);
        else
            throw new NotSupportedException("Actor user task waits require a shipped SQLite or PostgreSQL store.");
    }

    private static async Task AdvanceRootAuthoritiesAsync(KernelDb db, CancellationToken token)
    {
        // SQLite uses one marker plus the provider definitions. PostgreSQL also pins the exact protocol in each
        // installed authority row. Those rows and the main protocol have to advance atomically.
        if (!db.Database.IsNpgsql()) return;
        int[] ids = [RootKeyedAdmission.AuthorityMarkerId, RootEventStarts.PublicAuthorityMarkerId,
            RetainedResponseAuthorityMarkerId, PostgresRootAdmission.PreparedWaitAuthorityMarkerId];
        var previous = StoreFormat.RootShippedBeforeUserTasks;
        var changed = await db.StoreVersions.Where(row => ids.Contains(row.Id) && row.StoreSchema == previous.StoreSchema &&
                row.MessageProtocol == previous.MessageProtocol && row.JournalCodec == previous.JournalCodec)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.MessageProtocol, StoreFormat.RootShipped.MessageProtocol), token);
        if (changed != ids.Length)
            throw new StoreFormatException("The old PostgreSQL root authority markers do not belong to the selected shipped format.");
    }

    private static bool Includes(string readable, int format) =>
        readable.Split(',').Contains(format.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);
}
