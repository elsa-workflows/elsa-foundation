using System.Reflection;
using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresRootParkOpeningChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch, bool inspection = false, bool arrivals = false, bool recovery = false)
    {
        if (recovery) (inspection, arrivals) = (true, true);
        const string fault = "deliberate actor failure";
        const string original = "{\"before\":true}";
        const string payload = "{\"retain\":\"this input\"}";
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Profile(DbContextOptions<KernelDb> settings) => recovery ? KernelDb.ForPostgresRootParkRecovery(settings)
            : arrivals ? KernelDb.ForPostgresRootParkArrivals(settings)
            : inspection ? KernelDb.ForPostgresRootParkInspection(settings) : KernelDb.ForPostgresRootParkOpening(settings);
        KernelDb Open() => Profile(options);
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
                CREATE FUNCTION park_shadow.kernel_actor_list_filter(text) RETURNS jsonb
                    LANGUAGE sql IMMUTABLE AS $$ SELECT 'null'::jsonb $$;
                """, stop);
        }
        using var quoting = new NpgsqlCommandBuilder();
        var mixed = new NpgsqlConnectionStringBuilder(connection) { SearchPath = "park_shadow," + quoting.QuoteIdentifier(source) };
        var mixedOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(mixed.ConnectionString).Options;
        KernelDb OpenMixed() => Profile(mixedOptions);
        var admission = new PostgresRootAdmission(OpenMixed, TimeSpan.FromHours(1), lockOrdinaryWrites: true);
        var tenant = ContractScopes.Bind("park-preview");
        var probe = new Probe(original, fault);
        var ingress = new PostgresRootIngress(admission, probe.Type, authorizeRecover: recovery ? (_, _) => true : null);
        var accepted = await ingress.SendAsync(tenant, new Contract.Target.Start(new(probe.Type, "{}")),
            "start", Contract.Kinds.Start, cancellationToken: stop);
        var actor = accepted.Actor ?? throw new InvalidOperationException("The creator was refused.");
        var sibling = (await ingress.SendAsync(tenant, new Contract.Target.Start(new(probe.Type, "{}")),
            "sibling", Contract.Kinds.Start, cancellationToken: stop)).Actor
            ?? throw new InvalidOperationException("The sibling creator was refused.");
        var observedTail = false;
        var attemptedTail = false;
        var keptTail = false;
        string? tailOccurrence = null;
        var controlsRan = false;
        var arrivalControlsRan = false;
        var reparkControlsRan = false;
        var recoverControlsRan = false;
        var secondRecoverLanded = false;
        int? recoveredAt = null;
        ParkMark? opened = null;
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
            var parked = await Drive(owner, row => row is { Revision: >= 2, Status: InstanceStatus.Faulted } &&
                (arrivals || row.Revision == 2));
            Check(parked.State == before.State && parked.TypeData == before.TypeData && parked.StateVersion == before.StateVersion &&
                probe.Failed == 1, "the failed turn records Park and preserves the committed actor state and header");
            Check(observedTail && controlsRan && parked.AsOf >= tailDue,
                "the already buffered input matures before the failing turn's Park commits");
            Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(sibling), "side-turn", "ordinary", "{}",
                cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Accepted }, "the parked root has a ready neighbor");
            await Drive(owner, row => row is { Revision: 2 }, sibling);
            if (arrivals)
            {
                await Drive(owner, row => row is { Revision: 3, Status: InstanceStatus.Faulted });
                var tail = (await ReadInputs(before.Actor.Id)).Single(row => row.Id == tailOccurrence);
                Check(owner.Epoch == firstEpoch && keptTail && !attemptedTail && probe.Ordinary == 1 && probe.TurnsOf(actor) == 2 &&
                    tail is { Done: true, Trial: 0, KeptBy: (short)ParkKeepReason.Arrived } && arrivalControlsRan,
                    "the same owner keeps the buffered input as arrived without calling the actor, and advances the neighbor");
            }
            else
                Check(owner.Epoch == firstEpoch && !attemptedTail && probe.Ordinary == 1,
                    "the same owner withholds the buffered input without attempting to settle it, and advances the neighbor");
        }

        if (recovery)
        {
            await RecoverAsync();
            return 0;
        }
        if (arrivals)
        {
            await ObserveArrivalsAsync();
            await CrossProtocolAsync();
            return 0;
        }

        Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "poison", "fail", payload,
            cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Duplicate },
            "the already admitted delivery still returns its receipt after Park");
        Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "new", "ordinary", "{}",
            cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Refused },
            "the opening profile refuses fresh arrivals until it can retain them");
        await using (var prior = inspection ? KernelDb.ForPostgresRootParkOpening(options) : new KernelDb(options))
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
        if (inspection) await InspectParkAsync();
        return 0;

        async Task ObserveArrivalsAsync()
        {
            var reads = await Store(OpenMixed).OpenPreviewRootReadsAsync(8, stop);
            var parked = await reads.GetAsync(tenant, actor, stop);
            Check(parked is { Park.At: > 0, Header.Fault: fault }, "the earlier Park has a fixed projected mark");
            Check(opened is { At: > 0, Revision: 2 } && await ReadParkMark(before.Actor.Id) == opened,
                "the Park baseline captured right after the opening commit holds before the first fresh arrival");
            await using (var holder = Open())
            {
                // The Park writer locks the root's checkpoint row before it keeps any input.
                await using var hold = await holder.Database.BeginTransactionAsync(stop);
                await holder.Database.SqlQuery<string>($"""
                    SELECT "Id" AS "Value" FROM "Instances" WHERE "Id" = {before.Actor.Id} FOR UPDATE
                    """).ToListAsync(stop);
                var racing = ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "fresh", "ordinary", payload,
                    cancellationToken: stop).AsTask();
                Check(await Task.WhenAny(racing, Task.Delay(TimeSpan.FromMilliseconds(500), stop)) != racing,
                    "a public admission does not complete while another transaction holds the root row for update");
                await using (var monitor = Open())
                    Check(await monitor.Database.SqlQueryRaw<bool>("""
                        SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_stat_activity
                            WHERE datname = current_database() AND pid <> pg_backend_pid() AND wait_event_type = 'Lock'
                                AND query LIKE '%FROM "Instances"%' AND query LIKE '%FOR SHARE%') AS "Value"
                        """).SingleAsync(stop), "the waiting admission is blocked on its share lock of the root row");
                await hold.RollbackAsync(stop);
                Check(await racing is { Outcome: Contract.AdmissionOutcome.Accepted },
                    "public ingress accepts new input for the parked generation after the lock holder rolls back");
            }
            Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "fresh", "ordinary", payload,
                cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Duplicate },
                "repeating the operation does not create a second arrival");

            await using (var successor = await Own("arrival-successor"))
            {
                var after = await Drive(successor, row => row is { Revision: 4, Status: InstanceStatus.Faulted });
                Check(successor.Epoch > firstEpoch && after.State == before.State && after.Fault == fault && probe.Ordinary == 1,
                    "the successor captures the arrivals without calling the actor and retains its state");
            }
            var view = await reads.GetAsync(tenant, actor, stop);
            var inbox = await reads.InboxAsync(tenant, actor, cancellationToken: stop);
            Check(view?.Park == parked!.Park && view!.Header.Fault == fault, "arrivals preserve the original projected Park timestamp");
            Check(inbox.Items is [{ Id: "later", State: Contract.DeliveryState.Arrived },
                { Id: "poison", State: Contract.DeliveryState.Consumed },
                { Id: "fresh", State: Contract.DeliveryState.Arrived }],
                "public inspection distinguishes arrival and fault-causing input (" +
                string.Join(", ", inbox.Items.Select(item => item.Id + "/" + item.State)) + ")");
            var history = await store.ReadJournalOfAsync(0, before.Actor.Id, 0, stop);
            Check(history is [_, { Revision: 2, Entries: "[\"parked\"]" }], "arrivals do not replace or append to the opening journal");
            await using var db = Open();
            var revision = await db.Database.SqlQuery<long>($"""
                SELECT "FoundationParkRevision" AS "Value" FROM "Instances" WHERE "Id" = {before.Actor.Id}
                """).SingleAsync(stop);
            Check(revision == 2, "the opening revision remains two after the two retained arrivals");
            var retained = await ReadInputs(before.Actor.Id);
            Check(retained is [{ Done: true, KeptBy: (short)ParkKeepReason.Arrived },
                { Done: true, Payload: payload, KeptBy: (short)ParkKeepReason.Consumed },
                { Done: true, Payload: payload, KeptBy: (short)ParkKeepReason.Arrived }],
                "all three physical occurrences retain their envelope and exact kept reason");

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "FoundationDirectReceipts" SET "Until" = 1
                WHERE "Reference" = {actor.Id} AND "Operation" = {FoundationRootCreator.OperationDigest("poison")}
                """, stop);
            Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "poison", "ordinary", "{}",
                cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "an expired operation id admits a new occurrence while the original stays kept");
            await using (var third = await Own("reused-operation"))
                await Drive(third, row => row is { Revision: 5, Status: InstanceStatus.Faulted });

            var repeated = (await reads.InboxAsync(tenant, actor, cancellationToken: stop)).Items.Where(item => item.Id == "poison").ToArray();
            Check(repeated is [{ State: Contract.DeliveryState.Consumed }, { State: Contract.DeliveryState.Arrived }] &&
                repeated[0].Handle != repeated[1].Handle && (await reads.GetAsync(tenant, actor, stop))?.Park == parked.Park,
                "the same outside id has two distinct kept handles and one unchanged Park");

            Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "exhausted", "ordinary", "{}",
                cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Accepted }, "the exhausted-attempt control has a direct occurrence");
            var exhausted = (await new PostgresRootAdmission(Open, TimeSpan.Zero)
                .ReadPendingDirectAsync(tenant.Tenant, actor.Id, stop)).Single();
            await db.Messages.Where(row => row.Id == exhausted.InboxKey.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Attempts, new WorkerOptions().MaxAttempts), stop);
            await using (var fourth = await Own("exhausted-attempt"))
                await Drive(fourth, row => row is { Revision: 6, Status: InstanceStatus.Faulted });

            var reparked = (await reads.InboxAsync(tenant, actor, cancellationToken: stop)).Items.Single(item => item.Id == "exhausted");
            Check(reparked.State == Contract.DeliveryState.Consumed && (await reads.GetAsync(tenant, actor, stop))?.Park == parked.Park &&
                (await store.ReadJournalOfAsync(0, before.Actor.Id, 0, stop)) is [_, { Revision: 2 }, { Revision: 6, Entries: "[\"parked\"]" }],
                "an exhausted occurrence continues the same Park with the consumed reason and its own journal fact");
            Check(reparkControlsRan, "the exhausted re-park ran its refusal controls before it landed");

            // A taken batch with one exhausted occurrence re-parks as a whole, as the reference poison path does.
            Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "mixed-exhausted", "ordinary", "{}",
                    cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Accepted } &&
                await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "mixed-fresh", "ordinary", payload,
                    cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "the mixed batch has an exhausted occurrence and a fresh arrival");
            var mixed = await new PostgresRootAdmission(Open, TimeSpan.Zero).ReadPendingDirectAsync(tenant.Tenant, actor.Id, stop);
            Check(mixed.Count == 2, "both mixed inputs are pending before the owner takes them");
            await db.Messages.Where(row => row.Id == mixed[0].InboxKey.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Attempts, new WorkerOptions().MaxAttempts), stop);
            await using (var fifth = await Own("mixed-batch"))
                await Drive(fifth, row => row is { Revision: >= 7, Status: InstanceStatus.Faulted });
            var mixedKept = (await reads.InboxAsync(tenant, actor, cancellationToken: stop)).Items
                .Where(item => item.Id is "mixed-exhausted" or "mixed-fresh").ToArray();
            var mixedRows = (await ReadInputs(before.Actor.Id)).Where(row => mixed.Any(input => input.InboxKey.Id == row.Id)).ToArray();
            Check((await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop)) is { Revision: 7, State: original, Fault: fault } &&
                mixedKept is [{ Id: "mixed-exhausted", State: Contract.DeliveryState.Consumed },
                    { Id: "mixed-fresh", State: Contract.DeliveryState.Consumed }] &&
                mixedRows is [{ Done: true, KeptBy: (short)ParkKeepReason.Consumed },
                    { Done: true, Payload: payload, KeptBy: (short)ParkKeepReason.Consumed }] &&
                (await store.ReadJournalOfAsync(0, before.Actor.Id, 0, stop)) is
                    [_, { Revision: 2 }, { Revision: 6 }, { Revision: 7, Entries: "[\"parked\"]" }],
                "a batch with an exhausted occurrence keeps every taken input as consumed and adds one Park fact");
            Check((await reads.GetAsync(tenant, actor, stop))?.Park == parked.Park && await ReadParkMark(before.Actor.Id) == opened,
                "the final re-park keeps the opening Park time, revision and fault");
        }

        async Task CrossProtocolAsync()
        {
            await using (var older = KernelDb.ForPostgresRootParkInspection(options))
                await FormatRefused(() => EfStore.RequireStoreVersionsAsync(older, stop), "a protocol 20 context refuses the protocol 21 store");
            await FormatRefused(() => new EfStore(() => KernelDb.ForPostgresRootParkInspection(options), postgres: true)
                .BindWorkerPartitionAsync(0, stop), "a protocol 20 owner refuses to bind the protocol 21 store");

            // A fresh protocol 20 store in its own schema of the same empty database.
            await using (var db = Open())
                await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA park_protocol20", stop);
            var prior = new NpgsqlConnectionStringBuilder(connection) { SearchPath = "park_protocol20" };
            var priorOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(prior.ConnectionString).Options;
            await using (var db = KernelDb.ForPostgresRootParkInspection(priorOptions))
                await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(stop);
            var older20 = new EfStore(() => KernelDb.ForPostgresRootParkInspection(priorOptions), postgres: true);
            await older20.CreateAsync(1);
            await new PostgresRootAdmission(() => KernelDb.ForPostgresRootParkInspection(priorOptions), TimeSpan.FromHours(1)).InstallAsync(stop);
            Check(await older20.PartitionCountAsync(stop) == 1, "a fresh protocol 20 store exists in its own schema");
            await using (var newer = KernelDb.ForPostgresRootParkArrivals(priorOptions))
                await FormatRefused(() => EfStore.RequireStoreVersionsAsync(newer, stop), "a protocol 21 context refuses the protocol 20 store");
            await FormatRefused(() => new EfStore(() => KernelDb.ForPostgresRootParkArrivals(priorOptions), postgres: true)
                .BindWorkerPartitionAsync(0, stop), "a protocol 21 owner refuses to bind the protocol 20 store");
        }

        static async Task FormatRefused(Func<Task> attach, string message)
        {
            try
            {
                await attach();
                throw new InvalidOperationException("The store format was accepted: " + message);
            }
            catch (StoreFormatException error) { Console.WriteLine($"ok {message} ({error.Message})"); }
        }


        async Task InspectParkAsync()
        {
            var reads = await Store(OpenMixed).OpenPreviewRootReadsAsync(1, stop);
            var foreign = ContractScopes.Bind("park-foreign");
            await using var db = Open();
            var previous = await reads.GetAsync(tenant, actor, stop);
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "FoundationActorList" DROP COLUMN "ParkAt", DROP COLUMN "ParkFault"
                """, stop);
            await new PostgresRootAdmission(Open, TimeSpan.FromHours(1)).InstallAsync(stop);
            var rebuilt = await reads.GetAsync(tenant, actor, stop);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Kind", "Seq", "Partition", "Due", "Seen",
                    "Attempts", "Trial", "Done", "JobGeneration")
                SELECT 'park-sibling-' || n, -4, '0/index-sibling', 'ordinary', 100000 + n, 0, 0, 0, 0, 0, false, 0
                FROM generate_series(1, 500) n;
                ANALYZE "Messages";
                """, stop);
            await using (var plan = await db.Database.BeginTransactionAsync(stop))
            {
                await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off", stop);
                var lines = await db.Database.SqlQuery<string>($"""
                    EXPLAIN SELECT "Id" FROM "Messages"
                    WHERE "Partition" = 0 AND "Target" COLLATE "C" = {before.Actor.Id}
                        AND "InboxScope" = -4 AND "EventMatch" IS NULL AND (NOT "Done" OR "FoundationKeptBy" = 0)
                        AND ("Seq", "Id" COLLATE "C") > (0, '')
                    ORDER BY "Seq", "Id" COLLATE "C" LIMIT 2
                    """).ToArrayAsync(stop);
                Check(lines.Any(line => line.Contains("IX_FoundationRootInbox_Inspectable", StringComparison.Ordinal)),
                    "the scoped inbox seek can use the partial index with a same-partition backlog");
                await plan.RollbackAsync(stop);
            }
            Check(previous is { Park.At: > 0 } && rebuilt?.Park == previous.Park && rebuilt.Header.Fault == fault,
                "reinstalling the projection backfills the original Park mark and fault");
            try
            {
                await db.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE "Instances" RENAME COLUMN "State" TO park_hidden_state;
                    ALTER TABLE "Instances" RENAME COLUMN "FoundationHeader" TO park_hidden_header;
                    """, stop);
                try
                {
                    await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop);
                    throw new InvalidOperationException("The owner checkpoint read hydrated the hidden columns.");
                }
                catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UndefinedColumn)
                {
                    Console.WriteLine("ok the real owner checkpoint reader cannot hydrate under the same schema window");
                }
                var point = await reads.GetAsync(tenant, actor, stop);
                Check(point is { Park.At: > 0, Header.Status: Contract.ActorStatus.Faulted, Header.Fault: fault, Selection: null } &&
                    point.Header.Ref == actor && point.Header.EndedAt is null,
                    "the public point read reports Park and the recorded fault while the owner columns are unavailable");
                var parked = await reads.ListAsync(tenant, new(Type: probe.Type, Labels: point!.Header.Labels, Parked: true),
                    cancellationToken: stop);
                var live = await reads.ListAsync(tenant, new(Parked: false), cancellationToken: stop);
                Check(parked.Items is [var result] && result.Park == point!.Park && result.Header.Ref == actor && parked.Next is null &&
                    live.Items is [{ Park: null } one] && one.Header.Ref == sibling && live.Next is null,
                    "parked type and label filters use the projection even with a shadow filter helper");

                var first = await reads.InboxAsync(tenant, actor, cancellationToken: stop);
                Check(first.Items is [{ Id: "later", State: Contract.DeliveryState.Pending }] && first.Next is not null,
                    "the parked inbox includes the earlier pending input before the kept input");
                var fresh = await Store(OpenMixed).OpenPreviewRootReadsAsync(1, stop);
                var second = await fresh.InboxAsync(tenant, actor, first.Next, stop);
                Check(second.Items is [{ Id: "poison", State: Contract.DeliveryState.Consumed }] && second.Next is null &&
                    second.Items[0].Handle != first.Items[0].Handle && second.Items[0].Size > 0,
                    "a fresh public reader resumes the inbox and shows the kept input as consumed");
                try
                {
                    await fresh.InboxAsync(foreign, actor, first.Next, stop);
                    throw new InvalidOperationException("A different tenant continued the parked inbox.");
                }
                catch (ArgumentException) { Console.WriteLine("ok the opaque parked inbox cursor rejects another tenant"); }
                Check(await fresh.GetAsync(foreign, actor, stop) is null &&
                    (await fresh.ListAsync(foreign, new(Parked: true), cancellationToken: stop)).Items.Count == 0 &&
                    (await fresh.InboxAsync(foreign, actor, cancellationToken: stop)).Items.Count == 0,
                    "point, list and kept-input metadata stay within the bound tenant");
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await using var restore = Open();
                await restore.Database.ExecuteSqlRawAsync("""
                    DO $restore$ BEGIN
                        IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = to_regclass('"Instances"') AND attname = 'park_hidden_state')
                        THEN ALTER TABLE "Instances" RENAME COLUMN park_hidden_state TO "State"; END IF;
                        IF EXISTS (SELECT 1 FROM pg_attribute WHERE attrelid = to_regclass('"Instances"') AND attname = 'park_hidden_header')
                        THEN ALTER TABLE "Instances" RENAME COLUMN park_hidden_header TO "FoundationHeader"; END IF;
                    END $restore$;
                    """, cleanup.Token);
            }
        }

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
            if (arrivals && !arrivalControlsRan && batch.Turns.SingleOrDefault(turn => Continues(turn, ParkKeepReason.Arrived)) is { } arrival)
            {
                arrivalControlsRan = true;
                await ContinuationControlsAsync(ownerStore, batch, arrival, "arrival keep", token);
            }
            if (arrivals && !reparkControlsRan && batch.Turns.SingleOrDefault(turn => Continues(turn, ParkKeepReason.Consumed)) is { } repark)
            {
                reparkControlsRan = true;
                await ContinuationControlsAsync(ownerStore, batch, repark, "exhausted re-park", token);
            }
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
                    await Refused(ownerStore, batch with { Epoch = batch.Epoch - 1 }, "a stale epoch refuses the whole Park", token);
                    await ChangedEnvelopeRefused(ownerStore, batch, failed, "the same-key changed envelope rolls Park back", token);
                    await TriggerRefused(ownerStore, batch, "BEFORE DELETE ON \"Subscriptions\"", "RETURN NULL;",
                        "a silent subscription deletion veto rolls Park back", token);
                    await TriggerRefused(ownerStore, batch, KeepWrite, KeepRewrite,
                        "a keep write that rewrites the captured envelope rolls Park back", token);
                    await TriggerRefused(ownerStore, batch, ParkStamp, CheckpointRewrite,
                        "a Park stamp that rewrites the checkpoint rolls back", token);
                    if (inspection)
                    {
                        try
                        {
                            await db.Database.ExecuteSqlRawAsync("""
                                ALTER TABLE "Instances" DISABLE TRIGGER kernel_project_root_park_metadata
                                """, token);
                            await Refused(ownerStore, batch, "a missing Park projection rolls the opening back", token);
                        }
                        finally
                        {
                            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            await using var restore = Open();
                            await restore.Database.ExecuteSqlRawAsync("""
                                ALTER TABLE "Instances" ENABLE TRIGGER kernel_project_root_park_metadata
                                """, cleanup.Token);
                        }
                    }
                    Check((await ReadInputs(park.Instance)).SequenceEqual(physical) &&
                        (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, token)) is { Revision: 1, Status: InstanceStatus.Running, State: original } &&
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
            static bool Recovers(TurnCommit turn) => turn.FoundationRootOperator?.Kind == Contract.Kinds.Recover &&
                turn.Row?.Status == InstanceStatus.Running;
            if (recovery && !recoverControlsRan && batch.Turns.SingleOrDefault(Recovers) is { } recovering)
            {
                recoverControlsRan = true;
                await RecoverControlsAsync(ownerStore, batch, recovering, token);
            }
            var committed = await ownerStore.CommitAsync(batch, token);
            secondRecoverLanded |= committed.Landed && batch.Turns.Any(turn => turn.FoundationRootOperator?.Id == "recover-again");
            if (recovery && committed.Landed && recoveredAt is null && batch.Turns.FirstOrDefault(Recovers) is { } recoveredTurn)
            {
                recoveredAt = probe.Delivered.Count;
                // A fresh input admitted after the recover is given a lower Seq than every released row.
                Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "post", "ordinary", "{}",
                    cancellationToken: token) is { Outcome: Contract.AdmissionOutcome.Accepted }, "the recovered root admits a fresh input");
                await using var db = Open();
                Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "Seq" = (SELECT min("Seq") - 1 FROM "Messages" WHERE "Target" = {recoveredTurn.Instance})
                    WHERE "Target" = {recoveredTurn.Instance} AND "InboxScope" = -4 AND NOT "Done" AND "FoundationReleasedBy" IS NULL
                        AND "Id" NOT LIKE 'operator/%'
                    """, token) == 1, "raw SQL moves the fresh post-recover input ahead of the released rows");
            }
            if (committed.Landed && arrivals && opened is null && batch.Turns.SingleOrDefault(turn => turn.OpensPark) is { } landed)
                opened = await ReadParkMark(landed.Instance);
            keptTail |= committed.Landed && tailOccurrence is not null && batch.Turns.Any(turn =>
                Continues(turn, ParkKeepReason.Arrived) && turn.Done.Any(key => key.Id == tailOccurrence));
            return committed;
        }

        async Task RecoverAsync()
        {
            var reads = await Store(OpenMixed).OpenPreviewRootReadsAsync(8, stop);
            Check(await ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "fresh", "ordinary", payload,
                cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Accepted }, "the parked root admits a second arrival");
            await using (var keeper = await Own("recover-arrival"))
                await Drive(keeper, row => row is { Revision: 4, Status: InstanceStatus.Faulted });
            var parked = await reads.GetAsync(tenant, actor, stop);
            var selection = parked?.Selection ?? throw new InvalidOperationException("The parked view has no Park selection.");
            Check(parked.Park?.At == opened!.At && (await reads.GetAsync(tenant, sibling, stop))?.Selection is null,
                "the public view of the parked root carries a Park selection and the running sibling none");
            ValueTask<Contract.Admission> Send(Contract.ActorRef target, string id, Contract.OperatorCommand command) =>
                ingress.SendAsync(tenant, new Contract.Target.Ref(target), id, Contract.Kinds.Recover, JsonSerializer.Serialize(command),
                    cancellationToken: stop);
            foreach (var (command, what) in new (Contract.OperatorCommand, string)[]
            {
                (new(selection, Contract.RecoverMode.Retry), "Retry"), (new(selection, Contract.RecoverMode.RetryAll), "RetryAll"),
                (new(selection, Handles: ["handle"]), "handles"), (new(selection, Reason: "why"), "a reason"), (new(selection, ThenDelete: true), "ThenDelete")
            })
                Check(await Send(actor, "refused-" + what, command) is { Outcome: Contract.AdmissionOutcome.Refused }, $"ingress refuses a recover with {what}");
            Check(await Send(sibling, "refused-sibling", new(selection)) is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
                "ingress refuses the parked root's selection on its sibling");
            Check(await new PostgresRootIngress(new PostgresRootAdmission(() => KernelDb.ForPostgresRootParkArrivals(mixedOptions), TimeSpan.FromHours(1)),
                    probe.Type, authorizeRecover: (_, _) => true).SendAsync(tenant, new Contract.Target.Ref(actor), "protocol-21", Contract.Kinds.Recover,
                    JsonSerializer.Serialize(new Contract.OperatorCommand(selection)), cancellationToken: stop) is { Outcome: Contract.AdmissionOutcome.Refused },
                "a protocol 21 ingress refuses Recover");

            // A kept set larger than one page is refused at the owner and leaves the Park as it was.
            await using (var db = Open())
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Kind", "Partition", "Due", "Seen", "Attempts", "Trial", "Done", "JobGeneration", "FoundationKeptBy")
                    SELECT 'page-filler-' || n, -4, {before.Actor.Id}, 'ordinary', 0, 0, 0, 0, 0, true, 0, 1 FROM generate_series(1, {InMemoryStore.ParkPageLimit}) n
                    """, stop);
            Check(await Send(actor, "recover-page", new(selection)) is { Outcome: Contract.AdmissionOutcome.Accepted }, "ingress admits a recover of the oversized Park");
            await using (var pager = await Own("recover-page"))
                await Drive(pager, row => row is { Revision: 5 });
            Check((await store.ReadJournalOfAsync(0, before.Actor.Id, 0, stop))[^1] is { Revision: 5, Entries: """{"Outcome":"refused","Operation":"recover-page","Reason":"page"}""" } &&
                (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop)) is { Status: InstanceStatus.Faulted, Fault: fault, State: original } &&
                await ReadParkMark(before.Actor.Id) == opened && (await reads.GetAsync(tenant, actor, stop))?.Park == parked.Park,
                "the owner records a page refusal at the next revision and keeps the Park mark and fault");
            await using (var db = Open())
                await db.Database.ExecuteSqlRawAsync("""DELETE FROM "Messages" WHERE "Id" LIKE 'page-filler-%'""", stop);

            Check(await Send(actor, "recover", new(selection)) is { Outcome: Contract.AdmissionOutcome.Accepted }, "ingress admits the recover");
            Check(await Send(actor, "recover", new(selection)) is { Outcome: Contract.AdmissionOutcome.Duplicate }, "a repeated recover operation is a duplicate");
            Check(await Send(actor, "recover-again", new(selection)) is { Outcome: Contract.AdmissionOutcome.Accepted }, "a second recover of the same Park is admitted");
            await using (var recoverer = await Own("recoverer"))
                await Drive(recoverer, _ => probe.Delivered.Contains((actor, "post")) && secondRecoverLanded);
            var after = await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop);
            var view = await reads.GetAsync(tenant, actor, stop);
            await using (var db = Open())
                Check(after is { Status: InstanceStatus.Running, Fault: null, State: original } && view is { Park: null, Header.Status: Contract.ActorStatus.Running } &&
                    await ReadParkMark(before.Actor.Id) == new ParkMark(null, null, null) && await db.Database.SqlQuery<long>($"""
                        SELECT "FoundationParkGeneration" AS "Value" FROM "Instances" WHERE "Id" = {before.Actor.Id}
                        """).SingleAsync(stop) == 1 && await db.Database.SqlQuery<long?>($"""
                        SELECT "ParkRevision" AS "Value" FROM "FoundationActorList" WHERE "Reference" = {actor.Id}
                        """).SingleAsync(stop) is null,
                    "the root is Running with its Park mark and projection cleared and release generation one");
            var released = probe.Delivered.Skip(recoveredAt!.Value).Where(item => item.Actor == actor).Select(item => item.Id).ToArray();
            Check(released is ["later", "fresh", "post"] && probe.Failed == 1,
                $"the released arrivals reach the model in Seq order before the lower-Seq fresh input, and the fault input is not redelivered ({string.Join(", ", released)})");
            var inputs = await ReadInputs(before.Actor.Id);
            Check(inputs.Single(row => row.Kind == "fail") is { Done: true, KeptBy: null, Payload: null } && inputs.All(row => row.Done && row.KeptBy is null),
                "the fault input is cleared and stays settled, and no kept row remains");
            var history = await store.ReadJournalOfAsync(0, before.Actor.Id, 0, stop);
            Check(history.Count(entry => entry.Entries.Contains("\"recovered\"", StringComparison.Ordinal)) == 1 && history.Any(entry => entry is
                { Revision: 6, Entries: """{"Outcome":"recovered","Operation":"recover","Restored":2,"Cleared":1,"AlreadyRearmed":0}""" }) &&
                history[^1].Entries == """{"Outcome":"refused","Operation":"recover-again","Reason":"selection"}""" && history[^1].Revision == after!.Revision,
                "the journal records one recovery, and the second recover finds the root Running and records a refusal");
            Check(await Send(actor, "recover", new(selection)) is { Outcome: Contract.AdmissionOutcome.Duplicate } &&
                (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop))?.Revision == after!.Revision,
                "the recover operation id stays a duplicate after it applied once");
            Check(await Send(actor, "recover-running", new(selection)) is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
                "ingress refuses a stale Park selection on the running root");

            // A fresh protocol 21 store in its own schema of the same empty database.
            await using (var db = Open())
                await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA park_protocol21", stop);
            var prior = new NpgsqlConnectionStringBuilder(connection) { SearchPath = "park_protocol21" };
            var priorOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(prior.ConnectionString).Options;
            await using (var db = KernelDb.ForPostgresRootParkArrivals(priorOptions))
                await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(stop);
            await new EfStore(() => KernelDb.ForPostgresRootParkArrivals(priorOptions), postgres: true).CreateAsync(1);
            await new PostgresRootAdmission(() => KernelDb.ForPostgresRootParkArrivals(priorOptions), TimeSpan.FromHours(1)).InstallAsync(stop);
            await FormatRefused(async () => await new PostgresRootIngress(new PostgresRootAdmission(() => KernelDb.ForPostgresRootParkRecovery(priorOptions),
                    TimeSpan.FromHours(1)), probe.Type, authorizeRecover: (_, _) => true).SendAsync(tenant, new Contract.Target.Ref(actor), "protocol-21-store",
                    Contract.Kinds.Recover, JsonSerializer.Serialize(new Contract.OperatorCommand(selection)), cancellationToken: stop),
                "a recover sent to a protocol 21 store fails with a format error");
        }

        // Each guard rolls back the whole recover and leaves the Faulted row, kept rows, journal and Park mark.
        async Task RecoverControlsAsync(EfStore ownerStore, CommitBatch batch, TurnCommit turn, CancellationToken token)
        {
            try
            {
                var physical = await ReadInputs(turn.Instance);
                var checkpoint = (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, token))!;
                var journal = await store.ReadJournalOfAsync(0, turn.Instance, 0, token);
                var mark = await ReadParkMark(turn.Instance);
                await Refused(ownerStore, batch with { Epoch = batch.Epoch - 1 }, "a stale epoch refuses the recover", token);
                await ChangedEnvelopeRefused(ownerStore, batch, physical.First(row => row.KeptBy == (short)ParkKeepReason.Arrived && row.Payload == payload),
                    "a kept payload changed after the poll refuses the recover", token);
                await TriggerRefused(ownerStore, batch, "BEFORE UPDATE OF \"FoundationReleasedBy\" ON \"Messages\"",
                    // The raw command is formatted, so the payload literal doubles its braces.
                    "IF NEW.\"Payload\" = '{{}}' THEN RETURN NULL; END IF; RETURN NEW;", "a trigger that vetoes one restore refuses the recover", token);
                await TriggerRefused(ownerStore, batch, "BEFORE UPDATE OF \"FoundationParkGeneration\" ON \"Instances\"", CheckpointRewrite,
                    "a recover write that rewrites the checkpoint state rolls back", token);
                await using (var db = Open())
                {
                    try
                    {
                        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" DISABLE TRIGGER kernel_project_root_park_metadata""", token);
                        await Refused(ownerStore, batch, "a missing Park projection rolls the recover back", token);
                    }
                    finally
                    {
                        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" ENABLE TRIGGER kernel_project_root_park_metadata""", CancellationToken.None);
                    }
                }
                var unchanged = await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, token);
                Check((await ReadInputs(turn.Instance)).SequenceEqual(physical) && unchanged is { Status: InstanceStatus.Faulted, Fault: fault } &&
                    unchanged.Revision == checkpoint.Revision && unchanged.State == checkpoint.State &&
                    (await store.ReadJournalOfAsync(0, turn.Instance, 0, token)).SequenceEqual(journal) && await ReadParkMark(turn.Instance) == mark,
                    "the refused recover writes keep the Faulted row, kept rows, journal and Park mark");
            }
            catch (Exception error)
            {
                controlError = error;
                throw;
            }
        }

        static bool Continues(TurnCommit turn, ParkKeepReason reason) =>
            !turn.OpensPark && turn.FoundationKept is { Count: > 0 } kept && kept.Values.All(value => value == reason) &&
            (turn.Journal is null) == (reason == ParkKeepReason.Arrived);

        // The arrival keep and the exhausted re-park continue an existing Park. Each guard refuses the whole batch.
        async Task ContinuationControlsAsync(EfStore ownerStore, CommitBatch batch, TurnCommit turn, string kind, CancellationToken token)
        {
            try
            {
                var physical = await ReadInputs(turn.Instance);
                var checkpoint = (await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, token))!;
                var journal = await store.ReadJournalOfAsync(0, turn.Instance, 0, token);
                var mark = await ReadParkMark(turn.Instance);
                Check(checkpoint is { Status: InstanceStatus.Faulted, Fault: fault } && checkpoint.Revision == turn.Expected && mark == opened,
                    $"the {kind} starts from the opening Park mark");
                var kept = physical.Single(row => row.Id == turn.Done[0].Id);
                await Refused(ownerStore, batch with { Epoch = batch.Epoch - 1 }, $"a stale epoch refuses the {kind}", token);
                await Refused(ownerStore, batch with
                {
                    Turns = [.. batch.Turns.Select(item => ReferenceEquals(item, turn) ? item with { Expected = item.Expected - 1 } : item)]
                }, $"a stale expected revision refuses the {kind}", token);
                await ChangedEnvelopeRefused(ownerStore, batch, kept, $"the same-key changed envelope refuses the {kind}", token);
                await TriggerRefused(ownerStore, batch, KeepWrite, KeepRewrite,
                    $"a keep write that rewrites the captured envelope refuses the {kind}", token);
                await TriggerRefused(ownerStore, batch, ParkStamp, CheckpointRewrite,
                    $"a Park stamp that rewrites the checkpoint refuses the {kind}", token);
                await TriggerRefused(ownerStore, batch, ParkStamp, """NEW."FoundationParkAt" := NEW."FoundationParkAt" + 1; RETURN NEW;""",
                    $"a write that moves the Park time refuses the {kind}", token);
                await TriggerRefused(ownerStore, batch, ParkStamp,
                    """NEW."FoundationParkRevision" := NEW."FoundationParkRevision" + 1; RETURN NEW;""",
                    $"a write that moves the opening revision refuses the {kind}", token);
                await StoredMarkRefused(ownerStore, batch, turn.Instance, mark, $"""
                    UPDATE "Instances" SET "Fault" = replace("Fault", ' ', chr(92) || 'u0020') WHERE "Id" = {turn.Instance}
                    """, $"a differently encoded stored fault refuses the {kind}", token);
                await StoredMarkRefused(ownerStore, batch, turn.Instance, mark, $"""
                    UPDATE "Instances" SET "FoundationParkRevision" = "Revision" + 1 WHERE "Id" = {turn.Instance}
                    """, $"an opening revision after the expected revision refuses the {kind}", token);
                await StoredMarkRefused(ownerStore, batch, turn.Instance, mark, $"""
                    UPDATE "Instances" SET "FoundationParkRevision" = NULL WHERE "Id" = {turn.Instance}
                    """, $"a missing opening revision refuses the {kind}", token);
                var unchanged = await store.ReadFirstRootAsync(tenant.Tenant, actor.Id, token);
                Check((await ReadInputs(turn.Instance)).SequenceEqual(physical) &&
                    unchanged is { Status: InstanceStatus.Faulted, Fault: fault } && unchanged.Revision == checkpoint.Revision &&
                    unchanged.State == checkpoint.State && unchanged.StateVersion == checkpoint.StateVersion &&
                    (await store.ReadJournalOfAsync(0, turn.Instance, 0, token)).SequenceEqual(journal) &&
                    await ReadParkMark(turn.Instance) == mark,
                    $"the refused {kind} writes keep the checkpoint, journal, Park mark, time, revision, fault and occurrence");
            }
            catch (Exception error)
            {
                controlError = error;
                throw;
            }
        }

        async Task ChangedEnvelopeRefused(EfStore ownerStore, CommitBatch batch, InputImage input, string message, CancellationToken token)
        {
            await using var db = Open();
            var selected = db.Messages.Where(row => row.Id == input.Id);
            await selected.ExecuteUpdateAsync(update => update.SetProperty(row => row.Payload, "changed"), token);
            try { await Refused(ownerStore, batch, message, token); }
            finally
            {
                await selected.ExecuteUpdateAsync(update => update.SetProperty(row => row.Payload, input.Payload), token);
            }
        }

        async Task TriggerRefused(EfStore ownerStore, CommitBatch batch, string on, string body, string message, CancellationToken token)
        {
            await using var db = Open();
            var table = on[(on.IndexOf(" ON ", StringComparison.Ordinal) + 4)..];
            // The trigger text comes only from this fixture's constants.
#pragma warning disable EF1003
            await db.Database.ExecuteSqlRawAsync(
                "CREATE FUNCTION park_control() RETURNS trigger LANGUAGE plpgsql AS $$BEGIN " + body + " END$$; " +
                "CREATE TRIGGER park_control " + on + " FOR EACH ROW EXECUTE FUNCTION park_control();", token);
            try { await Refused(ownerStore, batch, message, token); }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER park_control ON " + table + "; DROP FUNCTION park_control();", token);
            }
