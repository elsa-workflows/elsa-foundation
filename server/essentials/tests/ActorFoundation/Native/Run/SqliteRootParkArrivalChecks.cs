using System.Data.Common;
using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    internal static async Task<int> RunParkArrivalsAsync(bool fenceInBatch, bool recovery = false)
    {
        const string fixture = "sqlite-park-arrivals";
        const string payload = """{"input":"retained"}""";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        var directory = Directory.CreateTempSubdirectory(fixture);
        try
        {
            var connection = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory.FullName, "park.db"), ForeignKeys = true, Pooling = false
            }.ToString();
            var observation = new ParkReadObservation();
            observation.Resume.TrySetResult();
            var rollback = new RecoverReleaseFailure();
            var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(connection).AddInterceptors(observation, rollback).Options;
            var definitions = new SqliteAdmissionDefinitions();
            KernelDb Open() => recovery ? KernelDb.ForSqliteParkRecoveryQualification(options, definitions)
                : KernelDb.ForSqliteParkArrivalsQualification(options, definitions);
            await using var db = Open();
            var partitions = recovery ? 2 : 1;
            await EfStore.InstallSqliteShippedAsync(db, partitions, token);
            await using (var shipped = KernelDb.ForSqliteRootShipped(options))
            {
                try
                {
                    await EfStore.InstallSqliteShippedAsync(shipped, partitions, token);
                    throw new InvalidOperationException("The shipped context attached to the Park arrivals qualification file.");
                }
                catch (StoreFormatException) { Console.WriteLine("ok the shipped context refuses the qualification format"); }
            }

            var scope = EmbeddedTenant.Bind(fixture);
            var admission = new SqliteRootAdmission(Open, TimeSpan.FromHours(1), definitions);
            var cursor = await SqliteRootAdmission.ReadSelectionCursorAsync(db, token);
            var store = new EfStore(Open, postgres: false, fenceInBatch) { CommitSql = CommitSql.Owned };
            await store.EnableRootJobsAsync(token);
            var reads = new GeneratedRootReads(store, cursor, 8, provider: "sqlite");
            var executor = new SqliteArrivalActor(fixture);
            var adapter = new ActorAdapter(new EfStore.RootHeaders(store.BindSqliteWorkerNode(executor.Withholds)), executor,
                rootJobHandle: store.RootJobHandle, rootTimedJobClock: store.ReadRootTimedJobPublicationTimeAsync, rootHeldReplies: true);
            var actor = (await admission.StartAsync(scope.Tenant, "start", new(fixture, "{}"), null, null, token)).Actor!.Value;
            var requests = new Requests();
            HeldRootRequest Request(string id) => new(scope.Tenant, "caller",
                requests.OpenBound(id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000, scope.Tenant, null), false, null);
            var pending = Request("held-before-park");
            EfStore.RootImage parked;
            long epoch;
            await using (var owner = await OwnAsync("first"))
            {
                await DriveAsync(owner, actor, row => row is { Revision: 1, Status: InstanceStatus.Running });
                Require(await admission.SendAsync(scope.Tenant, actor, "fail", "fail", null, null, null, token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "the candidate admits a faulting input");
                Require(await admission.SendRequestAsync(pending, actor.Id, "held", null, token) == HeldRootAdmission.Accepted,
                    "the candidate admits the held call before the Park");
                parked = await DriveAsync(owner, actor, row => row is { Revision: 2, Status: InstanceStatus.Faulted });
                epoch = owner.Epoch;
            }

            var oldView = await reads.GetAsync(scope, actor, token);
            Require(oldView is { Park.At: > 0, Selection: not null }, "the fault opens a selectable Park in the qualification file");
            Require(await admission.SendRequestAsync(Request("held-after-park"), actor.Id, "held", null, token) == HeldRootAdmission.Refused,
                "the qualification profile refuses fresh held calls during the Park");
            Require(await admission.SendAsync(scope.Tenant, actor, "arrival", "ordinary", payload, null, null, token) is
                { Outcome: Contract.AdmissionOutcome.Accepted } &&
                await admission.SendAsync(scope.Tenant, actor, "arrival", "ordinary", payload, null, null, token) is
                { Outcome: Contract.AdmissionOutcome.Duplicate },
                "the SQLite candidate admits a one-way Park arrival and preserves its receipt");
            await using var successor = await OwnAsync("successor");
            var kept = await DriveAsync(successor, actor, row => row is { Revision: 3, Status: InstanceStatus.Faulted });
            var actorPartition = new InstanceRef(kept.Actor.Id).Partition;
            await ExpectFoundationFailureAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "FoundationStateWire" = CAST('different' AS BLOB),
                    "Revision" = 4, "FoundationWriteRevision" = 4
                WHERE "Id" = {kept.Actor.Id} AND "Partition" = {actorPartition}
                """, token), "an idle Park continuation cannot rewrite the saved state wire");
            var view = (await reads.GetAsync(scope, actor, token))!;
            var inbox = await reads.InboxAsync(scope, actor, cancellationToken: token);
            Require(observation.Plan.Any(step => step.Contains("IX_FoundationRootInbox_Inspectable", StringComparison.Ordinal) &&
                step.Contains("Target=?", StringComparison.Ordinal)),
                "the arrival inbox uses its actor-scoped partial index with both kept reasons");
            var heldInput = await db.Messages.AsNoTracking().SingleAsync(row => row.Target == parked.Actor.Id && row.Kind == "held", token);
            Require(successor.Epoch > epoch && kept.State == parked.State && view.Park == oldView!.Park && executor.OrdinaryTurns == 0 &&
                PostgresRootSelection.OpenPark(cursor, view.Selection, scope.Tenant, actor.Id) == new FoundationParkMark(2, view.Park!.At) &&
                inbox.Items.Where(input => input.Id != "held-before-park").ToArray() is
                    [{ Id: "fail", State: Contract.DeliveryState.Consumed }, { Id: "arrival", State: Contract.DeliveryState.Arrived }] &&
                heldInput is { Done: false, InboxScope: InboxKey.FoundationOccurrenceScope, KeepUntil: > 0 } && heldInput.ReplyTo == pending.Address &&
                await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {parked.Actor.Id} AND "Kind" = 'ordinary'
                        AND "Done" AND "FoundationKeptBy" = {(short)ParkKeepReason.Arrived} AND "Payload" = {payload}) AS "Value"
                    """).SingleAsync(token), "the replacement owner keeps the exact arrival under the original Park without invoking the actor");
            if (recovery)
            {
                Require(successor.ClaimedPartitions().Order().SequenceEqual(new[] { 0, 1 }),
                    "SQLite Recover polls with an owned set of two partitions");
                Require(await admission.OperateAsync(scope.Tenant, actor, "recover", Kinds.Recover, view.Selection!, null, null, token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "the SQLite recovery candidate admits an immediate selected Recover");
                rollback.Armed = true;
                while (!rollback.Observed && successor.Owns)
                {
                    await successor.WokenAsync(null, token);
                    await successor.RunAsync(token);
                    await Task.Delay(15, token);
                }
                Require(rollback.Observed, "the fixture withholds one release write after the Recover checkpoint update");
                Require((await reads.GetAsync(scope, actor, token))?.Park == view.Park &&
                    (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, token))?.Revision == kept.Revision,
                    "a missing release write rolls back the Recover checkpoint and projection");
                Require(await db.Database.SqlQuery<bool>($"""
                        SELECT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {parked.Actor.Id} AND "Kind" = 'ordinary'
                            AND "Done" AND "FoundationKeptBy" = {(short)ParkKeepReason.Arrived} AND "Payload" = {payload})
                        AS "Value"
                        """).SingleAsync(token), "a missing release write preserves the retained arrival");
                Require(await db.Database.SqlQuery<bool>($"""
                        SELECT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {parked.Actor.Id} AND "Kind" = {Kinds.Recover}
                            AND NOT "Done") AS "Value"
                        """).SingleAsync(token), "a missing release write rolls back the command settlement");
                Require((await store.ReadJournalOfAsync(actorPartition, kept.Actor.Id, 0, token)).All(entry => entry.Revision <= kept.Revision),
                    "a missing release write leaves no new journal entry");
                await successor.ReleaseAsync(token);
                await db.Nodes.Where(node => node.Id == "successor")
                    .ExecuteUpdateAsync(update => update.SetProperty(node => node.LeaseUntil, 0L), token);
                await using var recoveryOwner = await OwnAsync("recovering");
                var recovered = await DriveAsync(recoveryOwner, actor, row => row is { Revision: >= 6, Status: InstanceStatus.Running });
                Require(recoveryOwner.Owns && recovered is { ParkAt: null, TerminalRevision: null } && executor.Order is ["ordinary", "held"] &&
                    await db.Database.SqlQuery<bool>($"""
                        SELECT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {parked.Actor.Id} AND "Kind" = 'ordinary'
                            AND "Done" AND "FoundationReleasedBy" = 1 AND "FoundationKeptBy" IS NULL)
                        AND EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {parked.Actor.Id} AND "Kind" = 'fail'
                            AND "Done" AND "Payload" IS NULL AND "Start" IS NULL AND "FoundationKeptBy" IS NULL) AS "Value"
                        """).SingleAsync(token), "the SQLite recovery candidate delivers the released cohort before the older held call");
                Require(await admission.OperateAsync(scope.Tenant, actor, "stale", Kinds.Recover, view.Selection!, null, null, token) is
                    { Outcome: Contract.AdmissionOutcome.Refused }, "the old Park selection cannot recover the running root again");
                var oversized = (await admission.StartAsync(scope.Tenant, "oversized", new(fixture, "{}"), null, null, token)).Actor!.Value;
                await DriveAsync(recoveryOwner, oversized, row => row is { Revision: 1, Status: InstanceStatus.Running });
                Require(await admission.SendAsync(scope.Tenant, oversized, "large-fault", "fail", null, null, null, token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "the one-page refusal fixture admits a faulting input");
                var beforeRefusal = await DriveAsync(recoveryOwner, oversized, row => row is { Revision: 2, Status: InstanceStatus.Faulted });
                var large = Json.Write(new string('x', ParkRecoveryCredit.LimitBytes / 2 + 1_024));
                foreach (var id in new[] { "large-a", "large-b" })
                {
                    Require(await admission.SendAsync(scope.Tenant, oversized, id, "ordinary", large, null, null, token) is
                        { Outcome: Contract.AdmissionOutcome.Accepted }, "the Park admits an arrival that fits by itself");
                    var expected = beforeRefusal.Revision + 1;
                    beforeRefusal = await DriveAsync(recoveryOwner, oversized, row => row.Revision == expected && row.Status == InstanceStatus.Faulted);
                }
                var selection = (await reads.GetAsync(scope, oversized, token))!.Selection!;
                Require(await admission.OperateAsync(scope.Tenant, oversized, "large-recover", Kinds.Recover, selection, null, null, token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "admission accepts a valid Recover selection for the oversized Park");
                var refused = await DriveAsync(recoveryOwner, oversized, row => row is { Revision: 5, Status: InstanceStatus.Faulted });
                var journal = (await store.ReadJournalOfAsync(new InstanceRef(refused.Actor.Id).Partition, refused.Actor.Id, 0, token))
                    .Single(entry => entry.Revision == refused.Revision);
                Require(refused.ParkAt == beforeRefusal.ParkAt && refused.State == beforeRefusal.State && executor.OrdinaryTurns == 1 &&
                    journal.Entries.Contains("\"Reason\":\"page\"", StringComparison.Ordinal) &&
                    await db.Database.SqlQuery<int>($"""
                        SELECT count(*) AS "Value" FROM "Messages" WHERE "Target" = {refused.Actor.Id} AND "Done"
                            AND "FoundationKeptBy" = {(short)ParkKeepReason.Arrived} AND "FoundationReleasedBy" IS NULL AND "Payload" = {large}
                        """).SingleAsync(token) == 2, "Recover refuses a second page and preserves both retained envelopes and the checkpoint");
                await CheckReparkFifoAsync(recoveryOwner);
                var guardFile = new SqliteConnectionStringBuilder(connection) { DataSource = Path.Combine(directory.FullName, "guard.db") };
                var guardOptions = new DbContextOptionsBuilder<KernelDb>().UseSqlite(guardFile.ConnectionString).Options;
                await using var guards = KernelDb.ForSqliteParkRecoveryQualification(guardOptions);
                await EfStore.InstallSqliteShippedAsync(guards, 1, token);
                await SqliteShippedInstallChecks.CheckGuardsAsync(guards, token);
                return 0;
            }
            Require(await admission.OperateAsync(scope.Tenant, actor, "end", Kinds.Terminate, view.Selection!, null, null, token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the original Park selection still admits a Terminate after the arrival");
            var ended = await DriveAsync(successor, actor, row => row is { Revision: >= 4, Status: InstanceStatus.Cancelled });
            Require(ended is { ParkAt: null, TerminalRevision: 4 } && executor.OrdinaryTurns == 0 &&
                (await reads.InboxAsync(scope, actor, cancellationToken: token)).Items.All(input => input.Id == "held-before-park"),
                "selected Terminate clears the consumed fault and kept arrival without applying either input");
            while ((await reads.InboxAsync(scope, actor, cancellationToken: token)).Items.Count != 0)
            {
                await successor.WokenAsync(null, token);
                await successor.RunAsync(token);
                await Task.Delay(15, token);
            }
            var settled = await db.Messages.AsNoTracking().SingleAsync(row => row.InboxScope == heldInput.InboxScope &&
                row.Id == heldInput.Id && row.Target == heldInput.Target && row.Partition == heldInput.Partition, token);
            Require(successor.Owns && executor.HeldTurns == 0 && settled.KeepUntil == heldInput.KeepUntil &&
                settled is { Done: true, Start: null, Payload: null, ReplyTo: null },
                "the same owner settles the preadmitted held input after the Park ends without invoking the actor");
            return 0;

            async Task CheckReparkFifoAsync(Worker owner)
            {
                var root = (await admission.StartAsync(scope.Tenant, "repark", new(fixture, "{}"), null, null, token)).Actor!.Value;
                var running = await DriveAsync(owner, root, row => row is { Revision: 1, Status: InstanceStatus.Running });
                var due = running.AsOf + 120_000;
                Require(await admission.SendAsync(scope.Tenant, root, "older-future", "older-future", null, due, null, token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted } &&
                    await admission.SendAsync(scope.Tenant, root, "first-fault", "fail", null, null, null, token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "the re-Park fixture admits an older future input before the first fault");
                await DriveAsync(owner, root, row => row is { Revision: 2, Status: InstanceStatus.Faulted });
                Require(await admission.SendAsync(scope.Tenant, root, "released-fault", "repark-fault", null, null, null, token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted } &&
                    await admission.SendAsync(scope.Tenant, root, "later-release", "later-release", null, null, null, token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "the first Park admits two arrivals in release order");
                await DriveAsync(owner, root, row => row is { Revision: 4, Status: InstanceStatus.Faulted });
                var selection = (await reads.GetAsync(scope, root, token))!.Selection!;
                Require(await admission.OperateAsync(scope.Tenant, root, "repark-recover", Kinds.Recover, selection, null, null, token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "the first Park admits a Recover");
                var repark = await DriveAsync(owner, root, row => row is { Revision: 6, Status: InstanceStatus.Faulted });
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "Due" = 0 WHERE "Target" = {repark.Actor.Id} AND "Kind" = 'older-future' AND NOT "Done"
                    """, token);
                var fences = await db.Partitions.AsNoTracking().OrderBy(row => row.Id)
                    .Select(row => new PartitionFence(row.Id, row.Epoch, row.Position)).ToArrayAsync(token);
                Require((await store.PollAsync(fences, TimeSpan.Zero, 1, token)).Messages is [{ Message.Kind: "older-future" }],
                    "the new Park offers the older due input ahead of the earlier generation's pending release");
                await DriveAsync(owner, root, row => row is { Revision: 7, Status: InstanceStatus.Faulted });
                Require(await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {repark.Actor.Id} AND "Kind" = 'older-future'
                        AND "Done" AND "FoundationKeptBy" = {(short)ParkKeepReason.Arrived}) AS "Value"
                    """).SingleAsync(token), "the fenced writer retains the older due input under the new Park");
            }

            async Task<Worker> OwnAsync(string name)
            {
                var worker = new Worker(name, Enumerable.Range(0, partitions).ToArray(), store, adapter, new WorkerOptions
                {
                    Lease = TimeSpan.FromSeconds(30), Partitions = partitions, PassTurnBudget = 1,
                    PollLimit = recovery ? 1 : new WorkerOptions().PollLimit
                });
                if (await new DurableHost([worker]).ActivateAsync(0, token) == worker) return worker;
                await worker.DisposeAsync();
                throw new InvalidOperationException("The SQLite Park fixture could not take the partition.");
            }

            async Task<EfStore.RootImage> DriveAsync(Worker worker, Contract.ActorRef reference, Func<EfStore.RootImage, bool> ready)
            {
                while (true)
                {
                    if (await store.ReadFirstRootAsync(scope.Tenant, reference.Id, token) is { } row && ready(row)) return row;
                    await worker.WokenAsync(null, token);
                    await worker.RunAsync(token);
                    if (!worker.Owns) throw new InvalidOperationException("The SQLite Park fixture lost ownership.");
                    await Task.Delay(15, token);
                }
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class RecoverReleaseFailure : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Observed { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed || !command.CommandText.Contains("""SET "Done" = 0, "FoundationKeptBy" = NULL, "Seen" = 0""",
                StringComparison.Ordinal)) return ValueTask.FromResult(result);
            Armed = false;
            Observed = true;
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
        }
    }

    private sealed class SqliteArrivalActor(string type) : Contract.IActorType
    {
        internal int OrdinaryTurns { get; private set; }
        internal int HeldTurns { get; private set; }
        internal List<string> Order { get; } = [];
        string Contract.IActorType.Type => type;
        public bool Withholds(Contract.ActorHeader header) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The Park arrival fixture does not start jobs.");

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Any(message => message.Kind is "fail" or "repark-fault")) throw new InvalidOperationException("park arrival fixture fault");
            OrdinaryTurns += turn.Inbox.Count(message => message.Kind == "ordinary");
            HeldTurns += turn.Inbox.Count(message => message.Kind == "held");
            Order.AddRange(turn.Inbox.Select(message => message.Kind).Where(kind => kind is "ordinary" or "held"));
            turn.State = """{"state":"last committed"}""";
            return default;
        }
    }
}
