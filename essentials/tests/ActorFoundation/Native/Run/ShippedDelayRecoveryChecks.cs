using System.Collections.Concurrent;
using System.Globalization;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Kernel.Native.Run;

internal static class ShippedDelayRecoveryChecks
{
    private const string NativeDefinition = "authored-native-delay";
    private const string FlowDefinition = "authored-flowchart-delay";
    private const string Fault = "park-delay-probe";
    private const int NativeDelay = 10_000;
    private const int FlowDelay = 4_000;

    internal static async Task<int> RunAsync(bool postgres, bool inBatch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("shipped-delay");
        try
        {
            var path = Path.Combine(directory?.FullName ?? "", "delays.db");
            var options = new DbContextOptionsBuilder<KernelDb>();
            if (postgres)
                options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
                    throw new InvalidOperationException("Use an empty dedicated PostgreSQL database."));
            else options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ConnectionString);
            var definitions = new SqliteAdmissionDefinitions();
            KernelDb Open() => postgres ? PostgresPreviewHosting.CreateShippedContext(options.Options)
                : KernelDb.ForSqliteRootRuntime(options.Options, definitions);

            var cold = new EfStore(Open, postgres);
            if (postgres)
            {
                await using var database = Open();
                Check(await PostgresFixtureDatabase.IsEmptyAsync(database, token), "the fixture uses an empty PostgreSQL database");
                await cold.CreateAsync(1);
                await cold.InstallPreviewRootSchemaAsync(token);
            }
            else await SqliteEmbeddedHosting.InstallShippedAsync(path, token);

            var scope = EmbeddedTenant.Bind("shipped-delay");
            bool Authorize(Contract.BoundScope caller, Contract.ActorRef _, string kind) =>
                ReferenceEquals(caller, scope) && kind == Contract.Kinds.Recover;
            Contract.IIngress ingress;
            Contract.IActorReads reads;
            if (postgres)
            {
                var foundation = await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(Open, PublicActor.ActorType,
                    Authorize, cancellationToken: token);
                (ingress, reads) = (foundation, foundation);
            }
            else
            {
                ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(path, PublicActor.ActorType,
                    Authorize, cancellationToken: token);
                reads = SqliteEmbeddedHosting.CreateFoundationReads(path);
            }

            var capabilities = new KernelHostCapabilities(Schedules: false,
                Timers: postgres ? PostgresPreviewHosting.TakesTimers(Open) : SqliteEmbeddedHosting.TakesTimers,
                Children: false,
                SelfSends: postgres ? PostgresPreviewHosting.TakesSelfSends(Open) : SqliteEmbeddedHosting.TakesSelfSends,
                HttpWaits: false);
            var serving = new KernelServing(capabilities: capabilities);
            var native = Wf.Workflow(NativeDefinition, Wf.Sequence("root",
                Wf.Delay("wait", NativeDelay), Wf.Set("after-delay", "finished", true)));
            var flowchart = NativeExecutableMapper.Map(ExecutableOf(FlowDefinition, FlowDefinition,
                Node("flow", Types.Flowchart, structure: Structure("elsa.flowchart.structure", new
                {
                    startNodeId = "wait",
                    connections = new[] { new { source = new { nodeId = "wait", port = "Done" }, target = new { nodeId = "after-delay" } } }
                }),
                    slots: [Slot("Flowchart.Activities",
                        Node("wait", Types.Delay, [Literal("Duration", TimeSpan.FromMilliseconds(FlowDelay).ToString("c", CultureInfo.InvariantCulture), "TimeSpan")]),
                        Set("after-delay", "v-finished", Literal("value", true, "Boolean")))]),
                Variable("v-finished", "finished", false, "Boolean")), capabilities);
            await serving.ServeHostWorkflowAsync(native, token);
            await serving.ServeHostWorkflowAsync(flowchart, token);
            Check(capabilities is { Timers: true, SelfSends: true, Schedules: false, Children: false, HttpWaits: false },
                "the shipped capabilities serve Native and mapped Flowchart Delay with no schedule-actor dependency");

