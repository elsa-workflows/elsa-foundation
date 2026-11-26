using System.Data.Common;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kernel.Ef.Conformance;

internal static class RootPreparationDeadlineChecks
{
    internal static async Task RunManual(Func<KernelDb> open, Func<IInterceptor, EfStore> intercepted, EfStore store)
    {
        const string tenant = "demo";
        var lease = TimeSpan.FromSeconds(30);
        var budget = TimeSpan.FromMilliseconds(500);
        var pause = new PausePreparation("KernelStoreVersions");
        var writer = intercepted(pause);
        var roots = new PostgresRootAdmission(open, TimeSpan.FromMinutes(2));
        await roots.InstallAsync();
        var reference = (await roots.AdmitAsync(tenant, "manual-deadline", new("native", "approval"))).Reference;
        var creator = (await roots.ReadPendingCreatorAsync(tenant, reference))!;
        var partition = creator.Target.Partition;
        var taken = (await store.TakeAsync(partition, "manual-deadline", lease))!;
        var fence = new PartitionFence(partition, taken.Epoch, taken.Position);
        var labels = new Dictionary<string, string> { ["step"] = "waiting" };
        await using var db = open();
        Task<long> Position() => db.Partitions.Where(p => p.Id == partition).Select(p => p.Position).SingleAsync();
        ValueTask<Committed> First(TimeSpan time) => writer.CommitFirstRootAsync(tenant, reference, creator, fence, time,
            "waiting", 1, "layout", labels, """["started"]""");

        pause.Next = budget + TimeSpan.FromMilliseconds(150);
        if ((await First(budget)).Landed || pause.Next != TimeSpan.Zero ||
            await roots.ReadPendingCreatorAsync(tenant, reference) != creator ||
            await db.Instances.AnyAsync(i => i.Id == creator.Target.Id) ||
            (await store.ReadJournalOfAsync(partition, creator.Target.Id, 0)).Count != 0 ||
            await Position() != fence.Position)
            throw new InvalidOperationException("An expired manual first checkpoint must preserve the creator, empty checkpoint and journal, and fence position.");
        var first = await First(lease);
        fence = fence with { Position = fence.Position + 1 };
        var current = await store.ReadFirstRootAsync(tenant, reference);
        var journal = await store.ReadJournalOfAsync(partition, creator.Target.Id, 0);
        if (!first.Landed || current is not { Status: InstanceStatus.Running, Revision: 1, State: "waiting" } ||
            await roots.ReadPendingCreatorAsync(tenant, reference) is not null || journal.Count != 1 || await Position() != fence.Position)
            throw new InvalidOperationException("The same manual first checkpoint must settle exactly once with time remaining.");
        Console.WriteLine("ok the delayed manual first checkpoint preserved the creator, checkpoint, journal and fence position, and the timely retry landed");

        if (await roots.AdmitDirectAsync(tenant, reference, "approve", "approval", "Ada") != true)
            throw new InvalidOperationException("Expected a fresh direct input.");
        var input = (await roots.ReadPendingDirectAsync(tenant, reference)).Single();
        ValueTask<Committed> Next(TimeSpan time) => writer.CommitRootTurnAsync(tenant, reference, current, [input], fence, time,
            InstanceStatus.Finished, "approved", 1, "layout", labels, """["approved"]""");

        pause.Next = budget + TimeSpan.FromMilliseconds(150);
        var late = await Next(budget);
        var unchanged = (await store.ReadFirstRootAsync(tenant, reference))!;
        var comparableCheckpoint = unchanged with { AsOf = current.AsOf, Labels = current.Labels };
        if (late.Landed || pause.Next != TimeSpan.Zero || comparableCheckpoint != current ||
            !unchanged.Labels.SequenceEqual(current.Labels) ||
            !(await roots.ReadPendingDirectAsync(tenant, reference)).SequenceEqual([input]) ||
            !(await store.ReadJournalOfAsync(partition, creator.Target.Id, 0)).SequenceEqual(journal) ||
            await Position() != fence.Position)
            throw new InvalidOperationException("An expired manual continuation must preserve the exact input, checkpoint, journal and fence position.");
        if (!(await Next(lease)).Landed ||
            await store.ReadFirstRootAsync(tenant, reference) is not { Status: InstanceStatus.Finished, Revision: 2, State: "approved" } ||
            (await roots.ReadPendingDirectAsync(tenant, reference)).Count != 0 ||
            (await store.ReadJournalOfAsync(partition, creator.Target.Id, 0)).Count != 2 ||
            await Position() != fence.Position + 1)
            throw new InvalidOperationException("The same manual continuation must settle exactly once with time remaining.");
        Console.WriteLine("ok the delayed manual continuation preserved the exact input, checkpoint, journal and fence position, and the timely retry landed");
    }

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

    private sealed class PausePreparation(string? source = null) : DbCommandInterceptor
    {
        internal TimeSpan Next { get; set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Next > TimeSpan.Zero && command.Transaction is null &&
                (source is null || command.CommandText.Contains(source, StringComparison.Ordinal)))
            {
                var duration = Next;
                Next = TimeSpan.Zero;
                await Task.Delay(duration, cancellationToken);
            }
            return result;
        }
    }
}
