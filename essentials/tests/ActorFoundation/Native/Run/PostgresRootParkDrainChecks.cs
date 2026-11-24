using System.Reflection;
using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// Protocol 23: a recover of more than one page opens a drain, restores page by page under a durable cursor, and completes.
internal static class PostgresRootParkDrainChecks
{
    private const int Clones = 400;

    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootParkDrain(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var stop = timeout.Token;
        await using (var db = Open())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the drain fixture uses an empty PostgreSQL database");
        var store = Store();
        await store.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admission.InstallAsync(stop);
        var tenant = ContractScopes.Bind("park-drain");
        var probe = new Probe();
        var ingress = new PostgresRootIngress(admission, probe.Type, authorizeRecover: (_, _) => true);
        var actor = (await ingress.SendAsync(tenant, new Contract.Target.Start(new(probe.Type, "{}")), "start", Contract.Kinds.Start,
            cancellationToken: stop)).Actor ?? throw new InvalidOperationException("The creator was refused.");
        var reads = await store.OpenPreviewRootReadsAsync(8, stop);
        ValueTask<Contract.Admission> Send(string id, string kind, string? payload = null) =>
            ingress.SendAsync(tenant, new Contract.Target.Ref(actor), id, kind, payload, cancellationToken: stop);
        ValueTask<Contract.Admission> Recover(string id, string selection) =>
            Send(id, Contract.Kinds.Recover, JsonSerializer.Serialize(new Contract.OperatorCommand(selection)));

        // The consumed occurrences of every landed commit, in commit order, with a marker at Complete.
        var consumed = new List<string>();
        var labels = new[] { "poison", "a1", "a2", "post", "fail-again", "small" }.Concat(Enumerable.Range(1, Clones).Select(n => $"c{n:D3}"))
            .ToDictionary(FoundationRootCreator.OperationDigest, op => op, StringComparer.Ordinal);
        string Label(string id) => labels.GetValueOrDefault(id.Split('/')[^2], id);
        var frozen = false;
        var cursors = new List<(long Seq, string Id, long Restored)>();
        var pageControlsRan = false;
        var postAdmitted = false;
        string instance = "";
        Exception? controlError = null;

        string selection;
        long firstEpoch;
        await using (var owner = await Own("first"))
        {
            instance = (await Drive(owner, row => row is { Revision: 1, Status: InstanceStatus.Running })).Actor.Id;
            firstEpoch = owner.Epoch;
            Check(await Send("poison", "fail", "{}") is { Outcome: Contract.AdmissionOutcome.Accepted }, "the root admits a failing input");
            await Drive(owner, row => row is { Status: InstanceStatus.Faulted });
            Check(await Send("a1", "ordinary", "{}") is { Outcome: Contract.AdmissionOutcome.Accepted }, "the parked root admits an arrival");
            await Drive(owner, _ => KeptCount().Result == 2);
            // 400 more arrivals, copied by raw SQL from the kept one, each under its own operation and occurrence.
            var ops = Enumerable.Range(1, Clones).Select(n => $"c{n:D3}").ToArray();
            var ids = ops.Select(op => $"{actor.Id}/{FoundationRootCreator.OperationDigest(op)}/{Guid.NewGuid():N}").ToArray();
            await using (var db = Open())
                Check(await db.Database.ExecuteSqlInterpolatedAsync($$"""
                    INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Start", "Payload", "Due", "KeepUntil",
                        "Done", "Seen", "Attempts", "Trial", "JobGeneration", "FoundationKeptBy")
                    SELECT m."InboxScope", d.id, m."Partition", m."Target", m."Kind", replace(m."Start", '"a1"', '"' || d.op || '"'),
                        '{"n":' || d.n || '}', m."Due", m."KeepUntil", true, 0, 0, 0, 0, 1
                    FROM "Messages" m, unnest({{ids}}::text[], {{ops}}::text[]) WITH ORDINALITY d(id, op, n)
                    WHERE m."Target" = {{instance}} AND m."FoundationKeptBy" = 1 ORDER BY d.n
                    """, stop) == Clones && await Labelled() == Clones + 1, $"raw SQL keeps {Clones} more arrivals, each under its own operation");
            Check(await Send("a2", "ordinary", "{}") is { Outcome: Contract.AdmissionOutcome.Accepted }, "the parked root admits a last arrival");
            await Drive(owner, _ => KeptCount().Result == Clones + 3);
            selection = (await reads.GetAsync(tenant, actor, stop))?.Selection ?? throw new InvalidOperationException("No Park selection.");
            Check(await Recover("recover", selection) is { Outcome: Contract.AdmissionOutcome.Accepted } &&
                await Recover("recover-again", selection) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "ingress admits two recovers of the Park");
            // The first owner opens the drain and lands one page; after that none of its commits lands, as if it died.
            await Drive(owner, _ => cursors.Count >= 2);
            frozen = true;
        }
        var abandoned = cursors[^1];
        Check(Drain().Result == abandoned && Released().Result.Length == 2 * InMemoryStore.ParkPageLimit,
            "the abandoned owner leaves the open drain's cursor and two pages released");
        Check(await Recover("recover-during", selection) is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
            "ingress refuses a recover while the drain is open");
        await using (var successor = await Own("successor"))
        {
            Check(successor.Epoch > firstEpoch, "a new owner epoch takes the partition over mid-drain");
            await Drive(successor, row => consumed.Contains("post") && row is { Status: InstanceStatus.Running });
        }

        var journal = await store.ReadJournalOfAsync(0, instance, 0, stop);
        var outcomes = journal.Where(row => row.Entries.StartsWith("{", StringComparison.Ordinal)).Select(row => row.Entries).ToArray();
        Check(outcomes is [
                """{"Outcome":"recovering","Operation":"recover","Restored":170,"Cleared":1}""",
                """{"Outcome":"refused","Operation":"recover-again","Reason":"selection"}""",
                """{"Outcome":"recovered","Operation":"recover","Restored":402,"Cleared":1,"AlreadyRearmed":0}"""],
            $"the journal records Open, the second recover's refusal and Complete with the totals ({string.Join(" | ", outcomes)})");
        Check(cursors.Count == 3 && cursors.Zip(cursors.Skip(1)).All(pair => pair.Second.Restored > pair.First.Restored &&
                (pair.Second.Seq > pair.First.Seq || pair.Second.Seq == pair.First.Seq && string.CompareOrdinal(pair.Second.Id, pair.First.Id) > 0)) &&
                cursors[^1].Restored == 402 && await Drain() is null,
            $"the cursor advances monotonically over three pages and closes at Complete ({string.Join(", ", cursors.Select(c => c.Restored))})");
        var released = await Released();
        var order = consumed.Where(id => !id.StartsWith("operator/", StringComparison.Ordinal)).ToArray();
        var complete = consumed.IndexOf("#complete");
        Check(released.Length == Clones + 2 && order.Take(released.Length).SequenceEqual(released) && order.Distinct().Count() == order.Length &&
            complete > 0 && consumed.IndexOf("post") > complete && await KeptCount() == 0,
            "every arrival is restored once and consumed once in Seq order, and the lower-Seq fresh input waits until Complete");
        var seen = probe.Delivered.Distinct().ToArray();
        Check(seen.SkipWhile(id => id != "a1").Take(Clones + 3).SequenceEqual(["a1", .. Enumerable.Range(1, Clones).Select(n => $"c{n:D3}"), "a2", "post"]),
            "the model sees the arrivals in Seq order before the post-recover input");

        // After Complete the next Park of one page recovers in one commit, as protocol 22 does.
        await using (var again = await Own("again"))
        {
            Check(await Send("fail-again", "fail", "{}") is { Outcome: Contract.AdmissionOutcome.Accepted }, "the recovered root admits a failing input");
            await Drive(again, row => row is { Status: InstanceStatus.Faulted });
            Check(await Send("small", "ordinary", "{}") is { Outcome: Contract.AdmissionOutcome.Accepted }, "the re-parked root admits an arrival");
            await Drive(again, _ => KeptCount().Result == 2);
            var second = (await reads.GetAsync(tenant, actor, stop))?.Selection ?? throw new InvalidOperationException("No second selection.");
            Check(await Recover("recover-2", second) is { Outcome: Contract.AdmissionOutcome.Accepted }, "ingress admits the page-sized recover");
            await Drive(again, row => row is { Status: InstanceStatus.Running } && probe.Delivered.Contains("small"));
        }
        Check((await store.ReadJournalOfAsync(0, instance, 0, stop))[^1].Entries ==
                """{"Outcome":"recovered","Operation":"recover-2","Restored":1,"Cleared":1,"AlreadyRearmed":0}""" &&
            await Drain() is null && await Generation() == 2,
            "a page-sized recover after Complete resolves in one commit at generation 2 with no drain");
        return 0;

        async Task ControlsAsync(EfStore ownerStore, CommitBatch batch, CancellationToken token)
        {
            try
            {
                var before = (await Drain(), await Released(), (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, token))!.Revision);
                await Refused(ownerStore, batch with { Epoch = batch.Epoch - 1 }, "a stale epoch refuses the page", token);
                await using (var db = Open())
                {
                    // The page's 31st row is n200; refusing its restore refuses the page.
                    await db.Database.ExecuteSqlRawAsync("""
                        CREATE FUNCTION drain_veto() RETURNS trigger LANGUAGE plpgsql AS $$BEGIN
                            IF NEW."Payload" = '{{"n":200}}' THEN RETURN NULL; END IF; RETURN NEW; END$$;
                        CREATE TRIGGER drain_veto BEFORE UPDATE OF "FoundationReleasedBy" ON "Messages" FOR EACH ROW EXECUTE FUNCTION drain_veto();
                        """, token);
                    try { await Refused(ownerStore, batch, "a trigger that vetoes one restore refuses the page", token); }
                    finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER drain_veto ON \"Messages\"; DROP FUNCTION drain_veto();", CancellationToken.None); }
                }
                // Raw writes against the drain rules of the checkpoint trigger, each rolled back.
                Check(await Probe("\"FoundationParkDrainSeq\" = \"FoundationParkDrainSeq\" + 1, \"FoundationParkDrainRestored\" = \"FoundationParkDrainRestored\" + 1", null),
                    "a raw page write that advances the cursor passes the trigger (rolled back)");
                Check(!await Probe("\"FoundationParkDrainSeq\" = \"FoundationParkDrainSeq\" + 1, \"FoundationParkDrainRestored\" = \"FoundationParkDrainRestored\" + 1", "[\"x\"]"),
                    "the trigger refuses a page write beside a journal entry");
                Check(!await Probe("\"FoundationParkDrainSeq\" = \"FoundationParkDrainSeq\" - 1, \"FoundationParkDrainRestored\" = \"FoundationParkDrainRestored\" + 1", null),
                    "the trigger refuses a cursor that moves back");
                Check(!await Probe("\"FoundationParkDrainSeq\" = \"FoundationParkDrainSeq\" + 1", null),
                    "the trigger refuses a cursor move that restores nothing");
                Check(!await Probe("\"FoundationParkGeneration\" = \"FoundationParkGeneration\" + 1", null),
                    "the trigger refuses a generation change during the drain");
                Check(!await Probe("\"FoundationParkDrainSeq\" = NULL, \"FoundationParkDrainId\" = NULL, \"FoundationParkDrainRestored\" = NULL, \"FoundationParkDrainCleared\" = NULL", null),
                    "the trigger refuses a Complete without its recovered entry");
                Check(!await Probe("\"FoundationParkDrainSeq\" = NULL, \"FoundationParkDrainId\" = NULL, \"FoundationParkDrainRestored\" = NULL, \"FoundationParkDrainCleared\" = NULL",
                        """{"Outcome":"refused","Operation":"probe","Reason":"selection"}"""),
                    "the trigger refuses a drain closed beside a refused entry");
                Check((await Drain(), await Released(), (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, token))!.Revision) is var after &&
                    after.Item1 == before.Item1 && after.Item2.SequenceEqual(before.Item2) && after.Item3 == before.Item3,
                    "the refused page writes keep the cursor, the released rows and the checkpoint");
            }
            catch (Exception error)
            {
                controlError = error;
                throw;
            }
        }

        // One raw journal entry (optional) and one Running-root write at the next revision, always rolled back.
        async Task<bool> Probe(string set, string? entry)
        {
            await using var db = Open();
            await using var transaction = await db.Database.BeginTransactionAsync(stop);
            try
            {
                var revision = (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop))!.Revision + 1;
                if (entry is not null)
                    await db.Database.ExecuteSqlRawAsync("""
                        INSERT INTO "Journal" ("Partition", "Position", "Instance", "Revision", "Entries")
                        SELECT 0, coalesce(max("Position"), 0) + 1, {0}, {1}, {2} FROM "Journal" WHERE "Partition" = 0
                        """, [instance, revision, entry], stop);
                // The column assignments come only from this fixture's constants.
#pragma warning disable EF1003
                await db.Database.ExecuteSqlRawAsync("UPDATE \"Instances\" SET \"Revision\" = \"Revision\" + 1, \"FoundationWriteRevision\" = \"Revision\" + 1, " +
                    set + " WHERE \"Id\" = {0}", [instance], stop);
#pragma warning restore EF1003
                await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE", stop);
                return true;
            }
            catch (Exception error) when ((error as PostgresException ?? error.InnerException as PostgresException)?.SqlState == "23514") { return false; }
            finally { await transaction.RollbackAsync(CancellationToken.None); }
        }

        async ValueTask<Committed> Write(string node, EfStore ownerStore, CommitBatch batch, CancellationToken token)
        {
            if (frozen && node == "first") return new Committed(false, 0, []);
            var recover = batch.Turns.SingleOrDefault(turn => turn.FoundationRootOperator?.Kind == Contract.Kinds.Recover);
            if (!pageControlsRan && recover is { Journal: null })
            {
                pageControlsRan = true;
                await ControlsAsync(ownerStore, batch, token);
            }
            var committed = await ownerStore.CommitAsync(batch, token);
            if (!committed.Landed) return committed;
            if (cursors.Count > 0)
                foreach (var turn in batch.Turns) consumed.AddRange(turn.Done.Select(key => Label(key.Id)));
            if (recover is { Row.Status: InstanceStatus.Running } && recover.FoundationRootOperator!.Id == "recover")
            {
                if (await Drain() is { } drain) cursors.Add(drain);
                else consumed.Add("#complete");
            }
            if (!postAdmitted && cursors.Count == 1)
            {
                // A fresh input admitted after Open is given a lower Seq than every released row.
                postAdmitted = true;
                Check(await Send("post", "ordinary", "{}") is { Outcome: Contract.AdmissionOutcome.Accepted }, "the recovering root admits a fresh input");
                await using var db = Open();
                Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "Seq" = (SELECT min("Seq") - 1 FROM "Messages" WHERE "Target" = {instance})
                    WHERE "Target" = {instance} AND "InboxScope" = -4 AND NOT "Done" AND "FoundationReleasedBy" IS NULL AND "Id" NOT LIKE 'operator/%'
                    """, token) == 1, "raw SQL moves the fresh input ahead of every released row");
            }
            return committed;
        }

        async Task<int> Labelled()
        {
            await using var db = Open();
            return (await db.Database.SqlQuery<string>($"""SELECT "Id" AS "Value" FROM "Messages" WHERE "Target" = {instance} AND "FoundationKeptBy" = 1""")
                .ToArrayAsync(stop)).Count(id => labels.ContainsKey(id.Split('/')[^2]));
        }

        async Task<(long Seq, string Id, long Restored)?> Drain()
        {
            await using var db = Open();
            var row = await db.Database.SqlQuery<DrainRow>($"""
                SELECT "FoundationParkDrainSeq" AS "Seq", "FoundationParkDrainId" AS "Id", "FoundationParkDrainRestored" AS "Restored"
                FROM "Instances" WHERE "Id" = {instance}
                """).SingleAsync(stop);
            return row is { Seq: { } seq, Id: { } id, Restored: { } restored } ? (seq, id, restored) : null;
        }

        async Task<string[]> Released()
        {
            await using var db = Open();
            return [.. (await db.Database.SqlQuery<string>($"""
                SELECT "Id" AS "Value" FROM "Messages" WHERE "Target" = {instance} AND "FoundationReleasedBy" = 1 ORDER BY "Seq", "Id" COLLATE "C"
                """).ToArrayAsync(stop)).Select(Label)];
        }

        async Task<int> KeptCount()
        {
            await using var db = Open();
            return await db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM "Messages" WHERE "Target" = {instance} AND "FoundationKeptBy" IS NOT NULL
                """).SingleAsync(stop);
        }

        async Task<long> Generation()
        {
            await using var db = Open();
            return await db.Database.SqlQuery<long>($"""SELECT "FoundationParkGeneration" AS "Value" FROM "Instances" WHERE "Id" = {instance}""").SingleAsync(stop);
        }

        async Task Refused(EfStore ownerStore, CommitBatch batch, string message, CancellationToken token) =>
            Check(!(await ownerStore.CommitAsync(batch, token)).Landed, message);

        async Task<EfStore.RootImage> Drive(Worker owner, Func<EfStore.RootImage?, bool> finished)
        {
            while (true)
            {
                if (controlError is not null) throw new InvalidOperationException("A drain control failed.", controlError);
                var row = await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop);
                if (finished(row)) return row!;
                await owner.WokenAsync(null, stop);
                await owner.RunAsync(stop);
                stop.ThrowIfCancellationRequested();
            }
        }

        async Task<Worker> Own(string node)
        {
            var ownerStore = Store();
            var roots = (await ownerStore.BindWorkerPartitionAsync(0, stop)).Scope;
            var observed = DispatchProxy.Create<IKernelStore, PostgresRootParkOpeningChecks.ObservedCommit>();
            var proxy = (PostgresRootParkOpeningChecks.ObservedCommit)(object)observed;
            proxy.Store = ownerStore;
            proxy.Write = (batch, token) => Write(node, ownerStore, batch, token);
            var lease = TimeSpan.FromSeconds(75);
            var worker = new Worker(node, 0, observed, new ActorAdapter(new EfStore.RootHeaders(roots), probe),
                new() { Lease = lease, Horizon = TimeSpan.FromSeconds(30) });
            var host = new DurableHost([worker]);
            await host.HeartbeatAsync(lease, stop);
            return (Worker?)await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture could not take ownership.");
        }
    }

    private sealed record DrainRow(long? Seq, string? Id, long? Restored);

    private sealed class Probe : Contract.IActorType
    {
        public List<string> Delivered { get; } = [];
        public string Type => "postgres-park-drain";
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            Delivered.AddRange(turn.Inbox.Select(input => input.Id));
            if (turn.Inbox.Any(input => input.Kind == "fail")) throw new InvalidOperationException("deliberate actor failure");
            turn.State = "{}";
            return default;
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Console.WriteLine("ok " + message);
    }
}
