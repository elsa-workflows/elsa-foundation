using System.Data.Common;
using Elsa.ActorFoundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class RootScheduleInventoryChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool settleOccurrences = false, bool fenceInBatch = true)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var token = timeout.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("root-schedule-inventory-");
        try
        {
            var options = new DbContextOptionsBuilder<KernelDb>();
            var barrier = new PostgresClaimBarrier();
            if (postgres)
                options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                    ?? throw new InvalidOperationException("Use a dedicated empty PostgreSQL qualification database."))
                    .AddInterceptors(barrier);
            else
                options.UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(directory!.FullName, "schedules.db"), ForeignKeys = true, Pooling = false, DefaultTimeout = 1
                }.ConnectionString);

            var format = settleOccurrences ? StoreFormat.RootScheduleSettlementQualification : StoreFormat.RootScheduleInventoryQualification;
            var definitions = new SqliteAdmissionDefinitions();
            KernelDb Open() => !postgres ? KernelDb.ForSqliteRootRuntime(options.Options, definitions, qualificationFormat: format)
                : settleOccurrences ? KernelDb.ForPostgresRootScheduleSettlementQualification(options.Options)
                : KernelDb.ForPostgresRootScheduleInventoryQualification(options.Options);
            await using var db = Open();
            var pg = new PostgresRootAdmission(Open, TimeSpan.Zero, scheduleActorType: ScheduleActor.ActorType);
            var sqlite = new SqliteRootAdmission(Open, TimeSpan.Zero, definitions, scheduleActorType: ScheduleActor.ActorType);
            if (postgres)
            {
                Require(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the PostgreSQL qualification database is empty");
                var provisioner = new EfStore(Open, postgres: true);
                await provisioner.CreateAsync(2);
                await provisioner.InstallPreviewRootSchemaAsync(token);
            }
            else await EfStore.InstallSqliteShippedAsync(db, 1, token);

            const string typeData = """{"Schedule":{"Kind":"interval","Value":"PT1M"},"StartKey":"unused","Revision":1}""";
            var timer = new ScheduleRootKey("definition", "timer-a");
            try
            {
                if (postgres) await new PostgresRootAdmission(Open, TimeSpan.Zero).AdmitScheduleAsync("tenant-a", timer, typeData, token);
                else await new SqliteRootAdmission(Open, TimeSpan.Zero, new SqliteAdmissionDefinitions()).AdmitScheduleAsync("tenant-a", timer, typeData, token);
                throw new InvalidOperationException("An ordinary ingress admitted a schedule creator.");
            }
            catch (NotSupportedException) { Console.WriteLine("ok ordinary ingress cannot reserve a schedule key"); }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            {
                await start.Task;
                return await AdmitAsync("tenant-a", timer);
            }, token)).ToArray();
            start.SetResult();
            var contenders = await Task.WhenAll(tasks);
            var reference = contenders.Single(item => item.Outcome == Contract.AdmissionOutcome.Accepted).Actor?.Id
                ?? throw new InvalidOperationException("The winning admission supplied no actor reference.");
            Require(contenders.Count(item => item.Outcome == Contract.AdmissionOutcome.Duplicate && item.Actor?.Id == reference) == 5,
                "six contending admissions yield one exact schedule actor");
            if (postgres)
                Require(barrier.ConfirmedOverlap, "two PostgreSQL transactions saw a missing key before attempting the same insert");

            var again = postgres
                ? await new PostgresRootAdmission(Open, TimeSpan.Zero, scheduleActorType: ScheduleActor.ActorType).AdmitScheduleAsync("tenant-a", timer, typeData, token)
                : await new SqliteRootAdmission(Open, TimeSpan.Zero, new SqliteAdmissionDefinitions(), scheduleActorType: ScheduleActor.ActorType)
                    .AdmitScheduleAsync("tenant-a", timer, typeData, token);
            Require(again.Outcome == Contract.AdmissionOutcome.Duplicate && again.Actor?.Id == reference,
                "a fresh admission service recovers the creator before any actor checkpoint");
            Require(!await db.Instances.AnyAsync(token) && await db.Messages.CountAsync(row => row.InboxScope == 0 && !row.Done, token) == 1,
                "the inventory lists a queued creator and requires no actor checkpoint");
            try
            {
                if (postgres)
                    await new PostgresRootAdmission(Open, TimeSpan.Zero, scheduleActorType: "ordinary").AdmitScheduleAsync("tenant-a", timer, typeData, token);
                else await new SqliteRootAdmission(Open, TimeSpan.Zero, new SqliteAdmissionDefinitions(), scheduleActorType: "ordinary")
                    .AdmitScheduleAsync("tenant-a", timer, typeData, token);
                throw new InvalidOperationException("An incompatible host binding claimed the reserved key.");
            }
            catch (StoreFormatException) { Console.WriteLine("ok a different host-bound actor type cannot claim the existing schedule"); }

            var second = await AdmitAsync("tenant-a", new("definition", "timer-b"));
            var unrelated = await AdmitAsync("tenant-b", timer);
            _ = await AdmitAsync("tenant-a", new("another-definition", "timer-c"));
            Require(unrelated.Actor?.Id != reference, "another tenant owns its independent schedule");
            var page1 = await ReadAsync("tenant-a", "definition", null, 1);
            var page2 = await ReadAsync("tenant-a", "definition", page1.Next, 1);
            Require(page1 is { Roots: [{ Key: "timer-a" } first], Next: "timer-a" } && first.Reference == reference &&
                page2 is { Roots: [{ Key: "timer-b" } last], Next: null } && last.Reference == second.Actor?.Id,
                "restart inventory seeks within the exact tenant and definition without reading the checkpoint table");
            Require((await ReadAsync("missing", "definition", null, 1)).Roots.Count == 0,
                "a foreign tenant cannot see either schedule actor");
            if (settleOccurrences)
            {
                await VerifyOccurrencesAsync(db, Open, postgres, fenceInBatch, pg, sqlite, timer, reference, token);
                if (postgres)
                    Require(barrier.ConfirmedSettlementOverlap, "two relays contend on the same birth and creation identity");
            }
            if (!postgres)
            {
                await db.Database.OpenConnectionAsync(token);
                await using var writer = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
                Require((await ReadAsync("tenant-a", "definition", null, 1)).Roots is [{ Key: "timer-a" }],
                    "the inventory reads a WAL snapshot while another connection holds the writer");
                await writer.RollbackAsync(token);
            }

            var ordinarySpec = new StartSpec("ordinary", "{}", History: History.Minimal);
            var ordinary = postgres
                ? (await pg.AdmitAsync("tenant-a", "ordinary", ordinarySpec, cancellationToken: token)).Reference
                : (await sqlite.StartAsync("tenant-a", "ordinary", ordinarySpec, null, null, token)).Actor?.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "FoundationScheduleRoots" SET "Reference" = {ordinary}
                WHERE "Tenant" = 'tenant-a' AND "Key" = 'timer-a'
                """, token);
            try
            {
                await ReadAsync("tenant-a", "definition", null, 1);
                throw new InvalidOperationException("The inventory accepted an ordinary creator as its schedule root.");
            }
            catch (StoreFormatException) { Console.WriteLine("ok even an exact-tenant ordinary creator cannot satisfy schedule inventory"); }
            if (postgres)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "FoundationScheduleRoots" SET "Reference" = {reference}
                    WHERE "Tenant" = 'tenant-a' AND "Key" = 'timer-a'
                    """, token);
                await db.Database.ExecuteSqlRawAsync("""
                    DROP INDEX "IX_FoundationScheduleRoots_Definition";
                    CREATE INDEX "IX_FoundationScheduleRoots_Definition"
                        ON "FoundationScheduleRoots" ("Tenant", "Definition", "Key" text_pattern_ops);
                    """, token);
                try
                {
                    await ReadAsync("tenant-a", "definition", null, 1);
                    throw new InvalidOperationException("An inventory read accepted an incompatible ordered index.");
                }
                catch (StoreFormatException) { Console.WriteLine("ok PostgreSQL refuses the wrong physical definition index"); }
                var pendingBefore = await db.Messages.CountAsync(token);
                try
                {
                    await AdmitAsync("tenant-a", new("definition", "drifted-index"));
                    throw new InvalidOperationException("A creator was admitted under an incompatible definition index.");
                }
                catch (StoreFormatException) { Console.WriteLine("ok PostgreSQL also refuses to admit a creator with that index"); }
                Require(await db.Messages.CountAsync(token) == pendingBefore, "schema refusal leaves the inbox unchanged");
            }

            await using var shipped = postgres ? KernelDb.ForPostgresRootShipped(options.Options) : KernelDb.ForSqliteRootShipped(options.Options);
            try
            {
                if (postgres) await EfStore.RequireStoreVersionsAsync(shipped, token);
                else await EfStore.InstallSqliteShippedAsync(shipped, 2, token);
                throw new InvalidOperationException("The shipped marker admitted the private schedule inventory.");
            }
            catch (StoreFormatException) { Console.WriteLine("ok shipped format refuses the private schedule inventory"); }
            return 0;

            Task<Contract.Admission> AdmitAsync(string tenant, ScheduleRootKey key) => postgres
                ? pg.AdmitScheduleAsync(tenant, key, typeData, token) : sqlite.AdmitScheduleAsync(tenant, key, typeData, token);
            Task<RootScheduleInventory.Page> ReadAsync(string tenant, string definition, string? after, int limit) => postgres
                ? pg.ReadScheduleRootsAsync(tenant, definition, after, limit, token)
                : sqlite.ReadScheduleRootsAsync(tenant, definition, after, limit, token);
        }
        finally
        {
            directory?.Delete(recursive: true);
        }
    }

    private static async Task VerifyOccurrencesAsync(KernelDb db, Func<KernelDb> open, bool postgres, bool fenceInBatch,
        PostgresRootAdmission pg, SqliteRootAdmission sqlite, ScheduleRootKey timer, string reference, CancellationToken token)
    {
        const string tenant = "tenant-a";
        var occurrence = new RootScheduleOccurrence(timer, reference, 1, "~timer-a", 1000);
        var pinned = new StartSpec("workflow", """{"Workflow":"version-a"}""", Input: "original");
        var foundation = postgres ? null : SqliteEmbeddedFoundation.ForScheduleQualification(db.Database.GetDbConnection().DataSource,
            "scheduled-occurrence-relay", pinned.Model);
        if (foundation is not null) await foundation.InstallShippedAsync(token);
        var bound = EmbeddedTenant.Bind(tenant);
        if (postgres)
        {
            var pressure = RootAdmissionPressure.CreateDefault().Backlog.HighWater + 1;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Partitions" SET "Backlog" = "Backlog" + {pressure}
                """, token);
            try
            {
                var throttled = await PostgresPreviewHosting.OpenScheduleOccurrencesAsync(open, bound, pinned.Model,
                    ScheduleActor.ActorType, cancellationToken: token);
                var denied = await throttled.AdmitAsync(Wire(occurrence), new(pinned.Model, pinned.Definition, pinned.Input), token);
                Require(denied is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.InboxFull, RetryAfter: not null },
                    "a PostgreSQL relay without shared requests still honors the persisted partition backlog");
            }
            finally
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Partitions" SET "Backlog" = "Backlog" - {pressure}
                    """, token);
            }
        }
        var relay = postgres
            ? await PostgresPreviewHosting.OpenScheduleOccurrencesAsync(open, bound, pinned.Model, ScheduleActor.ActorType, cancellationToken: token)
            : foundation!.BindScheduleOccurrences(bound, ScheduleActor.ActorType);
        try
        {
            await relay.AdmitAsync(Wire(occurrence), new(ScheduleActor.ActorType, "{}"), token);
            throw new InvalidOperationException("A host occurrence admitted the auxiliary type as its workflow.");
        }
        catch (ArgumentException) { Console.WriteLine("ok the host occurrence relay enforces the configured primary workflow type"); }
        var before = await db.Messages.CountAsync(token);
        var foreign = postgres
            ? await pg.ReadScheduleRootsAsync("tenant-b", timer.Definition, null, 1, token)
            : await sqlite.ReadScheduleRootsAsync("tenant-b", timer.Definition, null, 1, token);
        var otherTenant = await AdmitAsync(occurrence with { ScheduleReference = foreign.Roots.Single().Reference }, pinned);
        Require(otherTenant is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch },
            "an occurrence cannot use a different tenant's schedule binding");
        var contenders = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => AdmitAsync(occurrence, pinned), token)));
        var first = contenders.Single(item => item.Outcome == Contract.AdmissionOutcome.Accepted);
        Require(first.Actor is not null && contenders.Count(item => item.Outcome == Contract.AdmissionOutcome.Duplicate && item.Actor == first.Actor) == 3 &&
            await db.Messages.CountAsync(token) == before + 1, "a repeated relay acceptance writes one workflow birth and queues one creator");
        Require((await AdmitAsync(occurrence, pinned)) is { Outcome: Contract.AdmissionOutcome.Duplicate } replay && replay.Actor == first.Actor &&
            !await db.Database.SqlQueryRaw<bool>("""SELECT EXISTS (SELECT 1 FROM "FoundationRootReceipts") AS "Value" """).SingleAsync(token),
            "replay uses the durable birth even with no generic operation receipt");

        await MustRefuseAsync(occurrence, pinned with { Definition = """{"Workflow":"version-b"}""" });
        await MustRefuseAsync(occurrence with { Due = 2000 }, pinned);
        Require(await db.Messages.CountAsync(token) == before + 1, "changed pinned workflow or due time adds no creator to the existing occurrence");
        var next = await AdmitAsync(occurrence with { ArmGeneration = 2 }, pinned);
        Require(next.Outcome == Contract.AdmissionOutcome.Accepted && next.Actor != first.Actor &&
            await db.Messages.CountAsync(token) == before + 2, "the next arm generation has a distinct occurrence even with the same timer identity");
        Require(!await RetiredAsync(occurrence, first.Actor!.Value.Id),
            "a committed birth awaiting its first checkpoint is not retired");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "Messages" WHERE "Target" = (SELECT "Actor" FROM "FoundationRootBirths" WHERE "Reference" = {first.Actor?.Id})
            """, token);
        Require((await AdmitAsync(occurrence, pinned)).Actor == first.Actor &&
            await db.Messages.CountAsync(token) == before + 1, "replay does not require or recreate the original inbox row");
        Require(!await RetiredAsync(occurrence, first.Actor.Value.Id), "an absent actor body never proves retirement");
        if (postgres)
        {
            var blocked = occurrence with { ArmGeneration = 3 };
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION schedule_reject_creator() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN RETURN NULL; END; $body$;
                CREATE TRIGGER schedule_reject_creator BEFORE INSERT ON "Messages"
                FOR EACH ROW EXECUTE FUNCTION schedule_reject_creator();
                """, token);
            try { await MustRefuseAsync(blocked, pinned); }
            finally { await db.Database.ExecuteSqlRawAsync("""DROP TRIGGER schedule_reject_creator ON "Messages"; DROP FUNCTION schedule_reject_creator();""", token); }
            Require((await AdmitAsync(blocked, pinned)).Outcome == Contract.AdmissionOutcome.Accepted &&
                await db.Messages.CountAsync(token) == before + 2, "a suppressed PostgreSQL creator rolls back its birth and the exact retry can enqueue it");
        }

        // The inventory fixture leaves schedule creators pending. Keep them out of this workflow-only owner's
        // horizon while preserving the genuine schedule births and the occurrence's ordinary workflow creator.
        var deferred = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" AS pending SET "Due" = {long.MaxValue - 1}
            WHERE pending."InboxScope" = 0 AND pending."Due" = 0 AND EXISTS (
                SELECT 1 FROM "FoundationScheduleRoots" schedule JOIN "FoundationRootBirths" birth
                    ON birth."Reference" = schedule."Reference" AND birth."Tenant" = schedule."Tenant"
                WHERE birth."Creator" = pending."Id" AND birth."Actor" = pending."Target"
                    AND birth."Partition" = pending."Partition" AND birth."Creation" = 1)
            """, token);
        Require(deferred > 0, "the workflow-retirement fixture defers only the pending schedule actors");
        var nextOccurrence = occurrence with { ArmGeneration = 2 };
        var nextActor = next.Actor!.Value;
        await RunRetirementAsync(open, db, postgres, fenceInBatch, tenant, pinned.Model, nextActor,
            () => RetiredAsync(nextOccurrence, nextActor.Id), token);
        Require(!await RetiredAsync(nextOccurrence, first.Actor.Value.Id),
            "retirement proof is tied to the exact occurrence birth");
        var messageCount = await db.Messages.CountAsync(token);
        var retiredReplay = await AdmitAsync(nextOccurrence, pinned);
        Require(retiredReplay is { Outcome: Contract.AdmissionOutcome.Duplicate } && retiredReplay.Actor == nextActor &&
            await db.Messages.CountAsync(token) == messageCount, "the retired birth replays without recreating its inbox");

        Task<Contract.Admission> AdmitAsync(RootScheduleOccurrence item, StartSpec spec) =>
            relay.AdmitAsync(Wire(item),
                new(spec.Model, spec.Definition, spec.Input, spec.Ephemeral, (Contract.History)spec.History), token).AsTask();

        static Contract.AcceptedScheduleOccurrence Wire(RootScheduleOccurrence item) =>
            new(item.Schedule.Definition, item.Schedule.Key, new(item.ScheduleReference), item.ArmGeneration, item.TimerDelivery, item.Due);

        Task<bool> RetiredAsync(RootScheduleOccurrence item, string actorReference) => postgres
            ? pg.IsScheduledOccurrenceRetiredAsync(tenant, item, pinned, actorReference, token)
            : sqlite.IsScheduledOccurrenceRetiredAsync(tenant, item, pinned, actorReference, token);

        async Task MustRefuseAsync(RootScheduleOccurrence item, StartSpec spec)
        {
            try
            {
                await AdmitAsync(item, spec);
                throw new InvalidOperationException("A changed immutable occurrence was accepted.");
            }
            catch (StoreFormatException) { }
        }
    }

    private static async Task RunRetirementAsync(Func<KernelDb> open, KernelDb db, bool postgres, bool fenceInBatch, string tenant, string type,
        Contract.ActorRef reference, Func<Task<bool>> isRetired, CancellationToken token)
    {
        const string node = "schedule-retirement";
        var actor = new FinishingActor(type);
        var store = new EfStore(open, postgres, fenceInBatch) { CommitSql = CommitSql.Owned };
        await store.EnableRootSelfSendsAsync(token);
        await store.EnableRootJobsAsync(token);
        await store.EnableRootChildrenAsync(token);
        var roots = postgres ? store.BindWorkerNode(actor.Withholds) : store.BindSqliteWorkerNode(actor.Withholds);
        var adapter = new ActorAdapter(store.WorkerHeaders(roots), actor, store.RootSelfIssuer,
            rootJobHandle: store.RootJobHandle, rootTimedJobClock: store.ReadRootTimedJobPublicationTimeAsync, rootHeldReplies: true);
        Contract.IIngress ingress;
        Contract.IActorReads reads;
        if (postgres)
        {
            var foundation = await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(open, type, Authorize, cancellationToken: token);
            ingress = foundation;
            reads = foundation;
        }
        else
        {
            var path = db.Database.GetDbConnection().DataSource;
            var format = StoreFormat.RootScheduleSettlementQualification;
            var requests = SqliteEmbeddedHosting.CreateRequests(path, node, type, qualificationFormat: format);
            ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(path, type, Authorize, requests, token);
            reads = SqliteEmbeddedHosting.CreateFoundationReads(path, qualificationFormat: format);
        }

        var partitions = await db.Partitions.CountAsync(token);
        await using var worker = new Worker(node, Enumerable.Range(0, partitions).ToArray(), store, adapter,
            new WorkerOptions { Partitions = partitions, PassTurnBudget = 1 });
        var host = new DurableHost([worker]);
        await host.HeartbeatAsync(Defaults.Lease, token);
        Require(await host.ActivateAsync(0, token) == worker && worker.Fences.Count == partitions,
            $"the fixture holds its partition fences ({(fenceInBatch ? "in-batch" : "checked")})");
        var scope = EmbeddedTenant.Bind(tenant);
        Contract.ActorView? completed;
        do
        {
            await DriveAsync();
            completed = await reads.GetAsync(scope, reference, token);
        } while (completed?.Header.Status != Contract.ActorStatus.Finished);
        Require(!await isRetired(), "a finished scheduled workflow still owns its executable pin");
        var command = Json.Write(new Contract.OperatorCommand(completed.Selection!, ExpectedTarget: reference));
        Require((await ingress.SendAsync(scope, new Contract.Target.Ref(reference), "retire-occurrence", Contract.Kinds.Delete,
            command, cancellationToken: token)).Outcome == Contract.AdmissionOutcome.Accepted, "the owner admits selected scheduled workflow retirement");
        while (!await isRetired())
        {
            await DriveAsync();
        }
        Require(await isRetired(), "the fenced owner committed a durable exact retirement proof");

        async Task DriveAsync()
        {
            await worker.WokenAsync(null, token);
            await worker.RunAsync(token);
            if (!worker.Owns) throw new InvalidOperationException("The scheduled retirement fixture lost its owner.");
            await Task.Delay(15, token);
        }

        bool Authorize(Contract.BoundScope bound, Contract.ActorRef target, string kind) =>
            bound.Tenant == tenant && target == reference && kind == Contract.Kinds.Delete;
    }

    private sealed class FinishingActor(string type) : Contract.IActorType
    {
        public string Type => type;
        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Header.Type == type)
            {
                turn.State = "{}";
                turn.End(Contract.ActorStatus.Finished);
            }
            return ValueTask.CompletedTask;
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class PostgresClaimBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _contenders = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _settlers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reached;
        private int _settled;
        internal bool ConfirmedOverlap => Volatile.Read(ref _reached) >= 2;
        internal bool ConfirmedSettlementOverlap => Volatile.Read(ref _settled) >= 2;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"FoundationScheduleRoots\"", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _reached) >= 2) _contenders.TrySetResult();
                await _contenders.Task.WaitAsync(cancellationToken);
            }
            if (command.CommandText.StartsWith("/* accepted schedule occurrence */", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _settled) >= 2) _settlers.TrySetResult();
                await _settlers.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
