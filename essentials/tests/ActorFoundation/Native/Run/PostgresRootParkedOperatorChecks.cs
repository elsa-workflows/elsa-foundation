using System.Text.Json;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// S5a (#4229): operator commands reach a parked root in a composed set. An unshipped closed set carries Park opening and
// inspection with terminal selection and termination; a second one carries Park inspection with jobs. A Terminate is
// admitted with its receipt pinned, polled past withheld inputs, kept in its FIFO place while the Park opens, delivered
// and refused with a journalled reason that S5b replaces. Shipped formats carry none of these paths.
internal static class PostgresRootParkedOperatorChecks
{
    private static readonly StoreFormat Composed = StoreFormat.Current with { MessageProtocol = 95 };
    private static readonly StoreFormat ComposedJobs = StoreFormat.Current with { MessageProtocol = 94 };
    private const string JobsSchema = "parked_jobs";
    private const string Fault = "deliberate actor failure";

    private sealed record InputRow(long Seq, string Id, string Kind, bool Done, short? KeptBy);

    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var capabilities = RootProfiles.Formats[StoreFormat.RootTerminalSelection] | RootProfiles.Formats[StoreFormat.RootParkInspection];
        KernelDb Open() => KernelDb.ForUnshippedComposition(options, Composed, capabilities);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var stop = timeout.Token;
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the parked-operator fixture uses an empty PostgreSQL database");
        Check(db.UsesRootParkedOperators && !db.UsesRootParkArrivals && db.UsesRootTerminalSelection,
            "the composed set carries Park opening and inspection with terminal selection and termination");
        ShippedFormatsUnchanged();

        var cold = Store();
        await cold.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admission.InstallAsync(stop);
        var tenant = EmbeddedTenant.Bind("parked");
        var reads = new PostgresRootReads(cold, await admission.OpenJournalCursorAsync(stop), 12);
        var ingress = await cold.OpenSelectedRootIngressAsync(Probe.Type, TimeSpan.FromHours(1), (scope, _) => ReferenceEquals(scope, tenant), stop);
        var probe = new Probe();
        async Task<Contract.ActorRef> Start(string id) =>
            (await ingress.SendAsync(tenant, new Contract.Target.Start(new(Probe.Type, "{}")), id, Contract.Kinds.Start, cancellationToken: stop)).Actor!.Value;
        var early = await Start("early");
        var late = await Start("late");
        var lone = await Start("lone");
        probe.FailsOnTerminate.Add(lone.Id);
        await using var owner = await Own();
        var running = new Dictionary<Contract.ActorRef, EfStore.RootImage>();
        foreach (var root in new[] { early, late, lone }) running[root] = await Drive(root, image => image is { Revision: 1, Status: InstanceStatus.Running });
        var selections = new Dictionary<Contract.ActorRef, string>();
        foreach (var root in new[] { early, late, lone }) selections[root] = (await reads.GetAsync(tenant, root, stop))!.Selection!;

        // A Terminate admitted behind the failing input keeps its row and FIFO place while the Park opens, then is refused.
        Check(await Send(early, "fail-early", "fail") && await Terminate(early, "terminate-early"),
            "the failing input and a selected Terminate behind it are admitted while the root runs");
        var stored = await StoredOperator(early);
        await Drive(early, image => image is { Revision: 3, Status: InstanceStatus.Faulted });
        await Settled(early, "terminate-early", running[early]);
        Check(await Inputs(early) is [{ Kind: "fail", Done: true, KeptBy: (short)ParkKeepReason.Consumed } fail,
                { Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null } command] && fail.Seq < command.Seq && !probe.Saw(early, Contract.Kinds.Terminate),
            "the Park kept only the failing input; the Terminate kept its place and never reached the actor");