            var runtime = new WorkflowRuntime(ingress, reads);
            var timers = new ConcurrentQueue<(Contract.ActorRef Actor, string Id, long Now)>();
            var failures = new ConcurrentQueue<string>();
            var loop = new LoopOptions
            {
                BalanceEvery = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(30),
                Window = TimeSpan.FromMilliseconds(400)
            };
            await using var db = Open();
            var marker = await db.StoreVersions.AsNoTracking().SingleAsync(row => row.Id == StoreVersionEntity.MarkerId, token);
            Check(marker.Format == new StoreFormat(3, 27, 1), "the journey runs on the widened shipped marker");
            Contract.ActorRef nativeRoot, flowRoot;
            DelayWait nativeWait, flowWait;
            Contract.ActorView nativePark, flowPark;
            long originalEpoch;
            await using (var first = await StartOwnerAsync("delay-original"))
            {
                nativeRoot = await AdmitAsync(NativeDefinition);
                flowRoot = await AdmitAsync(FlowDefinition);
                nativeWait = await WaitingAsync(nativeRoot, NativeDelay);
                flowWait = await WaitingAsync(flowRoot, FlowDelay);
                originalEpoch = await EpochAsync();
                nativePark = await ParkAsync(nativeRoot);
                flowPark = await ParkAsync(flowRoot);
                Check(timers.IsEmpty && nativePark is { Header.Status: Contract.ActorStatus.Faulted, Selection: not null } &&
                    flowPark is { Header.Status: Contract.ActorStatus.Faulted, Selection: not null },
                    "both authored instances Park before the Delay fires");
                await first.StopAsync(token);
            }

            // Represent the missing-footprint recovery case while no owner has cached the timer.
            Check(await db.Messages.Where(row => row.Target == nativeWait.Actor && row.InboxScope == EfStore.RootSelfScope &&
                row.Id == nativeWait.PhysicalKey && !row.Done).ExecuteDeleteAsync(token) == 1,
                "the replacement owner will find the Native actor's saved timer missing");

            await using var successor = await StartOwnerAsync("delay-successor");
            Check(await RecoverAsync("recover-native", nativePark) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "selected Recover is admitted for the parked Native run");
            var rearmed = await UntilAsync(() => FindTimerAsync(nativeWait.Actor, nativeWait.PhysicalKey));
            Check(rearmed.Due == nativeWait.Due && rearmed.Sequence > nativeWait.Sequence && !rearmed.Done &&
                rearmed.KeptBy is null && await EpochAsync() > originalEpoch,
                "the replacement owner re-arms the exact Native timer at its original due time with a fresh sequence");

            var kept = await UntilAsync(async () => await FindTimerAsync(flowWait.Actor, flowWait.PhysicalKey)
                is { Done: true, KeptBy: (short)ParkKeepReason.Arrived } row ? row : null);
            Check(kept.Due == flowWait.Due && kept.Sequence == flowWait.Sequence,
                "the mapped Flowchart timer matures into Park with its original due time and sequence");
            var selectedFlow = await reads.GetAsync(scope, flowRoot, token);
            Check(selectedFlow is { Park: not null, Selection: not null } &&
                await RecoverAsync("recover-flowchart", selectedFlow) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "selected Recover is admitted for the parked Flowchart run");

            var nativeFinished = await UntilAsync(() => FinishedAsync(nativeRoot));
            var flowFinished = await UntilAsync(() => FinishedAsync(flowRoot));
            foreach (var (actor, wait, result) in new[] { (nativeRoot, nativeWait, nativeFinished), (flowRoot, flowWait, flowFinished) })
            {
                Check(Json.Read<NativeState>(result.State!).Variables["finished"]!.GetValue<bool>() &&
                    timers.Where(timer => timer.Actor == actor).ToArray() is [var timer] &&
                    timer.Id == wait.Key && timer.Now >= wait.Due &&
                    await FindTimerAsync(wait.Actor, wait.PhysicalKey) is not { Done: false },
                    "the recovered Delay runs once, at or after its original due time, and completes its authored continuation");
                var visible = await reads.GetAsync(scope, actor, token);
                Check(visible is { Park: null, Header.Status: Contract.ActorStatus.Finished },
                    "the public actor read shows a finished run with its Park cleared");
            }
            await successor.StopAsync(token);
            Check(failures.IsEmpty, "the retained provider owners stop cleanly");
            return 0;

            async Task<RuntimeNode> StartOwnerAsync(string node)
            {
                var model = new NativeExecutionModel(serving.Registry, new() { SelfSends = serving.Capabilities.SelfSends });
                var actor = new ParkProbe(new PublicActor([model]), timers);
                void Faulted(Exception error) => failures.Enqueue(error.Message);
                return postgres
                    ? await PostgresPreviewHosting.StartAsync(Open, node, actor, failures.Enqueue, loop, token, Faulted,
                        (IReplyTransport)PostgresPreviewHosting.CreateRequests(Open, node, PublicActor.ActorType), fenceInBatch: inBatch)
                    : await SqliteEmbeddedHosting.StartFoundationAsync(path, node, actor, failures.Enqueue, loop, inBatch,
                        token, Faulted, SqliteEmbeddedHosting.CreateRequests(path, node, PublicActor.ActorType));
            }

