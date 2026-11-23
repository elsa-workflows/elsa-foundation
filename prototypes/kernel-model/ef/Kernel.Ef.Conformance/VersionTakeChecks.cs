using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kernel.Ef.Conformance;

internal static class VersionTakeChecks
{
    internal static async Task Run(Func<KernelDb> create, IKernelStore store, IKernelStore cold)
    {
        var lease = TimeSpan.FromSeconds(10);
        Check((await Snapshot()).Fences.All(row => row.Epoch == 0 && row.Owner is null),
            "a newly versioned database has unowned partitions");

        foreach (var change in new (Action<StoreVersionEntity> Set, string Versions)[]
        {
            (row => row.StoreSchema++, $"Stored schema={StoreVersionEntity.StoreSchemaVersion + 1},"),
            (row => row.StoreSchema--, $"Stored schema={StoreVersionEntity.StoreSchemaVersion - 1},"),
            (row => row.MessageProtocol++, $"message protocol={StoreVersionEntity.MessageProtocolVersion + 1},"),
            (row => row.JournalCodec--, $"journal codec={StoreVersionEntity.JournalCodecVersion - 1}.")
        })
        {
            await Set(change.Set);
            var before = await Snapshot();
            await Refuses(async () => { await cold.TakeAsync(0, "unknown-single", lease); }, change.Versions);
            await Refuses(async () => { await store.TakeAsync([0, 1], "unknown-set", lease); }, change.Versions);
            Check(before.Same(await Snapshot()), "the incompatible single and set takes leave epochs, owners and node records unchanged");
        }
        await Set(_ => { });
        var first = await store.TakeAsync([0, 1], "owner", lease);
        Check(first.Taken is [{ Epoch: 1 }, { Epoch: 1 }], "a supported marker admits an atomic set take");

        await Set(row => row.MessageProtocol = 0);
        var owned = await Snapshot();
        await Refuses(async () => { await store.RetakeAsync(0, "owner", 1, lease); }, "message protocol=0");
        Check(owned.Same(await Snapshot()), "refusing a retake leaves its existing node record and fence unchanged");
        await Set(_ => { });
        Check(await store.RetakeAsync(0, "owner", 1, lease) is { Epoch: 2 }, "retake succeeds after restoring a supported marker");

        await using (var db = create())
        {
            db.StoreVersions.Remove(await db.StoreVersions.SingleAsync());
            await db.SaveChangesAsync();
        }
        var known = await Snapshot();
        await Refuses(async () => { await cold.TakeAsync(0, "missing", lease); }, "version marker is missing");
        await Refuses(() => ((EfStore)cold).CreateAsync(2), "version marker is missing");
        Check(known.Same(await Snapshot()), "take and reopen refuse an absent marker without changing ownership");

        await using (var db = create())
        {
            var table = db.GetService<ISqlGenerationHelper>().DelimitIdentifier(StoreVersionEntity.Table);
            await db.Database.ExecuteSqlRawAsync($"DROP TABLE {table}");
        }
        await Refuses(async () => { await cold.TakeAsync(0, "unversioned", lease); }, "version marker table is missing");
        await Refuses(() => ((EfStore)cold).CreateAsync(2), "version marker table is missing");
        Check(known.Same(await Snapshot()), "an unversioned database is left untouched");
        return;

        async Task Set(Action<StoreVersionEntity> change)
        {
            await using var db = create();
            var row = await db.StoreVersions.SingleAsync();
            row.StoreSchema = StoreVersionEntity.StoreSchemaVersion;
            row.MessageProtocol = StoreVersionEntity.MessageProtocolVersion;
            row.JournalCodec = StoreVersionEntity.JournalCodecVersion;
            change(row);
            await db.SaveChangesAsync();
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

    internal static async Task Refuses(Func<Task> action, string reason, string operation = "operation")
    {
        try { await action(); }
        catch (StoreException error) when (error.Failure == Failure.Definite &&
            error.Message.Contains(reason, StringComparison.Ordinal))
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
