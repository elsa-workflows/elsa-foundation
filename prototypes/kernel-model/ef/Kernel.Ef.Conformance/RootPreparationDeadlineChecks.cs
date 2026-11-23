using System.Data.Common;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kernel.Ef.Conformance;

internal static class RootPreparationDeadlineChecks
{
    internal static async Task Run(Func<KernelDb> open, Func<IInterceptor, EfStore> intercepted, EfStore store, bool grouped)
    {
        var admission = new PostgresRootAdmission(open, TimeSpan.FromMinutes(2));
        await admission.InstallAsync();
        var birth = await admission.AdmitAsync("demo", "deadline", new("workflow", "approval"));
        var creator = (await admission.ReadPendingCreatorAsync("demo", birth.Reference))!;
        var partition = creator.Target.Partition;
        var lease = TimeSpan.FromSeconds(30);
        var taken = (await store.TakeAsync(partition, "deadline", lease))!;
        var fence = new PartitionFence(partition, taken.Epoch, taken.Position);
        var first = await store.CommitFirstRootAsync("demo", birth.Reference, creator, fence, lease, "waiting", 1, "header",
            new Dictionary<string, string>(), """["started"]""");
        if (!first.Landed) throw new InvalidOperationException("The fixture birth must land.");
        fence = fence with { Position = fence.Position + 1 };
        foreach (var operation in new[] { "apply", "mark" })
            if (await admission.AdmitDirectAsync("demo", birth.Reference, operation, "input") != true)
                throw new InvalidOperationException("Expected a fresh direct input.");
        var pending = await admission.ReadPendingDirectAsync("demo", birth.Reference);
        var pause = new PausePreparation();
        var owner = intercepted(pause);
        var fences = new List<PartitionFence> { fence };
        if (grouped)
        {
            owner.BindWorkerNode();
            var sibling = (await store.TakeAsync(1 - partition, "deadline", lease))!;
            fences.Add(new(1 - partition, sibling.Epoch, sibling.Position));
        }
        else await owner.BindWorkerRootAsync("demo", birth.Reference);
        var live = (await owner.LoadAsync([creator.Target.Id])).Single();
        var turn = new TurnCommit(live.Id, live with { Revision = 2 }, 1, [], [pending[0].InboxKey], [], [], [], """["next"]""");
        CommitBatch Batch(TimeSpan budget) => grouped
            ? CommitBatch.Owned(fences, budget, false, [turn], [pending[1].InboxKey], [])
            : new(partition, fence.Epoch, fence.Position, budget, false, [turn], [pending[1].InboxKey], []);

        var shortLease = TimeSpan.FromMilliseconds(500);
        pause.Next = shortLease + TimeSpan.FromMilliseconds(150);
        if ((await owner.CommitAsync(Batch(shortLease))).Landed || pause.Next != TimeSpan.Zero)
            throw new InvalidOperationException("A completed preparation read consumed the commit step's budget.");
        await using var db = open();
        var unchanged = await store.ReadFirstRootAsync("demo", birth.Reference);
        if (unchanged?.Revision != 1 || unchanged.State != live.State ||
            await db.Messages.CountAsync(m => m.Target == live.Id && !m.Done && m.Seen == 0 && m.Trial == 0) != 2 ||
            (await store.ReadJournalOfAsync(partition, live.Id, 0)).Count != 1 ||
            (await db.Partitions.OrderBy(p => p.Id).Select(p => p.Position).ToListAsync()).Sum() != fence.Position)
            throw new InvalidOperationException("The timed-out step changed the persisted inputs, marks, checkpoint or positions.");
        if (!(await owner.CommitAsync(Batch(lease))).Landed ||
            !await db.Instances.AnyAsync(i => i.Id == live.Id && i.Revision == 2) ||
            !await db.Messages.AnyAsync(m => m.Id == pending[1].InboxKey.Id && m.Seen == fence.Epoch) ||
            (await store.ReadJournalOfAsync(partition, live.Id, 0)).Count != 2)
            throw new InvalidOperationException("The same prepared input must commit with time remaining.");
        Console.WriteLine($"ok {(grouped ? "grouped" : "single")} root preparation counts toward the commit-step deadline and the timely retry lands");
    }

    private sealed class PausePreparation : DbCommandInterceptor
    {
        internal TimeSpan Next { get; set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Next > TimeSpan.Zero && command.Transaction is null)
            {
                var duration = Next;
                Next = TimeSpan.Zero;
                await Task.Delay(duration, cancellationToken);
            }
            return result;
        }
    }
}
