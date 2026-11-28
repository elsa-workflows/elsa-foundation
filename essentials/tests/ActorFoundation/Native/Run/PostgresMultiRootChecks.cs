using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using static Kernel.Native.Run.PostgresMultiRootClient;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresMultiRootChecks
{
    internal static async Task<int> RunAsync(string[] references, bool owned, bool inBatch)
    {
        var birth = references.Length == 0;
        var open = Database();
        var store = new EfStore(open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        var ingress = new PostgresRootAdmission(open, TimeSpan.FromHours(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stop = timeout.Token;
        if (birth)
        {
            await store.CreateAsync(1);
            await ingress.InstallAsync(stop);
        }
        var foundation = new PostgresPreviewFoundation(ingress,
            new(store, await ingress.OpenJournalCursorAsync(stop), 64), WorkflowActor.WorkflowActor.ActorType);
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());
        var scope = EmbeddedTenant.Bind(Primary);
        string[] tenants = [Primary, Secondary, Primary];
        Contract.ActorRef[] actors = birth ? [await Start(foundation, scope, "start", stop),
            new(await CallSecondaryAsync(stop, "start")), await Start(foundation, scope, "unbound", stop)]
            : references.Select(id => new Contract.ActorRef(id)).ToArray();
        Check(actors.Distinct().Count() == 3, "distinct generated roots");
        var turns = new List<string>();
        var requested = actors.Take(2).Select((actor, i) => (tenants[i], actor.Id)).ToArray();
        var refused = false;
        try { await store.BindWorkerRootsAsync([(Primary, actors[1].Id), requested[0]], stop); }
        catch (InvalidOperationException) { refused = true; }
        Check(refused, "tenant mismatch refuses the entire binding");
        var prepared = await PostgresNativeRootOwner.PrepareAsync(store, "multi-" + Environment.ProcessId,
            requested, registry, stop, (actor, _, _) => turns.Add(actor.Id));
        Check(!store.SupportsJobTrialClear, "a scoped PostgreSQL owner does not accept grouped job trial clearing");
        await using var owner = await prepared.TryActivateAsync(stop) ?? throw new InvalidOperationException("Occupied partition.");
        if (birth)
        {
            await Pair(1, 1);
            await Send(1, "one", "B1");
            await Drive(1, 2);
            Console.WriteLine($"multi-references {string.Join(" ", actors.Select(actor => actor.Id))}");
        }
        else
        {
            Check((await Read(0)).Revision == 1 && (await Read(1)).Revision == 2, "cold load separate revisions");
            await Send(1, "one", "changed", duplicate: true);
            await Send(0, "one", "A1");
            await Send(1, Secondary + "-two", "B2");
            await CheckRollback();
            await Pair(2, 3);
            await Send(0, Primary + "-two", "A2");
            await Drive(3, 3);
            for (var i = 0; i < 2; i++)
            {
                var image = await Read(i);
                Check(image.Status == InstanceStatus.Finished && Json.Read<NativeState>(image.State!).Results["approve"]!.AsArray()
                        .Select(n => n!.GetValue<string>()).SequenceEqual(i == 0 ? ["A1", "A2"] : new[] { "B1", "B2" }),
                    "root state and journal stay isolated through completion");
            }
            await CheckReads(foundation, scope, actors[0], actors[1], stop);
            await CallSecondaryAsync(stop, "reads", actors[1].Id, actors[0].Id);
        }
        Check(await foundation.GetAsync(scope, actors[2], stop) is null
            && await ingress.ReadPendingCreatorAsync(Primary, actors[2].Id, stop) is not null, "unbound root remains untouched");
        Console.WriteLine($"passed multi-root {(birth ? "birth" : "cold replay and completion")} in process {Environment.ProcessId}");
        return 0;

        async Task<EfStore.RootImage> Read(int i) => await store.ReadFirstRootAsync(tenants[i], actors[i].Id, stop)
            ?? throw new InvalidOperationException("Expected a persisted root.");
        async Task Send(int i, string id, string name, bool duplicate = false)
        {
            var frame = Json.Read<NativeState>((await Read(i)).State!).Frames.Single().Key;
            var payload = Json.Write(new CompositeSignal(frame, "approve", name));
            var expected = duplicate ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted;
            if (i == 1) await CallSecondaryAsync(stop, "send", actors[i].Id, id, payload, expected.ToString());
            else await PostgresMultiRootClient.Send(foundation, scope, actors[i], id, payload, expected, stop);
        }
        async Task Pair(long a, long b)
        {
            turns.Clear();
            await Drive(a, b, together: true);
            Check(turns.Count == 2 && turns.Distinct().Count() == 2, "two roots reach model turns in a single pass");
            var left = (await Read(0)).Actor.Id;
            var right = (await Read(1)).Actor.Id;
            await using var db = open();
            var transactions = await db.Database.SqlQuery<string>($"""
                SELECT xmin::text AS "Value" FROM "Journal"
                WHERE ("Instance" = {left} AND "Revision" = {a}) OR ("Instance" = {right} AND "Revision" = {b})
                """).ToListAsync(stop);
            Check(transactions.Count == 2 && transactions[0] == transactions[1], "both journal revisions land in the same PostgreSQL transaction");
        }
        async Task Drive(long a, long b, bool together = false)
        {
            await owner.WokenAsync(null, stop);
            for (var pass = 0; pass < 8; pass++)
            {
                var prior = turns.Count;
                await owner.RunAsync(stop);
                if (together && turns.Count > prior) Check(turns.Count - prior == 2, "both roots dispatch in the same pass");
                if ((await store.ReadFirstRootAsync(Primary, actors[0].Id, stop))?.Revision == a &&
                    (await store.ReadFirstRootAsync(Secondary, actors[1].Id, stop))?.Revision == b) return;
            }
            throw new InvalidOperationException($"Missing root revisions {a} and {b}.");
        }
        async Task CheckRollback()
        {
            var fence = ((Worker)owner).Fences.Single();
            var roots = new[] { await Read(0), await Read(1) };
            var batch = new List<TurnCommit>();
            foreach (var (image, i) in roots.Select((image, i) => (image, i)))
            {
                var input = (await ingress.ReadPendingDirectAsync(tenants[i], actors[i].Id, stop)).Single();
                var row = (await store.LoadAsync([image.Actor.Id], stop)).Single() with
                    { Revision = image.Revision + 1, Status = i == 1 ? InstanceStatus.Finished : InstanceStatus.Running };
                batch.Add(new(row.Id, row, image.Revision, [], [input.InboxKey], [], [], [], """["probe"]""", Unsubscribe: i == 1));
            }
            await using var db = open();
            var blocked = new SubscriptionEntity { Key = "test", Instance = roots[1].Actor.Id, Wait = "open", Partition = fence.Partition };
            db.Subscriptions.Add(blocked);
            await db.SaveChangesAsync(stop);
            var result = await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, batch, [], []), stop);
            Check(!result.Landed && (await Read(0)).Revision == 1 && (await Read(1)).Revision == 2
                && (await ingress.ReadPendingDirectAsync(Primary, actors[0].Id, stop)).Count == 1
                && (await ingress.ReadPendingDirectAsync(Secondary, actors[1].Id, stop)).Count == 1
                && await db.Partitions.Where(p => p.Id == fence.Partition).Select(p => p.Position).SingleAsync(stop) == fence.Position,
                "second root's terminal refusal rolls back both checkpoints, settled inputs and the journal position");
            var unsupported = batch[1] with { Sent = [new Message("forbidden", new(roots[0].Actor.Id), "work")] };
            var shapeRefused = false;
            try { await store.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [batch[0], unsupported], [], []), stop); }
            catch (NotSupportedException) { shapeRefused = true; }
            Check(shapeRefused && (await Read(0)).Revision == 1, "unsupported sibling effects refuse the whole batch");
            db.Subscriptions.Remove(blocked);
            await db.SaveChangesAsync(stop);
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
    }
}
