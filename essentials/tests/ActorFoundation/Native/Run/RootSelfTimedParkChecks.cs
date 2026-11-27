using Elsa.ActorFoundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class RootSelfTimedParkChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = budget.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("root-self-park-");
        try
        {
            var options = new DbContextOptionsBuilder<KernelDb>();
            if (postgres)
                options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                    ?? throw new InvalidOperationException("Use a dedicated PostgreSQL qualification database."));
            else
                options.UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(directory!.FullName, "park.db"), ForeignKeys = true
                }.ConnectionString);

            var definitions = new SqliteAdmissionDefinitions();
            KernelDb Open() => postgres ? KernelDb.ForPostgresRootSelfTimedParkQualification(options.Options)
                : KernelDb.ForSqliteRootSelfTimedParkQualification(options.Options, definitions);
            var store = new EfStore(Open, postgres, fenceInBatch) { CommitSql = CommitSql.Owned };
            await using var db = Open();
            var scope = EmbeddedTenant.Bind("mixed-park");
            var pg = new PostgresRootAdmission(Open, TimeSpan.FromMinutes(1));
            var sqlite = new SqliteRootAdmission(Open, TimeSpan.FromMinutes(1), definitions);
            var ingress = new PostgresRootIngress(pg, Probe.ActorType, authorizeRecover: (_, _) => true);
            if (postgres)
            {
                await store.CreateAsync(1);
                await pg.InstallAsync(token);
            }
            else await EfStore.InstallSqliteShippedAsync(db, 1, token);

            var priorProfiles = postgres
                ? new Func<KernelDb>[] { () => KernelDb.ForPostgresRootShipped(options.Options),
                    () => KernelDb.ForPostgresRootSelfTimedQualification(options.Options) }
                : [() => KernelDb.ForSqliteRootShipped(options.Options),
                    () => KernelDb.ForSqliteRootSelfTimedQualification(options.Options, definitions),
                    () => KernelDb.ForSqliteParkRecoveryQualification(options.Options, definitions),
                    () => KernelDb.ForSqliteParkArrivalsQualification(options.Options, definitions)];
            foreach (var openPrior in priorProfiles)
            {
                await using var prior = openPrior();
                try
                {
                    if (postgres) await EfStore.RequireStoreVersionsAsync(prior, token);
                    else await EfStore.InstallSqliteShippedAsync(prior, 1, token);
                    throw new InvalidOperationException("An earlier profile attached to the mixed Park qualification database.");
                }
                catch (StoreFormatException error) when (error.Message.StartsWith("Unsupported kernel store versions.", StringComparison.Ordinal))
                {
                    Console.WriteLine($"ok {prior.RequiredFormat} refuses the mixed Park qualification marker");
                }
            }

            await store.EnableRootSelfSendsAsync(token);
            await store.EnableRootJobsAsync(token);
            var probe = new Probe();
            var roots = postgres ? store.BindWorkerNode(probe.Withholds) : store.BindSqliteWorkerNode(probe.Withholds);
            var adapter = new ActorAdapter(new EfStore.RootHeaders(roots), probe, store.RootSelfIssuer,
                rootJobHandle: store.RootJobHandle, rootTimedJobClock: store.ReadRootTimedJobPublicationTimeAsync);
            var reads = postgres ? await store.OpenPreviewRootReadsAsync(cancellationToken: token)
                : new GeneratedRootReads(store, await SqliteRootAdmission.ReadSelectionCursorAsync(db, token), 8, "sqlite");
            var spec = new StartSpec(Probe.ActorType, "{}");
            var root = postgres ? new Contract.ActorRef((await pg.AdmitAsync(scope.Tenant, "start", spec, cancellationToken: token)).Reference)
                : (await sqlite.StartAsync(scope.Tenant, "start", spec, null, null, token)).Actor!.Value;

            EfStore.RootImage parked;
            PhysicalTimer[] before;
            long firstEpoch;
            await using (var owner = await OwnAsync("keeper"))
            {
                await DriveAsync(owner, row => row.Status == InstanceStatus.Running && probe.Invocations == 1);
                parked = await DriveAsync(owner, row => row.Status == InstanceStatus.Faulted);
                Check(await SendAsync("outside", "outside", null, null) is { Outcome: Contract.AdmissionOutcome.Accepted },
                    "the parked root accepts a direct arrival beside its private timer");
                parked = await DriveAsync(owner, row => row.Status == InstanceStatus.Faulted && row.Revision >= parked.Revision + 2);
                before = await TimersAsync(parked.Actor.Id);
                Check(before.Count(row => row.KeptBy == (short)ParkKeepReason.Consumed && row.Done) == 1 &&
                      before.Count(row => row.KeptBy == (short)ParkKeepReason.Arrived && row.Done) == 1 &&
                      before.Count(row => row.KeptBy is null && !row.Done) == 1 &&
                      probe.Arrivals == 0 && probe.Invocations == 1 &&
                      JobRegistry.Read(parked.Jobs) is [{ Generation: 0 }],
                    "Park keeps the poison and matured timer with distinct reasons, and preserves the future timer and live job");
                firstEpoch = owner.Epoch;
            }

            var parkedView = (await reads.GetAsync(scope, root, token))!;
            var selection = parkedView.Selection!;
            await using var successor = await OwnAsync("resumer");
            Check(successor.Epoch > firstEpoch, "the new owner takes the parked root at a newer epoch");
            probe.ChangeRecipe = true;
            Check(await RecoverAsync("mismatched", selection) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "the provider admits a Recover command before the owner compares the restored recipe");
            var refused = await DriveAsync(successor, row => row.Revision > parked.Revision);
            Check(refused.Status == InstanceStatus.Faulted && refused.ParkAt == parked.ParkAt &&
                  refused.Jobs == parked.Jobs && (await TimersAsync(parked.Actor.Id)).SequenceEqual(before) &&
                  (await store.ReadJournalOfAsync(0, parked.Actor.Id, 0, token))[^1].Entries.Contains("\"Reason\":\"plan\"", StringComparison.Ordinal),
                "a changed recipe leaves every timer envelope, the Park and the job registration intact");
            probe.ChangeRecipe = false;
            Check(await RecoverAsync("recover", selection) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "the same Park accepts a fresh exact Recover");
            var recovered = await DriveAsync(successor, row => row.Status == InstanceStatus.Running);
            var released = await TimersAsync(parked.Actor.Id);
            Check(recovered.Jobs == parked.Jobs && recovered.NextJobGeneration == parked.NextJobGeneration && released.Length == 2 &&
                  released.Any(row => row == before.Single(timer => !timer.Done)) &&
                  released.Any(row => row == before.Single(timer => timer.KeptBy == (short)ParkKeepReason.Arrived)
                        with { Done = false, KeptBy = null, Released = 1 }) && probe.PoisonCalls == 1,
                "Recover restores the matured timer with its original sequence and due time, leaves the future timer and clears the poison");
            await DriveAsync(successor, _ => probe.Arrivals == 2 && probe.Invocations == 2);
            probe.Release.TrySetResult();
            var finished = await DriveAsync(successor, row => row.Status == InstanceStatus.Finished);
            Check(probe.Arrivals == 2 && probe.PoisonCalls == 1 && probe.Results == 1 &&
                  (await TimersAsync(parked.Actor.Id)).Length == 0 &&
                  !await db.Messages.AnyAsync(row => row.Target == finished.Actor.Id && row.InboxScope == EfStore.RootTimedJobScope, token),
                "the successor delivers the mixed arrivals and one timed result, and recalls the remaining timer");
            return 0;

            async ValueTask<Contract.Admission> SendAsync(string id, string kind, string? payload, string? selected)
            {
                if (postgres)
                    return await ingress.SendAsync(scope, new Contract.Target.Ref(root), id, kind, payload, cancellationToken: token);
                return selected is null ? await sqlite.SendAsync(scope.Tenant, root, id, kind, payload, null, null, token)
                    : await sqlite.OperateAsync(scope.Tenant, root, id, kind, selected, null, null, token);
            }

            ValueTask<Contract.Admission> RecoverAsync(string id, string selected) =>
                SendAsync(id, Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(selected)), selected);

            Task<PhysicalTimer[]> TimersAsync(string actor) => db.Database.SqlQuery<PhysicalTimer>($"""
                SELECT "Id", "Seq", "Due", "Done", "FoundationKeptBy" AS "KeptBy", "FoundationReleasedBy" AS "Released"
                FROM "Messages" WHERE "Target" = {actor} AND "InboxScope" = {EfStore.RootSelfScope} ORDER BY "Seq"
                """).ToArrayAsync(token);

            async Task<Worker> OwnAsync(string name)
            {
                var worker = new Worker(name, 0, store, adapter, new WorkerOptions
                {
                    Lease = TimeSpan.FromSeconds(30), PassTurnBudget = 1, MaxAttempts = 1
                });
                Check(await new DurableHost([worker]).ActivateAsync(0, token) == worker, "the fixture takes its partition");
                return worker;
            }

            async Task<EfStore.RootImage> DriveAsync(Worker owner, Func<EfStore.RootImage, bool> ready)
            {
                while (true)
                {
                    if (await store.ReadFirstRootAsync(scope.Tenant, root.Id, token) is { } row && ready(row)) return row;
                    await owner.WokenAsync(null, token);
                    await owner.RunAsync(token);
                    if (!owner.Owns) throw new InvalidOperationException("The qualification owner lost its fence.");
                    await Task.Delay(10, token);
                }
            }
        }
        finally { directory?.Delete(recursive: true); }
    }

    private sealed record PhysicalTimer(string Id, long Seq, long Due, bool Done, short? KeptBy, long? Released);

    private sealed class Probe : Contract.IActorType
    {
        internal const string ActorType = "self-timed-park";
        private const string ExactPayload = "with null\0 and unmatched \ud800";
        private volatile int _invocations;
        internal bool ChangeRecipe { get; set; }
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Arrivals { get; private set; }
        internal int PoisonCalls { get; private set; }
        internal int Results { get; private set; }
        internal int Invocations => _invocations;
        public string Type => ActorType;
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;

        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state)
        {
            var timers = DurableKeyJson.Read<Contract.TimerSpec[]>(state);
            if (ChangeRecipe) timers[0] = timers[0] with { Due = timers[0].Due + 1 };
            return new(timers, [new("work", null)], []);
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocations);
            await Release.Task.WaitAsync(cancellationToken);
            return "result";
        }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox)
                switch (input.Kind)
                {
                    case Contract.Kinds.Start:
                        turn.StartJob("work", timeout: TimeSpan.FromSeconds(20));
                        turn.State = DurableKeyJson.Write(new[] { Timer("poison", 850), Timer("arrival", 1800), Timer("future", 30_000) });
                        break;
                    case "poison":
                        PoisonCalls++;
                        throw new InvalidOperationException("the timer poisoned its turn");
                    case "arrival":
                        Check(input.Payload == ExactPayload, "the restored timer retains its exact split or escaped payload");
                        Arrivals++;
                        break;
                    case "outside":
                        Arrivals++;
                        break;
                    case Contract.Kinds.JobFinished:
                        Results++;
                        turn.Recall(DurableKeyJson.Read<Contract.TimerSpec[]>(turn.State!)[2].Id);
                        turn.End(Contract.ActorStatus.Finished);
                        break;
                }
            return ValueTask.CompletedTask;

            Contract.TimerSpec Timer(string kind, long delay)
            {
                var due = turn.Now + delay;
                return new(turn.Send(turn.Header.Ref, kind, ExactPayload, due), kind, ExactPayload, due);
            }
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("ok " + message);
    }
}