#pragma warning restore EF1003
        }

        // The checkpoint writer trigger refuses direct edits, so the control edits the stored mark in its own transaction.
        async Task StoredMarkRefused(EfStore ownerStore, CommitBatch batch, string instance, ParkMark mark, FormattableString change,
            string message, CancellationToken token)
        {
            await Bypass(change, token);
            try { await Refused(ownerStore, batch, message, token); }
            finally
            {
                await Bypass($"""
                    UPDATE "Instances" SET "Fault" = {mark.Fault}, "FoundationParkRevision" = {mark.Revision} WHERE "Id" = {instance}
                    """, token);
            }
        }

        async Task Bypass(FormattableString change, CancellationToken token)
        {
            await using var db = Open();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" DISABLE TRIGGER kernel_require_root_checkpoint_writer""", token);
            if (await db.Database.ExecuteSqlInterpolatedAsync(change, token) != 1)
                throw new InvalidOperationException("The control did not change exactly the root row.");
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" ENABLE TRIGGER kernel_require_root_checkpoint_writer""", token);
            await transaction.CommitAsync(token);
        }

        async Task<ParkMark> ReadParkMark(string id)
        {
            await using var db = Open();
            return await db.Database.SqlQuery<ParkMark>($"""
                SELECT "FoundationParkAt" AS "At", "FoundationParkRevision" AS "Revision", "Fault" FROM "Instances" WHERE "Id" = {id}
                """).SingleAsync(stop);
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

    private sealed record ParkMark(long? At, long? Revision, string? Fault);

    private const string KeepWrite = "BEFORE UPDATE OF \"FoundationKeptBy\" ON \"Messages\"";
    private const string KeepRewrite = "NEW.\"Payload\" := 'changed by the keep write'; RETURN NEW;";
    private const string ParkStamp = "BEFORE UPDATE OF \"FoundationParkAt\" ON \"Instances\"";
    private const string CheckpointRewrite = "NEW.\"State\" := replace(NEW.\"State\", 'true', 'false'); RETURN NEW;";

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
        private readonly Dictionary<Contract.ActorRef, int> _turns = [];
        public List<(Contract.ActorRef Actor, string Id)> Delivered { get; } = [];
        public int Failed { get; private set; }
        public int Ordinary { get; private set; }
        public int TurnsOf(Contract.ActorRef actor) => _turns.GetValueOrDefault(actor);
        public string Type => "postgres-park-opening";
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            _turns[turn.Header.Ref] = TurnsOf(turn.Header.Ref) + 1;
            Delivered.AddRange(turn.Inbox.Select(input => (turn.Header.Ref, input.Id)));
            if (turn.Inbox.Any(input => input.Kind == "fail"))
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
