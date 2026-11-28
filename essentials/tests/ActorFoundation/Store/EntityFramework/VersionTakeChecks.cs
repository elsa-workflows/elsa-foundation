using System.Data.Common;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kernel.Ef.Conformance;

internal static class VersionTakeChecks
{
    internal static async Task Run(Func<KernelDb> create, IKernelStore store, IKernelStore cold,
        Func<IInterceptor, EfStore> observeWrites)
    {
        var lease = TimeSpan.FromSeconds(10);
        Check((await Snapshot()).Fences.All(row => row.Epoch == 0 && row.Owner is null),
            "a newly versioned database has unowned partitions");

        foreach (var unsupported in IncompatibleFormats())
        {
            await SetFormatAsync(create, unsupported);
            var before = await Snapshot();
            await Refuses(async () => { await cold.TakeAsync(0, "unknown-single", lease); }, $"Stored {unsupported}.");
            await Refuses(async () => { await store.TakeAsync([0, 1], "unknown-set", lease); }, $"Stored {unsupported}.");
            Check(before.Same(await Snapshot()), "the incompatible single and set takes leave epochs, owners and node records unchanged");
        }
        await SetFormatAsync(create, StoreFormat.Current);
        var first = await store.TakeAsync([0, 1], "owner", lease);
        Check(first.Taken is [{ Epoch: 1 }, { Epoch: 1 }], "a supported marker admits an atomic set take");

        Func<string, Task>? assertOwnedRefusal = null;
        await using (var profile = create())
            if (profile.Database.IsSqlite() && store is EfStore { OwnedCommit: true })
            {
                const string inputId = "versioned-commit-input";
                InstanceRef actor = new("0/versioned-commit");
                var writes = new OwnedWriteProbe();
                var observed = observeWrites(writes);
                var fence = first.Taken[0];
                var send = new TurnCommit(actor.Id, null, null, [], [], [new(inputId, actor, "input")], [], [], null);
                var batch = new CommitBatch(fence.Partition, fence.Epoch, fence.Position, lease, false, [send], [], []);
                assertOwnedRefusal = reason => CheckOwnedRefusalAsync(observed, batch, writes, reason);

                var unsupported = StoreFormat.Current with { MessageProtocol = 0 };
                await SetFormatAsync(create, unsupported);
                await assertOwnedRefusal($"Stored {unsupported}.");
                Check(!await profile.Messages.AnyAsync(message => message.Id == inputId), "the refused commit leaves no outbound input");

                await SetFormatAsync(create, StoreFormat.Current);
                Check((await observed.CommitAsync(batch)).Landed && writes.Calls == 2 &&
                    await profile.Messages.AnyAsync(message => message.Id == inputId),
                    "the same SQLite owner commits after restoring the supported marker");

                await using (var borrowed = new SqliteConnection(profile.Database.GetConnectionString()))
                {
                    borrowed.CreateFunction<long, int?, int?, int?, int>("kernel_require_owned_format", (_, _, _, _) => 42);
                    var borrowedOptions = new DbContextOptionsBuilder<KernelDb>().UseSqlite(borrowed).Options;
                    var borrower = new EfStore(() => new KernelDb(borrowedOptions), postgres: false, fenceInBatch: observed.FenceInBatch)
                    {
                        Engine = observed.Engine
                    };
                    var empty = batch with { Turns = [] };
                    Check((await borrower.CommitAsync(empty)).Landed && (await borrower.CommitAsync(empty)).Landed,
                        "the owner can commit repeatedly over a borrowed closed connection");
                    await borrowed.OpenAsync();
                    await using var lookup = borrowed.CreateCommand();
                    lookup.CommandText = "SELECT kernel_require_owned_format(1, NULL, NULL, NULL)";
                    Check(Convert.ToInt32(await lookup.ExecuteScalarAsync()) == 42, "the commit preserves the caller's SQLite function");
                }

                await profile.Database.OpenConnectionAsync();
                var connection = profile.Database.GetDbConnection();
                var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(connection).Options;
                await using var writeContext = new KernelDb(options);
                // Initialize EF's own SQLite functions before the caller opens its reader.
                _ = writeContext.Database.GetDbConnection();
                await using var command = connection.CreateCommand();
                command.CommandText = """SELECT "Id" FROM "Partitions" ORDER BY "Id" """;
                await using var reader = await command.ExecuteReaderAsync();
                Check(await reader.ReadAsync(), "the reused SQLite connection has an active reader");
                var reused = new EfStore(() => writeContext, postgres: false, fenceInBatch: observed.FenceInBatch)
                {
                    Engine = observed.Engine
                };
                Check((await reused.CommitAsync(batch with { Turns = [] })).Landed && await reader.ReadAsync(),
                    "an owned commit over an already-open SQLite connection leaves the caller's reader usable");
            }

        await SetFormatAsync(create, StoreFormat.Current with { MessageProtocol = 0 });
        var owned = await Snapshot();
        await Refuses(async () => { await store.RetakeAsync(0, "owner", 1, lease); }, "message protocol=0");
        Check(owned.Same(await Snapshot()), "refusing a retake leaves its existing node record and fence unchanged");
        await SetFormatAsync(create, StoreFormat.Current);
        Check(await store.RetakeAsync(0, "owner", 1, lease) is { Epoch: 2 }, "retake succeeds after restoring a supported marker");

        await using (var db = create())
        {
            db.StoreVersions.Remove(await db.StoreVersions.SingleAsync());
            await db.SaveChangesAsync();
        }
        var known = await Snapshot();
        await Refuses(async () => { await cold.TakeAsync(0, "missing", lease); }, "version marker is missing");
        await Refuses(() => ((EfStore)cold).CreateAsync(2), "version marker is missing");
        if (assertOwnedRefusal is not null) await assertOwnedRefusal("version marker is missing");
        Check(known.Same(await Snapshot()), "take and reopen refuse an absent marker without changing ownership");

        await using (var db = create())
        {
            var table = db.GetService<ISqlGenerationHelper>().DelimitIdentifier(StoreVersionEntity.Table);
            await db.Database.ExecuteSqlRawAsync($"DROP TABLE {table}");
        }
        await Refuses(async () => { await cold.TakeAsync(0, "unversioned", lease); }, "version marker table is missing");
        await Refuses(() => ((EfStore)cold).CreateAsync(2), "version marker table is missing");
        if (assertOwnedRefusal is not null) await assertOwnedRefusal("version marker table is missing");
        Check(known.Same(await Snapshot()), "an unversioned database is left untouched");
        return;

        async Task CheckOwnedRefusalAsync(EfStore observed, CommitBatch batch, OwnedWriteProbe writes, string reason)
        {
            var before = await Snapshot();
            var calls = writes.Calls;
            try { await Refuses(() => observed.CommitAsync(batch).AsTask(), reason, "the owned SQLite commit"); }
            catch (StoreException error) when (observed.FenceInBatch && error is
                { Failure: Failure.Unknown, InnerException: StoreFormatException refusal } &&
                refusal.Message.Contains(reason, StringComparison.Ordinal))
            {
                // The in-batch retry classifier tries to reread the fence and meets the same format refusal.
                Console.WriteLine($"ok the owned SQLite commit refused with {reason}");
            }
            Check(writes.Calls == calls && before.Same(await Snapshot()), "the refused owned commit attempts no write");
        }

        async Task<Ownership> Snapshot()
        {
            await using var db = create();
            var fences = await db.Partitions.OrderBy(row => row.Id)
                .Select(row => new Fence(row.Id, row.Owner, row.Epoch, row.Position)).ToArrayAsync();
            var nodes = await db.Nodes.OrderBy(row => row.Id)
                .Select(row => new Node(row.Id, row.SeenAt, row.LeaseUntil)).ToArrayAsync();
            return new(fences, nodes);
        }
    }

