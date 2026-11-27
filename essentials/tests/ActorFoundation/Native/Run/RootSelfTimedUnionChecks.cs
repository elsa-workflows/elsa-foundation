using System.Reflection;
using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class RootSelfTimedUnionChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        var token = budget.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("root-self-timed-");
        try
        {
            var options = new DbContextOptionsBuilder<KernelDb>();
            if (postgres)
                options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                    ?? throw new InvalidOperationException("Use a dedicated PostgreSQL qualification database."));
            else
                options.UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(directory!.FullName, "union.db"), ForeignKeys = true
                }.ConnectionString);
            var definitions = new SqliteAdmissionDefinitions();
            KernelDb Open() => postgres ? KernelDb.ForPostgresRootSelfTimedQualification(options.Options)
                : KernelDb.ForSqliteRootSelfTimedQualification(options.Options, definitions);
            var store = new EfStore(Open, postgres, fenceInBatch) { CommitSql = CommitSql.Owned };
            await using var db = Open();
            const string tenant = "mixed-sends";
            var postgresAdmission = new PostgresRootAdmission(Open, TimeSpan.Zero);
            var sqliteAdmission = new SqliteRootAdmission(Open, TimeSpan.Zero, definitions);
            if (postgres)
            {
                await store.CreateAsync(1);
                await postgresAdmission.InstallAsync(token);
            }
            else
                await EfStore.InstallSqliteShippedAsync(db, 1, token);

            var actor = new Probe();
            await store.EnableRootSelfSendsAsync(token);
            await store.EnableRootJobsAsync(token);
            var rootScope = postgres ? store.BindWorkerNode(actor.Withholds) : store.BindSqliteWorkerNode(actor.Withholds);
            var adapter = new ActorAdapter(new EfStore.RootHeaders(rootScope), actor, store.RootSelfIssuer,
                rootJobHandle: store.RootJobHandle, rootTimedJobClock: store.ReadRootTimedJobPublicationTimeAsync);

            var storeProxy = DispatchProxy.Create<IKernelStore, PostgresRootParkOpeningChecks.ObservedCommit>();
            var observed = (PostgresRootParkOpeningChecks.ObservedCommit)(object)storeProxy;
            observed.Store = store;
            var negativeVerified = false;
            observed.Write = async (batch, ct) =>
            {
                if (!negativeVerified && batch.Turns.SingleOrDefault(turn => turn.Sent.Any(message => message.Kind == "immediate")) is { } mixed)
                {
                    var copied = mixed with
                    {
                        Sent = mixed.Sent.Select(message => message.Kind == "immediate" ? message with { } : message).ToArray()
                    };
                    try
                    {
                        await store.CommitAsync(batch with { Turns = [copied] }, ct);
                        throw new InvalidOperationException("The qualified writer accepted a copied self envelope without its authored ordinal.");
                    }
                    catch (NotSupportedException)
                    {
                        negativeVerified = true;
                    }
                }
                return await store.CommitAsync(batch, ct);
            };

            await using var worker = new Worker("mixed-sends", 0, storeProxy, adapter, new WorkerOptions
            {
                Lease = TimeSpan.FromSeconds(60), PassTurnBudget = 1
            });
            Check(await new DurableHost([worker]).ActivateAsync(0, token) == worker, "the qualified owner takes the partition");
            var first = await StartAsync("first");
            var second = await StartAsync("second");
            await DriveAsync(() => Task.FromResult(actor.Invocations == 2 && actor.Count("immediate") == 2));

            var firstImage = (await store.ReadFirstRootAsync(tenant, first, token))!;
            var secondImage = (await store.ReadFirstRootAsync(tenant, second, token))!;
            foreach (var image in new[] { firstImage, secondImage })
            {
                var rows = await RowsAsync(image.Actor.Id);
                Check(rows.Count(row => row.InboxScope == EfStore.RootTimedJobScope) == 2 &&
                    rows.Count(row => row.InboxScope == EfStore.RootSelfScope) == 2 &&
                    JobRegistry.Read(image.Jobs) is [{ Generation: 0 }],
                    "each mixed turn keeps its own pending deadline, ticket and two self timers");
            }
            Check(negativeVerified, "an otherwise valid root input cannot be forged by copying a self envelope");
            actor.Release.TrySetResult();
            await DriveAsync(async () => (await store.ReadFirstRootAsync(tenant, first, token))?.Status == InstanceStatus.Finished &&
                (await store.ReadFirstRootAsync(tenant, second, token))?.Status == InstanceStatus.Finished);
            Check(actor.Count(Contract.Kinds.JobFinished) == 2 && actor.Count("result-step") == 2 && actor.Count("timer") == 2 &&
                actor.Count("discarded") == 0 && actor.Count("recalled") == 0 &&
                (await RowsAsync(firstImage.Actor.Id)).Length == 0 && (await RowsAsync(secondImage.Actor.Id)).Length == 0,
                "each root applies one durable result and timer, consumes its result self-send, and settles every private row");
            return 0;

            async Task<string> StartAsync(string id) => postgres
                ? (await postgresAdmission.AdmitAsync(tenant, id, new StartSpec(Probe.ActorType, id), cancellationToken: token)).Reference
                : (await sqliteAdmission.StartAsync(tenant, id, new StartSpec(Probe.ActorType, id), null, null, token)).Actor!.Value.Id;

            Task<MessageEntity[]> RowsAsync(string actorId) => db.Messages.AsNoTracking().Where(message => message.Target == actorId &&
                (message.InboxScope == EfStore.RootSelfScope || message.InboxScope == EfStore.RootTimedJobScope)).ToArrayAsync(token);

            async Task DriveAsync(Func<Task<bool>> ready)
            {
                while (!await ready())
                {
                    await worker.WokenAsync(null, token);
                    await worker.RunAsync(token);
                    if (!worker.Owns) throw new InvalidOperationException("The mixed-envelope owner lost its fence.");
                    if (!await ready()) await Task.Delay(20, token);
                }
            }
        }
        finally { directory?.Delete(recursive: true); }
    }

    private sealed class Probe : Contract.IActorType
    {
        internal const string ActorType = "root-self-timed";
        private const string SelfPayload = "contents\0\ud800";
        private readonly List<string> _seen = [];
        private readonly Dictionary<Contract.ActorRef, string> _recalls = [];
        private int _invocations;
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Invocations => Volatile.Read(ref _invocations);
        internal int Count(string kind) => _seen.Count(value => value == kind);
        public string Type => ActorType;
        public bool Withholds(Contract.ActorHeader header) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocations);
            await Release.Task.WaitAsync(cancellationToken);
            return "result";
        }

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var state = turn.State is { } existing ? Json.Read<int>(existing) : 0;
            var root = turn.Header.Ref;
            foreach (var input in turn.Inbox)
            {
                _seen.Add(input.Kind);
                switch (input.Kind)
                {
                    case Contract.Kinds.Start:
                        turn.Recall(turn.Send(root, "discarded", dueAt: long.MaxValue));
                        turn.StartJob("work", timeout: TimeSpan.FromSeconds(25));
                        turn.Send(root, "immediate", SelfPayload);
                        turn.Send(root, "timer", dueAt: turn.Now + 7_000);
                        _recalls.Add(root, turn.Send(root, "recalled", dueAt: long.MaxValue));
                        break;
                    case "immediate":
                        Check(input.Payload == SelfPayload, "root receives the exact encoded self payload");
                        state |= 1;
                        break;
                    case Contract.Kinds.JobFinished:
                        Check(Json.Read<Contract.JobFinished>(input.Payload!) is { Error: null, Result: "\"result\"" },
                            "root receives the recorded timed callback");
                        turn.Recall(_recalls[root]);
                        turn.Send(root, "result-step");
                        state |= 2;
                        break;
                    case "result-step":
                        state |= 4;
                        break;
                    case "timer":
                        state |= 8;
                        break;
                }
            }
            turn.State = Json.Write(state);
            if (state == 15) turn.End(Contract.ActorStatus.Finished);
            return ValueTask.CompletedTask;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("ok " + message);
    }
}
