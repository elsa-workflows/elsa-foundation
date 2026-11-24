using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresRootParkOpeningChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        const string fault = "deliberate actor failure";
        const string original = "{\"before\":true}";
        const string payload = "{\"retain\":\"this input\"}";
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootParkOpening(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stop = timeout.Token;
        await using (var db = Open())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the Park fixture uses an empty PostgreSQL database");
        var store = Store();
        await store.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1), lockOrdinaryWrites: true);
        await admission.InstallAsync(stop);
        var tenant = ContractScopes.Bind("park-preview");
        var probe = new Probe(original, fault);
        var ingress = new PostgresRootIngress(admission, probe.Type);
        var accepted = await ingress.SendAsync(tenant, new Contract.Target.Start(new(probe.Type, "{}")),
            "start", Contract.Kinds.Start, cancellationToken: stop);
        var actor = accepted.Actor ?? throw new InvalidOperationException("The creator was refused.");
        EfStore.RootImage before;
        long firstEpoch;
        await using (var owner = await Own("first"))
        {
            before = await Drive(owner, row => row is { Revision: 1, Status: InstanceStatus.Running });
            firstEpoch = owner.Epoch;
            Check(before.State == original, "the real actor committed its initial checkpoint");
            await using (var db = Open())
            {
                db.Subscriptions.AddRange(
                    new SubscriptionEntity { Key = "signal", Instance = before.Actor.Id, Wait = "w", Partition = 0 },
                    new SubscriptionEntity { Key = "signal", Instance = "0/other", Wait = "w", Partition = 0 });
                await db.SaveChangesAsync(stop);
            }
            Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "poison", "fail", payload,
                cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Accepted }, "the ordinary ingress admits the failing input");
            var at = before.AsOf + 120_000;
            Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "later", "ordinary", "{}",
                new(DueAt: at), stop) is { Outcome: Contract.AdmissionOutcome.Accepted }, "the ordinary ingress admits a future input");
            var parked = await Drive(owner, row => row is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(parked.State == before.State && parked.TypeData == before.TypeData && parked.StateVersion == before.StateVersion &&
                probe.Failed == 1, "the failed turn records Park and preserves the committed actor state and header");
        }

        Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "poison", "fail", payload,
            cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Duplicate },
            "the already admitted delivery still returns its receipt after Park");
        Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "new", "ordinary", "{}",
            cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Refused },
            "the opening profile refuses fresh arrivals until it can retain them");
        await using (var prior = new KernelDb(options))
        {
            try
            {
                await EfStore.RequireStoreVersionsAsync(prior, stop);
                throw new InvalidOperationException("The prior context attached to the Park format.");
            }
            catch (StoreFormatException) { Console.WriteLine("ok the older context refuses the Park format"); }
        }
        var journal = await store.ReadJournalOfAsync(0, before.Actor.Id, 0, stop);
        Check(journal is [_, { Revision: 2, Entries: "[\"parked\"]" }], "the original journal contains one Park entry");
        var inputs = await ReadInputs(before.Actor.Id);
        Check(inputs is [{ Kind: "fail", Done: true, Trial: 0, Payload: payload, KeptBy: (short)ParkKeepReason.Consumed },
            { Kind: "ordinary", Done: false, KeptBy: null }], "only the failing input becomes a kept occurrence and retains its payload");
        await using (var db = Open())
        {
            var saved = await db.Instances.AsNoTracking().SingleAsync(row => row.Id == before.Actor.Id, stop);
            var parkAt = await db.Database.SqlQueryRaw<long>("""SELECT "FoundationParkAt" AS "Value" FROM "Instances" """).SingleAsync(stop);
            Check(DurableKeyJson.Read<string>(saved.Fault!) == fault && parkAt >= before.AsOf, "the fault and Park time survive a cold read");
            Check(await db.Subscriptions.AsNoTracking().Select(row => row.Instance).ToArrayAsync(stop) is ["0/other"],
                "the Park drops the subject's wait and preserves the unrelated wait");
            await db.Messages.Where(row => row.Target == before.Actor.Id).ExecuteUpdateAsync(update => update
                .SetProperty(row => row.KeepUntil, 1).SetProperty(row => row.Due, 0), stop);
            db.Messages.Add(new MessageEntity
            {
                Id = "expired-unrelated", Target = "0/other", Kind = "ordinary", Done = true, KeepUntil = 1
            });
            await db.SaveChangesAsync(stop);
        }
        await store.SweepAsync(stop);
        await using (var db = Open())
            Check(!await db.Messages.AnyAsync(row => row.Id == "expired-unrelated", stop),
                "the same sweep still removes an expired ordinary Done row");
        Check((await ReadInputs(before.Actor.Id)).Select(row => row.Id).SequenceEqual(inputs.Select(row => row.Id)),
            "the sweep preserves the kept payload after its ordinary receipt expires");
        await using (var successor = await Own("successor"))
        {
            await successor.WokenAsync(null, stop);
            await successor.RunAsync(stop);
            Check(successor.Epoch > firstEpoch && probe.Ordinary == 0 &&
                (await ReadInputs(before.Actor.Id)) is [_, { Done: false }],
                "a newer owner leaves the queued input pending while the actor remains parked");
        }
        return 0;

        async Task<Worker> Own(string node)
        {
            var ownerStore = Store();
            var roots = (await ownerStore.BindWorkerPartitionAsync(0, stop)).Scope;
            var worker = new Worker(node, 0, ownerStore, new ActorAdapter(new EfStore.RootHeaders(roots), probe));
            var host = new DurableHost([worker]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return (Worker?)await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture could not take ownership.");
        }

        async Task<EfStore.RootImage> Drive(Worker owner, Func<EfStore.RootImage?, bool> finished)
        {
            while (true)
            {
                var row = await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop);
                if (finished(row)) return row!;
                await owner.WokenAsync(null, stop);
                await owner.RunAsync(stop);
                stop.ThrowIfCancellationRequested();
            }
        }

        async Task<InputImage[]> ReadInputs(string id)
        {
            await using var db = Open();
            return await db.Database.SqlQuery<InputImage>($"""
                SELECT "Id", "Kind", "Done", "Trial", "Payload", "FoundationKeptBy" AS "KeptBy"
                FROM "Messages" WHERE "Target" = {id} AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                ORDER BY "Seq"
                """).ToArrayAsync(stop);
        }
    }

    private sealed record InputImage(string Id, string Kind, bool Done, long Trial, string? Payload, short? KeptBy);

    private sealed class Probe(string original, string fault) : Contract.IActorType
    {
        public int Failed { get; private set; }
        public int Ordinary { get; private set; }
        public string Type => "postgres-park-opening";
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Single().Kind == "fail")
            {
                Failed++;
                turn.State = "null";
                throw new InvalidOperationException(fault);
            }
            if (turn.Inbox[0].Kind == "ordinary") Ordinary++;
            turn.State = original;
            return default;
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Console.WriteLine("ok " + message);
    }
}
