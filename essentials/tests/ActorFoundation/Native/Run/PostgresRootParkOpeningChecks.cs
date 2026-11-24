using System.Reflection;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        EfStore Store(Func<KernelDb>? open = null) => new(open ?? Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stop = timeout.Token;
        await using (var db = Open())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the Park fixture uses an empty PostgreSQL database");
        var store = Store();
        await store.CreateAsync(1);
        await new PostgresRootAdmission(Open, TimeSpan.FromHours(1)).InstallAsync(stop);
        string source;
        await using (var db = Open())
        {
            source = await db.Database.SqlQueryRaw<string>("""SELECT current_schema() AS "Value" """).SingleAsync(stop);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE SCHEMA park_shadow;
                CREATE TABLE park_shadow."Subscriptions" (LIKE "Subscriptions" INCLUDING ALL);
                CREATE TABLE park_shadow."Partitions" (LIKE "Partitions" INCLUDING ALL);
                CREATE TABLE park_shadow."FoundationDirectReceipts" (LIKE "FoundationDirectReceipts" INCLUDING ALL);
                CREATE TABLE park_shadow."FoundationRootReceipts" (LIKE "FoundationRootReceipts" INCLUDING ALL);
                """, stop);
        }
        using var quoting = new NpgsqlCommandBuilder();
        var mixed = new NpgsqlConnectionStringBuilder(connection) { SearchPath = "park_shadow," + quoting.QuoteIdentifier(source) };
        var mixedOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(mixed.ConnectionString).Options;
        KernelDb OpenMixed() => KernelDb.ForPostgresRootParkOpening(mixedOptions);
        var admission = new PostgresRootAdmission(OpenMixed, TimeSpan.FromHours(1), lockOrdinaryWrites: true);
        var tenant = ContractScopes.Bind("park-preview");
        var probe = new Probe(original, fault);
        var ingress = new PostgresRootIngress(admission, probe.Type);
        var accepted = await ingress.SendAsync(tenant, new Contract.Target.Start(new(probe.Type, "{}")),
            "start", Contract.Kinds.Start, cancellationToken: stop);
        var actor = accepted.Actor ?? throw new InvalidOperationException("The creator was refused.");
        var sibling = (await ingress.SendAsync(tenant, new Contract.Target.Start(new(probe.Type, "{}")),
            "sibling", Contract.Kinds.Start, cancellationToken: stop)).Actor
            ?? throw new InvalidOperationException("The sibling creator was refused.");
        var observedTail = false;
        var attemptedTail = false;
        string? tailOccurrence = null;
        var controlsRan = false;
        Exception? controlError = null;
        long tailDue = 0;
        EfStore.RootImage before;
        long firstEpoch;
        await using (var owner = await Own("first"))
        {
            before = await Drive(owner, row => row is { Revision: 1, Status: InstanceStatus.Running });
            firstEpoch = owner.Epoch;
            await Drive(owner, row => row is { Revision: 1 }, sibling);
            Check(before.State == original, "the real actor committed its initial checkpoint");
            await using (var db = Open())
            {
                db.Subscriptions.AddRange(
                    new SubscriptionEntity { Key = "signal", Instance = before.Actor.Id, Wait = "w", Partition = 0 },
                    new SubscriptionEntity { Key = "signal", Instance = "0/other", Wait = "w", Partition = 0 });
                await db.SaveChangesAsync(stop);
            }
            tailDue = (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop))!.AsOf + 8_000;
            Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "later", "ordinary", "{}",
                new(DueAt: tailDue), stop) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "the ordinary ingress admits the earlier-sequence future input");
            Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "poison", "fail", payload,
                cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Accepted }, "the ordinary ingress admits the ready failing input");
            var parked = await Drive(owner, row => row is { Revision: 2, Status: InstanceStatus.Faulted });
            Check(parked.State == before.State && parked.TypeData == before.TypeData && parked.StateVersion == before.StateVersion &&
                probe.Failed == 1, "the failed turn records Park and preserves the committed actor state and header");
            Check(observedTail && controlsRan && parked.AsOf >= tailDue,
                "the already buffered input matures before the failing turn's Park commits");
            Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(sibling), "side-turn", "ordinary", "{}",
                cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Accepted }, "the parked root has a ready neighbor");
            await Drive(owner, row => row is { Revision: 2 }, sibling);
            Check(owner.Epoch == firstEpoch && !attemptedTail && probe.Ordinary == 1,
                "the same owner withholds the buffered input without attempting to settle it, and advances the neighbor");
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
        Check(inputs is [{ Kind: "ordinary", Done: false, KeptBy: null },
            { Kind: "fail", Done: true, Trial: 0, Payload: payload, KeptBy: (short)ParkKeepReason.Consumed }], "only the failing input becomes a kept occurrence and retains its payload");
        await using (var db = Open())
        {
            var saved = await db.Instances.AsNoTracking().SingleAsync(row => row.Id == before.Actor.Id, stop);
            var parkAt = await db.Database.SqlQuery<long>($"""
                SELECT "FoundationParkAt" AS "Value" FROM "Instances" WHERE "Id" = {before.Actor.Id}
                """).SingleAsync(stop);
            Check(DurableKeyJson.Read<string>(saved.Fault!) == fault && parkAt >= before.AsOf, "the fault and Park time survive a cold read");
            Check(await db.Subscriptions.AsNoTracking().Select(row => row.Instance).ToArrayAsync(stop) is ["0/other"],
                "the Park drops the subject's wait and preserves the unrelated wait");
            await db.Messages.Where(row => row.Target == before.Actor.Id && row.Done)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.KeepUntil, 1), stop);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "FoundationDirectReceipts" SET "Until" = 1
                WHERE "Reference" = {actor.Id} AND "Operation" = {FoundationRootCreator.OperationDigest("poison")};
                UPDATE "FoundationRootReceipts" SET "Until" = 1 WHERE "Reference" = {actor.Id};
                INSERT INTO park_shadow."FoundationDirectReceipts" SELECT * FROM "FoundationDirectReceipts" WHERE "Until" = 1;
                INSERT INTO park_shadow."FoundationRootReceipts" SELECT * FROM "FoundationRootReceipts" WHERE "Until" = 1;
                """, stop);
            db.Messages.Add(new MessageEntity
            {
                Id = "expired-unrelated", Target = "0/other", Kind = "ordinary", Done = true, KeepUntil = 1
            });
            await db.SaveChangesAsync(stop);
        }
        var expectedAfterSweep = await ReadInputs(before.Actor.Id);
        Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "poison", "fail", payload,
            cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Refused },
            "retry after the actual direct admission receipt expires admits no new occurrence");
        Check(await admission.SweepDirectReceiptsAsync(stop) == 1 && await admission.SweepReceiptsAsync(stop) == 1,
            "both sweepers remove the expired canonical receipts");
        await using (var db = Open())
            Check(await db.Database.SqlQueryRaw<bool>("""
                SELECT NOT EXISTS (SELECT 1 FROM "FoundationDirectReceipts" WHERE "Until" = 1)
                    AND NOT EXISTS (SELECT 1 FROM "FoundationRootReceipts" WHERE "Until" = 1)
                    AND EXISTS (SELECT 1 FROM park_shadow."FoundationDirectReceipts" WHERE "Until" = 1)
                    AND EXISTS (SELECT 1 FROM park_shadow."FoundationRootReceipts" WHERE "Until" = 1) AS "Value"
                """).SingleAsync(stop), "receipt sweeping leaves the shadow schema untouched");
        await Store(OpenMixed).SweepAsync(stop);
        await using (var db = Open())
            Check(!await db.Messages.AnyAsync(row => row.Id == "expired-unrelated", stop),
                "the same sweep still removes an expired ordinary Done row");
        Check((await ReadInputs(before.Actor.Id)).SequenceEqual(expectedAfterSweep),
            "the sweep preserves the kept payload after its ordinary receipt expires");
        await using (var successor = await Own("successor"))
        {
            await successor.WokenAsync(null, stop);
            await successor.RunAsync(stop);
            Check(successor.Epoch > firstEpoch && probe.Ordinary == 1 &&
                (await ReadInputs(before.Actor.Id)) is [{ Done: false }, _],
                "a newer owner leaves the queued input pending while the actor remains parked");
        }
        return 0;

        async Task<Worker> Own(string node)
        {
            var ownerStore = Store(OpenMixed);
            var roots = (await ownerStore.BindWorkerPartitionAsync(0, stop)).Scope;
            var observed = DispatchProxy.Create<IKernelStore, ObservedCommit>();
            var proxy = (ObservedCommit)(object)observed;
            proxy.Store = ownerStore;
            proxy.Write = (batch, token) => Write(ownerStore, batch, token);
            var lease = TimeSpan.FromSeconds(75);
            var worker = new Worker(node, 0, observed, new ActorAdapter(new EfStore.RootHeaders(roots), probe), new()
            {
                Lease = lease,
                Horizon = TimeSpan.FromSeconds(30),
                OnObservedDelivery = row => observedTail |= row.Message.Id == "later"
            });
            var host = new DurableHost([worker]);
            await host.HeartbeatAsync(lease, stop);
            return (Worker?)await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture could not take ownership.");
        }

        async ValueTask<Committed> Write(EfStore ownerStore, CommitBatch batch, CancellationToken token)
        {
            attemptedTail |= batch.Turns.Any(turn => turn.Row is null && turn.Done.Any(key => key.Id == tailOccurrence));
            if (!controlsRan && batch.Turns.SingleOrDefault(turn => turn.OpensPark) is { } park)
            {
                controlsRan = true;
                try
                {
                    var asOf = (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, token))!.AsOf;
                    Check(asOf < tailDue && observedTail, "the ready input fails while the earlier buffered input is still future");
                    do
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, tailDue - asOf)), token);
                        asOf = (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, token))!.AsOf;
                    }
                    while (asOf < tailDue);

                    await using var db = Open();
                    var physical = await ReadInputs(park.Instance);
                    tailOccurrence = physical.Single(row => row.Kind == "ordinary").Id;
                    var failed = physical.Single(row => row.Kind == "fail");
                    var journalBeforePark = await store.ReadJournalOfAsync(0, park.Instance, 0, stop);
                    var selected = db.Messages.Where(row => row.Id == failed.Id);
                    await selected.ExecuteUpdateAsync(update => update.SetProperty(row => row.Payload, "changed"), token);
                    try { await Refused(ownerStore, batch, "the same-key changed envelope rolls Park back", token); }
                    finally
                    {
                        await selected.ExecuteUpdateAsync(update => update.SetProperty(row => row.Payload, failed.Payload), token);
                    }
                    await db.Database.ExecuteSqlRawAsync("""
                        CREATE FUNCTION park_hold_wait() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NULL; END';
                        CREATE TRIGGER park_hold_wait BEFORE DELETE ON "Subscriptions"
                          FOR EACH ROW EXECUTE FUNCTION park_hold_wait();
                        """, token);
                    try { await Refused(ownerStore, batch, "a silent subscription deletion veto rolls Park back", token); }
                    finally
                    {
                        await db.Database.ExecuteSqlRawAsync("""
                            DROP TRIGGER park_hold_wait ON "Subscriptions"; DROP FUNCTION park_hold_wait();
                            """, token);
                    }
                    await db.Database.ExecuteSqlRawAsync("""
                        CREATE FUNCTION park_change_kept() RETURNS trigger LANGUAGE plpgsql
                            AS $$BEGIN NEW."Payload" := 'changed by the keep write'; RETURN NEW; END$$;
                        CREATE TRIGGER park_change_kept BEFORE UPDATE OF "FoundationKeptBy" ON "Messages"
                            FOR EACH ROW EXECUTE FUNCTION park_change_kept();
                        """, token);
                    try { await Refused(ownerStore, batch, "a keep write that rewrites the captured envelope rolls Park back", token); }
                    finally
                    {
                        await db.Database.ExecuteSqlRawAsync("""
                            DROP TRIGGER park_change_kept ON "Messages"; DROP FUNCTION park_change_kept();
                            """, token);
                    }
                    Check((await ReadInputs(park.Instance)).SequenceEqual(physical) &&
                        (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, token)) is { Revision: 1, Status: InstanceStatus.Running } &&
                        (await store.ReadJournalOfAsync(0, park.Instance, 0, token)).SequenceEqual(journalBeforePark) &&
                        await db.Subscriptions.AnyAsync(row => row.Instance == park.Instance, token),
                        "the refused writes keep the original checkpoint, journal, subscriptions and occurrence");
                }
                catch (Exception error)
                {
                    controlError = error;
                    throw;
                }
            }
            return await ownerStore.CommitAsync(batch, token);
        }

        async Task Refused(EfStore ownerStore, CommitBatch batch, string message, CancellationToken token) =>
            Check(!(await ownerStore.CommitAsync(batch, token)).Landed, message);

        async Task<EfStore.RootImage> Drive(Worker owner, Func<EfStore.RootImage?, bool> finished, Contract.ActorRef? target = null)
        {
            while (true)
            {
                if (controlError is not null) throw new InvalidOperationException("Park rollback control failed.", controlError);
                var row = await store.ReadFirstRootAsync(tenant.Tenant, (target ?? actor).Id, stop);
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
                SELECT "Id", "Kind", "Done", "Trial", "Payload", "Start" AS "Header", "KeepUntil", "Due", "FoundationKeptBy" AS "KeptBy"
                FROM "Messages" WHERE "Target" = {id} AND "InboxScope" = {InboxKey.FoundationOccurrenceScope}
                ORDER BY "Seq"
                """).ToArrayAsync(stop);
        }
    }

    private sealed record InputImage(string Id, string Kind, bool Done, long Trial, string? Payload,
        string? Header, long? KeepUntil, long Due, short? KeptBy);

    public class ObservedCommit : DispatchProxy
    {
        internal IKernelStore Store = null!;
        internal Func<CommitBatch, CancellationToken, ValueTask<Committed>> Write = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == nameof(IKernelStore.CommitAsync)
                ? Write((CommitBatch)args![0]!, (CancellationToken)args[1]!)
                : method.Invoke(Store, args);
    }

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