    private sealed class OwnedWriteProbe : DbConnectionInterceptor
    {
        internal int Calls { get; private set; }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            ((SqliteConnection)connection).CreateFunction("observe_owned_write", () => ++Calls);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TEMP TRIGGER observe_owned_fence BEFORE UPDATE ON "Partitions"
                BEGIN SELECT observe_owned_write(); END;
                CREATE TEMP TRIGGER observe_owned_send BEFORE INSERT ON "Messages"
                BEGIN SELECT observe_owned_write(); END;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal static StoreFormat[] IncompatibleFormats()
    {
        var current = StoreFormat.Current;
        return [current with { StoreSchema = current.StoreSchema - 1 }, current with { StoreSchema = current.StoreSchema + 1 },
            current with { MessageProtocol = current.MessageProtocol - 1 }, current with { MessageProtocol = current.MessageProtocol + 1 },
            current with { JournalCodec = current.JournalCodec - 1 }, current with { JournalCodec = current.JournalCodec + 1 }];
    }

    internal static async Task SetFormatAsync(Func<KernelDb> create, StoreFormat format)
    {
        await using var db = create();
        var row = await db.StoreVersions.SingleAsync();
        row.StoreSchema = format.StoreSchema;
        row.MessageProtocol = format.MessageProtocol;
        row.JournalCodec = format.JournalCodec;
        await db.SaveChangesAsync();
    }

    internal static async Task Refuses(Func<Task> action, string reason, string operation = "operation")
    {
        try { await action(); }
        catch (StoreFormatException error) when (error.Message.Contains(reason, StringComparison.Ordinal))
        {
            Check(true, $"{operation} refused with {reason}");
            return;
        }
        throw new InvalidOperationException($"Expected {operation} to refuse with {reason}.");
    }

    private static void Check(bool valid, string title)
    {
        if (!valid) throw new InvalidOperationException(title);
        Console.WriteLine($"ok {title}");
    }

    private sealed record Fence(int Id, string? Owner, long Epoch, long Position);
    private sealed record Node(string Id, long SeenAt, long LeaseUntil);
    private sealed record Ownership(Fence[] Fences, Node[] Nodes)
    {
        internal bool Same(Ownership after) => Fences.SequenceEqual(after.Fences) && Nodes.SequenceEqual(after.Nodes);
    }
}
