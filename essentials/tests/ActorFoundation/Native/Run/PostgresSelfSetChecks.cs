using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

internal static class PostgresSelfSetChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh dedicated database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootSelf(options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stop = timeout.Token;
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        await store.CreateAsync(2);
        var ingress = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await ingress.InstallAsync(stop);
        await store.EnableRootSelfSendsAsync(stop);
        if (!owned)
        {
            try { store.BindWorkerNode(); }
            catch (NotSupportedException) when (!store.OwnedSetsAvailable)
            {
                await using var untouched = Open();
                Require(!await untouched.Partitions.AnyAsync(p => p.Owner != null, stop) &&
                    !await untouched.Messages.AnyAsync(stop), "the EF writer declines grouped ownership before changing the database");
                return 0;
            }
            throw new InvalidOperationException("Unexpected grouped EF writer.");
        }
        store.BindWorkerNode();

        const string tenant = "flow-set";
        var life = TimeSpan.FromMinutes(1);
        var fences = (await store.TakeAsync([0, 1], "set-host", life, stop)).Taken.OrderBy(f => f.Partition).ToArray();
        var emitted = new Dictionary<int, Message>();
        var turns = new List<TurnCommit>();
        for (var i = 0; emitted.Count < 2; i++)
        {
            var reference = (await ingress.AdmitAsync(tenant, "birth-" + i, new("native", "set"), cancellationToken: stop)).Reference;
            var creator = (await ingress.ReadPendingCreatorAsync(tenant, reference, stop))!;
            var p = creator.Target.Partition;
            if (emitted.ContainsKey(p)) continue;
            Require((await store.CommitFirstRootAsync(tenant, reference, creator, fences[p], life, null, 1, "root",
                new Dictionary<string, string>(), """["birth"]""", stop)).Landed, "initial checkpoint");
            fences[p] = fences[p] with { Position = 1 };
            var id = creator.Target.Id;
            var handle = FoundationSelfSend.Issue(store.RootSelfIssuer!.Handle, CreationProtocol.Origin(id, 1), 2, 0, null);
            var follow = FoundationSelfSend.Create(id, 1, handle, "step", "resume");
            emitted.Add(p, follow);
            await ingress.AdmitDirectAsync(tenant, reference, "begin", "begin", cancellationToken: stop);
            var direct = (await ingress.ReadPendingDirectAsync(tenant, reference, stop)).Single();
            var row = (await store.LoadAsync([id], stop)).Single();
            turns.Add(new(id, row with { Revision = 2 }, 1, [], [direct.InboxKey], [follow], [], [], null));
        }
        fences[1] = (await store.TakeAsync([1], "set-host", life, stop)).Taken.Single();
        Require(fences[1].Epoch > fences[0].Epoch && (await store.CommitAsync(CommitBatch.Owned(fences, life, false, turns, [], []), stop)).Landed,
            "two silent checkpoints emit scoped continuations under different epochs");
        await using var db = Open();
        Require(await db.Database.SqlQuery<bool>($"""
            SELECT count(*) = 2 AND count(DISTINCT m.xmin::text) = 1 AND bool_and(m.xmin = i.xmin) AS "Value"
            FROM "Messages" m JOIN "Instances" i ON i."Id" = m."Target" AND i."Partition" = m."Partition"
            WHERE m."InboxScope" = {EfStore.RootSelfScope} AND i."Revision" = 2
            """).SingleAsync(stop), "both continuations share one committing transaction with both checkpoints");

        var keys = new[] { emitted[0].InboxKey, emitted[1].InboxKey };
        Require((await store.CommitAsync(CommitBatch.Owned(fences, life, false, [], keys, []), stop)).Landed, "grouped marks land");
        var landed = await db.Messages.AsNoTracking().Where(m => m.InboxScope == EfStore.RootSelfScope).OrderBy(m => m.Partition).ToListAsync(stop);
        Require(landed.Select(m => m.Seen).SequenceEqual(fences.Select(f => f.Epoch)), "each private input keeps its own epoch");
        var fresh = new[] { fences[0], (await store.TakeAsync([1], "set-host", life, stop)).Taken.Single() };
        var ready = (await store.PollAsync(fresh, TimeSpan.Zero, 100, stop)).Messages.Where(m => m.Message.InboxKey.Scope == -1).ToArray();
        Require(ready.Length == 1 && ready[0].Message == emitted[1], "the retaken home redelivers its exact private envelope");
        turns.Clear();
        foreach (var p in new[] { 0, 1 })
        {
            var row = (await store.LoadAsync([emitted[p].Target.Id], stop)).Single();
            turns.Add(new(row.Id, row with { Revision = 3 }, 2, [keys[p]], [], [], [], [], p == 0 ? """["step"]""" : null));
        }
        Require(!(await store.CommitAsync(CommitBatch.Owned(fences, life, false, turns, [], []), stop)).Landed &&
            await db.Messages.CountAsync(m => m.InboxScope == EfStore.RootSelfScope, stop) == 2 &&
            await db.Instances.AllAsync(i => i.Revision == 2, stop), "one stale home preserves both inputs and both checkpoints");
        Require((await store.CommitAsync(CommitBatch.Owned(fresh, life, false, turns, [], []), stop)).Landed &&
            !await db.Messages.AnyAsync(m => m.InboxScope == EfStore.RootSelfScope, stop) &&
            await db.Instances.AllAsync(i => i.Revision == 3, stop), "both consumed inputs settle with their checkpoints");
        var positions = await db.Partitions.OrderBy(p => p.Id).Select(p => p.Position).ToListAsync(stop);
        Require(positions.SequenceEqual([2L, 1L]), "only the partition with new journal entries advances");
        return 0;
    }

    private static void Require(bool ok, string detail)
    {
        if (!ok) throw new InvalidOperationException(detail);
        Console.WriteLine("ok " + detail);
    }
}
