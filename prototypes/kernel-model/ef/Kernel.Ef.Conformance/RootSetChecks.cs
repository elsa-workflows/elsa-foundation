using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class RootSetChecks
{
    internal static async Task Run(Func<KernelDb> create, EfStore store)
    {
        var admission = new PostgresRootAdmission(create, TimeSpan.FromHours(1));
        await admission.InstallAsync();
        store.BindWorkerNode();
        var roots = new Dictionary<int, Message>();
        var admitted = new List<InboxKey>();
        for (var i = 0; roots.Count < 2; i++)
        {
            var receipt = await admission.AdmitAsync("demo", "root-set-" + i, new("native", "approval"));
            var creator = (await admission.ReadPendingCreatorAsync("demo", receipt.Reference))!;
            roots.TryAdd(creator.Target.Partition, creator);
            admitted.Add(creator.InboxKey);
        }
        var time = TimeSpan.FromMinutes(1);
        var original = (await store.TakeAsync([0, 1], "sets", time)).Taken;
        var a = roots[0].InboxKey;
        var b = roots[1].InboxKey;
        Check((await store.CommitAsync(CommitBatch.Owned(original, time, false, [], admitted, []))).Landed &&
            await Marks(a, original[0].Epoch, 0) && await Marks(b, original[1].Epoch, 0),
            "the initial epoch marks every admitted creator seen");
        var renewed = (await store.RetakeAsync(1, "sets", original[1].Epoch, time))!;
        Check(renewed.Epoch > original[1].Epoch && original[0].Epoch != renewed.Epoch, "the fixture has two distinct current epochs");
        var latest = new[] { original[0], new PartitionFence(1, renewed.Epoch, renewed.Position) };
        var limitOne = await store.PollAsync(latest, TimeSpan.Zero, 1);
        Check(limitOne.Messages is [var nextDue] && nextDue.Message.InboxKey == b,
            "the shared limit returns the retaken partition's stale mark and skips the other partition's current mark");
        Check(!(await store.CommitAsync(CommitBatch.Owned(original, time, false, [], [], [a, b]))).Landed &&
            await Marks(a, original[0].Epoch, 0) && await Marks(b, original[1].Epoch, 0), "one stale sibling preserves both original marks");
        Check((await store.CommitAsync(CommitBatch.Owned(latest, time, false, [], [a], [b]))).Landed &&
            await Marks(a, latest[0].Epoch, 0) && await Marks(b, latest[1].Epoch, latest[1].Epoch),
            "seen and trial take their own partition epochs");
        var next = await store.PollAsync(latest, TimeSpan.Zero, 100);
        Check(next.Messages.All(m => m.Message.InboxKey != a && m.Message.InboxKey != b),
            "the set poll uses both persisted epochs");
        async Task<bool> Marks(InboxKey key, long seen, long trial)
        {
            await using var db = create();
            return await db.Messages.AsNoTracking().AnyAsync(m => m.Id == key.Id && m.Seen == seen && m.Trial == trial);
        }
    }

    private static void Check(bool pass, string detail)
    {
        if (!pass) throw new InvalidOperationException(detail);
        Console.WriteLine("ok " + detail);
    }
}