            async Task<Contract.ActorRef> AdmitAsync(string definition)
            {
                var receipt = await runtime.StartAsync(scope, definition, NativeExecutionModel.Start(definition), cancellationToken: token);
                Check(receipt is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the public ingress admits " + definition);
                return receipt.Actor!.Value;
            }

            async Task<DelayWait> WaitingAsync(Contract.ActorRef root, int duration) =>
                await UntilAsync(async () =>
                {
                    if (await cold.ReadFirstRootAsync(scope.Tenant, root.Id, token) is not { Status: InstanceStatus.Running } image ||
                        Json.Read<NativeState>(image.State!).Waits.Values.SingleOrDefault(wait => wait.Kind == "timer") is not { } wait)
                        return null;
                    var physical = CreationProtocol.Origin(image.Actor.Id, image.Actor.Creation) + "#" + wait.Key;
                    var timer = await FindTimerAsync(image.Actor.Id, physical);
                    Check(timer is { Done: false } && timer.Due == wait.Due && wait.Due - image.AsOf > 0 && wait.Due - image.AsOf <= duration,
                        "the authored Delay checkpoint and its private timer agree on the absolute deadline");
                    return new DelayWait(image.Actor.Id, physical, wait.Key, timer!.Due, timer.Sequence);
                });

            async Task<Contract.ActorView> ParkAsync(Contract.ActorRef actor)
            {
                Check(await ingress.SendAsync(scope, new Contract.Target.Ref(actor), "park", Fault, cancellationToken: token)
                    is { Outcome: Contract.AdmissionOutcome.Accepted }, "the fixture admits the faulting input");
                return await UntilAsync(async () => await reads.GetAsync(scope, actor, token) is { Park: not null } parked ? parked : null);
            }

            ValueTask<Contract.Admission> RecoverAsync(string operation, Contract.ActorView parked) =>
                ingress.SendAsync(scope, new Contract.Target.Ref(parked.Header.Ref), operation, Contract.Kinds.Recover,
                    Json.Write(new Contract.OperatorCommand(parked.Selection)), cancellationToken: token);

            async Task<EfStore.RootImage?> FinishedAsync(Contract.ActorRef actor) =>
                await cold.ReadFirstRootAsync(scope.Tenant, actor.Id, token) is { Status: InstanceStatus.Finished } row ? row : null;

            Task<long> EpochAsync() => db.Partitions.AsNoTracking().Select(row => row.Epoch).SingleAsync(token);

            Task<PhysicalTimer?> FindTimerAsync(string actor, string id) => db.Database.SqlQuery<PhysicalTimer>($"""
                SELECT "Due", "Seq" AS "Sequence", "Done", "FoundationKeptBy" AS "KeptBy"
                FROM "Messages" WHERE "Target" = {actor} AND "InboxScope" = {EfStore.RootSelfScope} AND "Id" = {id}
                """).SingleOrDefaultAsync(token);

            async Task<T> UntilAsync<T>(Func<Task<T?>> read) where T : class
            {
                while (true)
                {
                    if (await read() is { } value) return value;
                    if (failures.TryPeek(out var error)) throw new InvalidOperationException(error);
                    await Task.Delay(20, token);
                }
            }
        }
        finally { directory?.Delete(recursive: true); }
    }

    private sealed record DelayWait(string Actor, string PhysicalKey, string Key, long Due, long Sequence);
    private sealed record PhysicalTimer(long Due, long Sequence, bool Done, short? KeptBy);

    private sealed class ParkProbe(PublicActor actor, ConcurrentQueue<(Contract.ActorRef Actor, string Id, long Now)> timers) : Contract.IActorType
    {
        public string Type => actor.Type;
        public bool Withholds(Contract.ActorHeader header) => actor.Withholds(header);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => actor.ExecuteJobAsync(job, token);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => actor.RestoreWaits(header, state);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => actor.TerminalGrowth(header, state);
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => actor.ProtectsDeletion(header, state, now);

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.Inbox.Any(input => input.Kind == Fault)) throw new InvalidOperationException(Fault);
            foreach (var timer in turn.Inbox.Where(input => input.Kind == NativeKinds.Timer && input.Sender == turn.Header.Ref))
                timers.Enqueue((turn.Header.Ref, timer.Id, turn.Now));
            return actor.TurnAsync(turn, token);
        }
    }

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
