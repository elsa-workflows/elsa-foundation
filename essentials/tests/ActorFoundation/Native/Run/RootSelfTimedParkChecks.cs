using Elsa.ActorFoundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class RootSelfTimedParkChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool fenceInBatch, bool rearm = false)
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
            DbContextOptions<KernelDb>? shadowOptions = null;
            KernelDb Open() => (postgres, rearm) switch
            {
                (true, false) => KernelDb.ForPostgresRootSelfTimedParkQualification(options.Options),
                (true, true) => KernelDb.ForPostgresRootTimerRearmQualification(shadowOptions ?? options.Options),
                (false, false) => KernelDb.ForSqliteRootSelfTimedParkQualification(options.Options, definitions),
                _ => KernelDb.ForSqliteRootTimerRearmQualification(options.Options, definitions)
            };
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
                ? new List<Func<KernelDb>> { () => KernelDb.ForPostgresRootShipped(options.Options),
                    () => KernelDb.ForPostgresRootSelfTimedQualification(options.Options) }
                : [() => KernelDb.ForSqliteRootShipped(options.Options),
                    () => KernelDb.ForSqliteRootSelfTimedQualification(options.Options, definitions),
                    () => KernelDb.ForSqliteParkRecoveryQualification(options.Options, definitions),
                    () => KernelDb.ForSqliteParkArrivalsQualification(options.Options, definitions)];
            if (rearm)
                priorProfiles.Add(postgres
                    ? () => KernelDb.ForPostgresRootSelfTimedParkQualification(options.Options)
                    : () => KernelDb.ForSqliteRootSelfTimedParkQualification(options.Options, definitions));
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
                    Console.WriteLine($"ok {prior.RequiredFormat} refuses {db.RequiredFormat}");
                }
            }

            await store.EnableRootSelfSendsAsync(token);
            await store.EnableRootJobsAsync(token);
            await store.EnableRootChildrenAsync(token);
            var probe = new Probe(rearm);
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
            PhysicalTimer[] originals = [];
            long firstEpoch;
            await using (var owner = await OwnAsync("keeper"))
            {
                var starting = await DriveAsync(owner, row => row.Status == InstanceStatus.Running && probe.Invocations == 1);
                if (rearm)
                {
                    var recipes = DurableKeyJson.Read<Contract.TimerSpec[]>(starting.State);
                    var origin = CreationProtocol.Origin(starting.Actor.Id, 1);
                    originals = (await TimersAsync(starting.Actor.Id)).Where(row => recipes.Skip(3)
                        .Any(recipe => row.Id == origin + "#" + recipe.Id)).ToArray();
                    Check(originals.Length == 2, "the committed checkpoint retains both timers' original recipes");
                    Check(await SendAsync("drop", "drop", null, null) is { Outcome: Contract.AdmissionOutcome.Accepted },
                        "the fixture admits a turn that recalls a timer but retains its wait");
                    await DriveAsync(owner, row => row.Revision > starting.Revision);
                    var foreign = CreationProtocol.Origin(starting.Actor.Id + "-foreign", 1);
                    probe.ForeignTimer = FoundationSelfSend.Issue(store.RootSelfIssuer!.Handle, foreign, 1, 0, recipes[3].Due);
                }
                parked = await DriveAsync(owner, row => row.Status == InstanceStatus.Faulted);
                Check(await SendAsync("outside", "outside", null, null) is { Outcome: Contract.AdmissionOutcome.Accepted },
                    "the parked root accepts a direct arrival beside its private timer");
                parked = await DriveAsync(owner, row => row.Status == InstanceStatus.Faulted && row.Revision >= parked.Revision + 2);
                before = await TimersAsync(parked.Actor.Id);
                Check(before.Count(row => row.KeptBy == (short)ParkKeepReason.Consumed && row.Done) == 1 &&
                      before.Count(row => row.KeptBy == (short)ParkKeepReason.Arrived && row.Done) == 1 &&
                      before.Count(row => row.KeptBy is null && !row.Done) == (rearm ? 2 : 1) &&
                      probe.Arrivals.Count == 0 && probe.Invocations == 1 &&
                      JobRegistry.Read(parked.Jobs) is [{ Generation: 0 }],
                    "Park keeps the poison and matured timer with distinct reasons, and preserves the future timer and live job");
                firstEpoch = owner.Epoch;
            }

            if (rearm)
            {
                // Install the ordinary Done footprint while no owner can have the timer buffered.
                var settled = originals[1].Id;
                Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "Done" = {true}
                    WHERE "Target" = {parked.Actor.Id} AND "Id" = {settled} AND "InboxScope" = {EfStore.RootSelfScope}
                        AND "EventMatch" IS NULL AND NOT "Done"
                    """, token) == 1, "the fixture leaves an ordinary Done timer in the store");
                before = await TimersAsync(parked.Actor.Id);
                Check(before.Single(row => row.Id == settled) is { Done: true, KeptBy: null } &&
                      before.All(row => row.Id != originals[0].Id),
                    "the Park includes both a missing and an ordinarily Done timer in its saved recipes");
                if (postgres)
                {
                    var source = await db.Database.SqlQueryRaw<string>("""SELECT pg_catalog.current_schema() AS "Value" """).SingleAsync(token);
                    await db.Database.ExecuteSqlRawAsync("""
                        CREATE SCHEMA timer_rearm_shadow;
                        CREATE VIEW timer_rearm_shadow."FoundationRootMessages" AS
                            SELECT * FROM "FoundationRootMessages" WHERE false;
                        """, token);
                    var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
                    {
                        SearchPath = "timer_rearm_shadow," + new NpgsqlCommandBuilder().QuoteIdentifier(source)
                    }.ConnectionString;
                    try
                    {
                        shadowOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
                        var timer = DurableKeyJson.Read<Contract.TimerSpec[]>(parked.State!)[4];
                        var recipe = new ParkTimerRecipe(timer.Id, timer.Kind, "changed", timer.Due);
                        Check(!await store.RootSelfIssuer!.CheckTimerRearms!(parked.Actor.Id, 0, [recipe], token),
                            "the recovery preflight checks the Done body in the marker schema despite a shadow private view");
                    }
                    finally
                    {
                        shadowOptions = null;
                        await db.Database.ExecuteSqlRawAsync("DROP SCHEMA timer_rearm_shadow CASCADE", token);
                    }
                }
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
                "an invalid recipe leaves every timer envelope, the Park and the job registration intact");
            probe.ChangeRecipe = false;
            if (rearm)
            {
                probe.ChangeDonePayload = true;
                Check(await RecoverAsync("changed-done-body", selection) is { Outcome: Contract.AdmissionOutcome.Accepted },
                    "the provider admits a Recover carrying an authentic timer handle with a changed saved body");
                var mismatch = await DriveAsync(successor, row => row.Revision > refused.Revision);
                Check(mismatch.Status == InstanceStatus.Faulted && mismatch.ParkAt == parked.ParkAt &&
                      mismatch.Jobs == parked.Jobs && (await TimersAsync(parked.Actor.Id)).SequenceEqual(before) &&
                      (await store.ReadJournalOfAsync(0, parked.Actor.Id, 0, token))[^1].Entries.Contains("\"Reason\":\"plan\"", StringComparison.Ordinal),
                    "a Done body mismatch settles as a plan refusal under the current owner");
                probe.ChangeDonePayload = false;
            }
            Check(await RecoverAsync("recover", selection) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "the same Park accepts a fresh exact Recover");
            var recovered = await DriveAsync(successor, row => row.Status == InstanceStatus.Running);
            var released = await TimersAsync(parked.Actor.Id);
            Check(recovered.Jobs == parked.Jobs && recovered.NextJobGeneration == parked.NextJobGeneration &&
                  released.Length == (rearm ? 4 : 2) &&
                  released.Any(row => row == before.Single(timer => !timer.Done)) &&
                  released.Any(row => row == before.Single(timer => timer.KeptBy == (short)ParkKeepReason.Arrived)
                        with { Done = false, KeptBy = null, Released = 1 }) && probe.PoisonCalls == 1,
                "Recover restores the matured timer with its original sequence and due time, leaves the future timer and clears the poison");
            if (rearm)
                Check(originals.All(original => released.Single(row => row.Id == original.Id) is { Done: false, KeptBy: null, Released: null } row &&
                            row.Due == original.Due && row.Seq > before.Max(timer => timer.Seq)),
                    "Recover re-arms both exact keys at their saved due times using fresh sequences behind the released cohort");
            var expectedArrivals = rearm ? 4 : 2;
            await DriveAsync(successor, _ => probe.Arrivals.Count == expectedArrivals && probe.Invocations == 2);
            if (rearm)
                Check(probe.Arrivals.Take(2).ToHashSet(StringComparer.Ordinal).SetEquals(["arrival", "outside"]) &&
                      probe.Arrivals.Skip(2).SequenceEqual(["missing", "done"]),
                    "the released cohort reaches the successor before either newly re-armed timer");
            probe.Release.TrySetResult();
            var finished = await DriveAsync(successor, row => row.Status == InstanceStatus.Finished);
            Check(probe.Arrivals.Count == expectedArrivals && probe.PoisonCalls == 1 && probe.Results == 1 &&
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

    private sealed class Probe(bool rearm) : Contract.IActorType
    {
        internal const string ActorType = "self-timed-park";
        private const string ExactPayload = "with null\0 and unmatched \ud800";
        private volatile int _invocations;
        internal bool ChangeRecipe { get; set; }
        internal string? ForeignTimer { get; set; }
        internal bool ChangeDonePayload { get; set; }
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<string> Arrivals { get; } = [];
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
            if (ChangeRecipe)
            {
                if (ForeignTimer is { } foreign) timers[3] = timers[3] with { Id = foreign };
                else timers[0] = timers[0] with { Due = timers[0].Due + 1 };
            }
            if (ChangeDonePayload) timers[4] = timers[4] with { Payload = "changed" };
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
                        var timers = new List<Contract.TimerSpec>
                        {
                            Timer("poison", rearm ? 2500 : 850), Timer("arrival", rearm ? 4000 : 1800), Timer("future", 30_000)
                        };
                        if (rearm) timers.AddRange([Timer("missing", 3000), Timer("done", 6000)]);
                        turn.State = DurableKeyJson.Write(timers);
                        break;
                    case "drop":
                        turn.Recall(DurableKeyJson.Read<Contract.TimerSpec[]>(turn.State!)[3].Id);
                        break;
                    case "poison":
                        PoisonCalls++;
                        throw new InvalidOperationException("the timer poisoned its turn");
                    case "arrival":
                    case "missing":
                    case "done":
                        Check(input.Payload == ExactPayload, "the restored timer retains its exact split or escaped payload");
                        Arrivals.Add(input.Kind);
                        break;
                    case "outside":
                        Arrivals.Add(input.Kind);
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
