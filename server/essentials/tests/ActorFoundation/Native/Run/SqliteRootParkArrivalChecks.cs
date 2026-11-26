using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    internal static async Task<int> RunParkArrivalsAsync(bool fenceInBatch)
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
            var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(connection).AddInterceptors(observation).Options;
            var definitions = new SqliteAdmissionDefinitions();
            KernelDb Open() => KernelDb.ForSqliteParkArrivalsQualification(options, definitions);
            await using var db = Open();
            await EfStore.InstallSqliteShippedAsync(db, 1, token);
            await using (var shipped = KernelDb.ForSqliteRootShipped(options))
            {
                try
                {
                    await EfStore.InstallSqliteShippedAsync(shipped, 1, token);
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
            await ExpectFoundationFailureAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "FoundationStateWire" = CAST('different' AS BLOB),
                    "Revision" = 4, "FoundationWriteRevision" = 4
                WHERE "Id" = {kept.Actor.Id} AND "Partition" = 0
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

            async Task<Worker> OwnAsync(string name)
            {
                var worker = new Worker(name, 0, store, adapter, new WorkerOptions
                {
                    Lease = TimeSpan.FromSeconds(30), PassTurnBudget = 1
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

    private sealed class SqliteArrivalActor(string type) : Contract.IActorType
    {
        internal int OrdinaryTurns { get; private set; }
        internal int HeldTurns { get; private set; }
        string Contract.IActorType.Type => type;
        public bool Withholds(Contract.ActorHeader header) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The Park arrival fixture does not start jobs.");

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Any(message => message.Kind == "fail")) throw new InvalidOperationException("park arrival fixture fault");
            OrdinaryTurns += turn.Inbox.Count(message => message.Kind == "ordinary");
            HeldTurns += turn.Inbox.Count(message => message.Kind == "held");
            turn.State = """{"state":"last committed"}""";
            return default;
        }
    }
}