        // A Terminate admitted after the Park passes a withheld ordinary input and changes nothing else.
        var due = (await cold.ReadFirstRootAsync(tenant.Tenant, late.Id, stop))!.AsOf + 1_500;
        Check(await Send(late, "withheld", "ordinary", due) && await Send(late, "fail-late", "fail"), "a future input and a failing one are admitted");
        var before = await Drive(late, image => image is { Revision: 2, Status: InstanceStatus.Faulted });
        while ((await cold.ReadFirstRootAsync(tenant.Tenant, late.Id, stop))!.AsOf <= due) await Task.Delay(100, stop);
        await ReceiptsPinned();
        Check(await Terminate(late, "terminate-late"), "a Terminate with the root's running selection is admitted for the parked root");
        Check(!await Terminate(early, "terminate-other", selections[late]), "a Terminate with another root's selection is refused at admission");
        var inbox = (await cold.ReadRootInboxAsync(tenant.Tenant, late.Id, 0, "", 10, stop)).Rows;
        Check(inbox is [{ Id: "withheld", KeptBy: null }, { Id: "fail-late", KeptBy: ParkKeepReason.Consumed }, { Id: "terminate-late", KeptBy: null }],
            "inspection lists the withheld input, the kept fault input and the pending Terminate in FIFO order");
        var after = await Drive(late, image => image is { Revision: 3 });
        await Settled(late, "terminate-late", running[late]);
        Check(await Inputs(late) is [{ Id: var withheld, Done: false, KeptBy: null }, { Kind: "fail", KeptBy: (short)ParkKeepReason.Consumed }, { Done: true }] &&
            withheld.StartsWith(late.Id, StringComparison.Ordinal) && !probe.Saw(late, "ordinary") && after.Revision == before.Revision + 1,
            "the refusal moves only the revision: the withheld input stays pending and the kept input stays kept");

        // A Terminate whose own turn fails opens the Park without it; the owner polls it again and refuses it.
        Check(await Terminate(lone, "terminate-lone"), "a Terminate is admitted for the root whose terminate turn fails");
        await Drive(lone, image => image is { Revision: 3, Status: InstanceStatus.Faulted });
        await Settled(lone, "terminate-lone", running[lone]);
        Check(await Inputs(lone) is [{ Kind: Contract.Kinds.Terminate, Done: true, KeptBy: null }] && probe.Saw(lone, Contract.Kinds.Terminate),
            "the failed Terminate opened the Park outside the kept set and settled on the parked root");

        // Format 22 still refuses Terminate at admission and at decode.
        var unreachable = new DbContextOptionsBuilder<KernelDb>().UseNpgsql("Host=parked-refusal.invalid").Options;
        Check(await new PostgresRootAdmission(() => KernelDb.ForPostgresRootParkRecovery(unreachable), TimeSpan.FromHours(1))
                .AdmitRootOperatorAsync(tenant.Tenant, early.Id, "format-22", Contract.Kinds.Terminate, null, null, stop, selections[early]) is null,
            "format 22 refuses a Terminate at admission before it opens a connection");
        try
        {
            PostgresRootAdmission.DecodeOperator(KernelDb.ForPostgresRootParkRecovery(unreachable), stored, early.Id);
            throw new InvalidOperationException("Format 22 decoded a stored Terminate.");
        }
        catch (StoreException error) when (error.Message == "Unexpected stored one-way operator envelope.")
        {
            Check(true, "format 22 refuses a stored Terminate at decode");
        }
        await JobsInspection();
        return 0;

        void ShippedFormatsUnchanged()
        {
            Check(RootProfiles.Formats.Values.All(set => (set & RootCapabilities.ParkOpening) == 0 || (set & RootCapabilities.Termination) == 0),
                "no shipped format carries Park with termination, so formats 19-22 and 5, 11-17 keep their paths");
            foreach (var shipped in new Func<DbContextOptions<KernelDb>, KernelDb>[] { KernelDb.ForPostgresRootParkOpening,
                KernelDb.ForPostgresRootParkInspection, KernelDb.ForPostgresRootParkArrivals, KernelDb.ForPostgresRootParkRecovery,
                KernelDb.ForPostgresRootOperators, KernelDb.ForPostgresRootTerminations, KernelDb.ForPostgresRootOperatorReplies })
                if (shipped(options).UsesRootParkedOperators) throw new InvalidOperationException("A shipped profile refuses parked operators no more.");
            Check(true, "the shipped Park and operator profiles do not use the parked-operator paths");
        }

        // The Park pin covers the operator receipts: a receipts view in the marker schema fails the pin before admission.
        async Task ReceiptsPinned()
        {
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "FoundationOperatorReceipts" RENAME TO fixture_receipts;
                CREATE VIEW "FoundationOperatorReceipts" AS SELECT * FROM fixture_receipts;
                """, stop);
            try
            {
                await admission.AdmitRootOperatorAsync(tenant.Tenant, late.Id, "pinned", Contract.Kinds.Terminate, null, null, stop, selections[late]);
                throw new InvalidOperationException("A Terminate was admitted beside an unpinned receipts relation.");
            }
            catch (StoreFormatException error) when (error.Message == "Park requires the source tables in the marker schema.")
            {
                Check(true, "the Park pin refuses a Terminate whose receipts relation is not a marker-schema table");
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("""
                    DROP VIEW "FoundationOperatorReceipts"; ALTER TABLE fixture_receipts RENAME TO "FoundationOperatorReceipts";
                    """, stop);
            }
        }

        // A composed Park set with jobs inspects its direct inputs, kept and pending, and never lists its job rows.
        async Task JobsInspection()
        {
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA " + JobsSchema, stop);
            var jobsOptions = new DbContextOptionsBuilder<KernelDb>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = JobsSchema }.ConnectionString).Options;
            var jobs = RootProfiles.Formats[StoreFormat.RootParkInspection] | RootCapabilities.JobProtocol;
            KernelDb OpenJobs() => KernelDb.ForUnshippedComposition(jobsOptions, ComposedJobs, jobs);
            await using (var created = OpenJobs()) await created.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(stop);
            var store = new EfStore(OpenJobs, postgres: true);
            await store.CreateAsync(1);
            var jobsAdmission = new PostgresRootAdmission(OpenJobs, TimeSpan.FromHours(1));
            await jobsAdmission.InstallAsync(stop);
            var jobsIngress = new PostgresRootIngress(jobsAdmission, Probe.Type);
            var root = (await jobsIngress.SendAsync(tenant, new Contract.Target.Start(new(Probe.Type, "{}")), "jobs-root", Contract.Kinds.Start,
                cancellationToken: stop)).Actor!.Value;
            await using var seed = OpenJobs();
            var actor = await seed.Database.SqlQuery<string>($"""
                SELECT "Actor" AS "Value" FROM "FoundationRootBirths" WHERE "Tenant" = {tenant.Tenant} AND "Reference" = {root.Id}
                """).SingleAsync(stop);
            seed.Instances.Add(new InstanceEntity { Id = actor, Partition = 0, Model = Probe.Type, Definition = "", Status = InstanceStatus.Running, State = "{}" });
            await seed.SaveChangesAsync(stop);
            var header = DurableKeyJson.Write(new { Model = Probe.Type, Definition = "", TypeData = "{}", Labels = Array.Empty<KeyValuePair<string, string>>(), EscapedState = false });
            Check(await seed.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "FoundationReference" = {root.Id}, "FoundationTenant" = {tenant.Tenant}, "FoundationHeader" = {header},
                    "Revision" = 1, "FoundationWriteRevision" = 1 WHERE "Id" = {actor}
                """, stop) == 1, "the jobs fixture seeds the running root");
            foreach (var id in new[] { "kept", "pending" })
                Check(await jobsIngress.SendAsync(tenant, new Contract.Target.Ref(root), id, "ordinary", "{}", cancellationToken: stop) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, $"the jobs set admits the {id} direct input");
            Check(await seed.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "Status" = {(int)InstanceStatus.Faulted}, "Fault" = {DurableKeyJson.Write(Fault)}, "Revision" = 2,
                    "FoundationWriteRevision" = 2, "FoundationParkAt" = floor(extract(epoch from clock_timestamp()) * 1000)::bigint
                WHERE "FoundationReference" = {root.Id};
                UPDATE "Messages" SET "Done" = true, "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
                WHERE "InboxScope" = -4 AND "Id" LIKE {root.Id + "/%"} AND "Seq" = (SELECT min("Seq") FROM "Messages" WHERE "InboxScope" = -4 AND "Id" LIKE {root.Id + "/%"});
                INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Due", "Seen", "Attempts", "Trial", "Done", "JobGeneration")
                SELECT -6, 'job/' || b."Actor" || '/0', b."Partition", b."Actor", 'kernel.job', 0, 0, 0, 0, false, 1
                FROM "FoundationRootBirths" b WHERE b."Reference" = {root.Id}
                """, stop) >= 3, "the jobs fixture parks the root, keeps its first input and adds a job row");
            Check((await store.ReadRootInboxAsync(tenant.Tenant, root.Id, 0, "", 10, stop)).Rows is
                    [{ Id: "kept", KeptBy: ParkKeepReason.Consumed }, { Id: "pending", KeptBy: null }],
                "inspection of the Park and jobs set lists the kept and the pending input and no job row");
            try
            {
                await new EfStore(() => KernelDb.ForPostgresRootJobs(unreachable), postgres: true).ReadRootInboxAsync(tenant.Tenant, root.Id, 0, "", 10, stop);
                throw new InvalidOperationException("The shipped jobs format inspected its inbox.");
            }
            catch (NotSupportedException)
            {
                Check(true, "the shipped jobs format still refuses inbox inspection");
            }
        }

        async Task<bool> Send(Contract.ActorRef root, string id, string kind, long? dueAt = null) =>
            await ingress.SendAsync(tenant, new Contract.Target.Ref(root), id, kind, "{}", dueAt is null ? null : new(DueAt: dueAt), stop) is
                { Outcome: Contract.AdmissionOutcome.Accepted };

        async Task<bool> Terminate(Contract.ActorRef root, string id, string? selection = null) =>
            await ingress.SendAsync(tenant, new Contract.Target.Ref(root), id, Contract.Kinds.Terminate,
                JsonSerializer.Serialize(new Contract.OperatorCommand(selection ?? selections[root])), cancellationToken: stop) is
                { Outcome: Contract.AdmissionOutcome.Accepted };

        async Task<MessageEntity> StoredOperator(Contract.ActorRef root)
        {
            await using var read = Open();
            return await read.Messages.AsNoTracking().SingleAsync(row => row.InboxScope == -4 && row.Id.StartsWith("operator/" + root.Id + "/"), stop);
        }

        // The command settled at the next revision with a journalled refusal, and the parked checkpoint and its Park stayed put.
        async Task Settled(Contract.ActorRef root, string operation, EfStore.RootImage runningImage)
        {
            var image = (await cold.ReadFirstRootAsync(tenant.Tenant, root.Id, stop))!;
            var journal = await cold.ReadJournalOfAsync(0, image.Actor.Id, 0, stop);
            var listed = (await cold.ReadRootActorAsync(tenant.Tenant, root.Id, stop))!.Value.Item;
            using var entry = journal is [.., _, { Entries: var text }] ? JsonDocument.Parse(text) : null;
            Check(journal is [.., { Entries: """["parked"]""" } park, var last] && last.Revision == park.Revision + 1 && entry?.RootElement is
                { ValueKind: JsonValueKind.Object } refusal && refusal.GetProperty("Outcome").GetString() == "refused" &&
                refusal.GetProperty("Operation").GetString() == operation && refusal.GetProperty("Reason").GetString() == "parked" &&
                image is { Status: InstanceStatus.Faulted, Suspended: false, TerminalRevision: null } && image.State == runningImage.State &&
                image.Revision == last.Revision && listed is { Status: InstanceStatus.Faulted, ParkFault: Fault, ParkAt: > 0 },
                $"{operation} is refused on the parked root at the next revision with a journalled reason, and the Park stays");
        }

        async Task<InputRow[]> Inputs(Contract.ActorRef root)
        {
            await using var read = Open();
            var actor = (await cold.ReadFirstRootAsync(tenant.Tenant, root.Id, stop))!.Actor.Id;
            return await read.Database.SqlQuery<InputRow>($"""
                SELECT "Seq", "Id", "Kind", "Done", "FoundationKeptBy" AS "KeptBy" FROM "Messages"
                WHERE "Target" = {actor} AND "InboxScope" = -4 ORDER BY "Seq"
                """).ToArrayAsync(stop);
        }

        async Task<IScopeActivation> Own()
        {
            var store = Store();
            var scope = (await store.BindWorkerPartitionAsync(0, stop)).Scope;
            var adapter = new ActorAdapter(new EfStore.RootHeaders(scope), probe);
            var host = new DurableHost([new Worker("parked", 0, store, adapter, new WorkerOptions { Partitions = 1 })]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture owner could not take its partition.");
        }

        async Task<EfStore.RootImage> Drive(Contract.ActorRef root, Func<EfStore.RootImage?, bool> done)
        {
            for (var turn = 0; turn < 60; turn++)
            {
                if (await cold.ReadFirstRootAsync(tenant.Tenant, root.Id, stop) is { } image && done(image)) return image;
                await owner.WokenAsync(null, stop);
                await owner.RunAsync(stop);
            }
            throw new InvalidOperationException($"The root {root.Id} did not reach the expected checkpoint.");
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        internal const string Type = "postgres-parked-operators";
        private readonly List<(string Actor, string Kind)> _seen = [];
        public HashSet<string> FailsOnTerminate { get; } = new(StringComparer.Ordinal);
        public bool Saw(Contract.ActorRef actor, string kind) => _seen.Contains((actor.Id, kind));
        string Contract.IActorType.Type => Type;
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            _seen.AddRange(turn.Inbox.Select(input => (turn.Header.Ref.Id, input.Kind)));
            if (turn.Inbox.Any(input => input.Kind == "fail") ||
                FailsOnTerminate.Contains(turn.Header.Ref.Id) && turn.Inbox.Any(input => input.Kind == Contract.Kinds.Terminate))
            {
                turn.State = "null";
                throw new InvalidOperationException(Fault);
            }
            turn.State = "{\"running\":true}";
            if (turn.Inbox.Any(input => input.Kind == Contract.Kinds.Terminate)) turn.End(Contract.ActorStatus.Cancelled);
            return default;
        }
    }

    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        Console.WriteLine("ok " + label);
    }
}
