using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicModel = Kernel.Model.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresRootTerminateChecks
{
    internal static async Task<int> RunAsync(bool inBatch)
    {
        const string operation = "terminate-copy";
        const string definition = "before-upgraded-turn";
        const int payloadLength = 16_405_000;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootTerminations(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stop = timeout.Token;
        var cold = Store();
        await cold.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admission.InstallAsync(stop);
        var reads = new PostgresRootReads(cold, await admission.OpenJournalCursorAsync(stop), 12);
        var tenant = EmbeddedTenant.Bind("operators");
        var basic = new PostgresPreviewFoundation(admission, reads, PublicActor.ActorType);
        var api = new PostgresPreviewFoundation(admission, reads, PublicActor.ActorType,
            (scope, kind) => ReferenceEquals(scope, tenant) && kind is Contract.Kinds.Terminate or Contract.Kinds.Suspend);
        var runtime = new WorkflowRuntime(basic, basic);
        var registry = new WorkflowRegistry().Add(Wf.Workflow(definition, Wf.Parallel("root", null,
            Wf.Node<DirectApprovalWorkflow>("approval"), Wf.Node<InvocationWait>("receive"))));
        var native = new NativeExecutionModel(registry);
        var writer = new ObservedNative(native, 13);
        var reader = new ObservedNative(native, native.StateVersion);
        var actor = (await runtime.StartAsync(tenant, "start", NativeExecutionModel.Start(definition), cancellationToken: stop)).Actor!.Value;
        EfStore.RootImage prior;
        Message held;
        long priorEpoch;
        int priorBytes;
        await using (var owner = await Own("writer", writer))
        {
            var start = await Drive(owner, image => image is { Revision: 1 });
            var frame = Json.Read<NativeState>(start.State!).Frames.Values.Single(item => item.Node == "approval").Id;
            var value = new string('x', payloadLength);
            var signal = Json.Write(new CompositeSignal(frame, "approve", value));
            Check((await basic.SendAsync(tenant, new Contract.Target.Ref(actor), "first", CompositeSignal.Kind, signal,
                cancellationToken: stop)).Outcome == Contract.AdmissionOutcome.Accepted, "the first authored approval is admitted");
            await Drive(owner, image => image is { Revision: 2 });
            Check((await Control(tenant, "pause", Contract.Kinds.Suspend)).Outcome == Contract.AdmissionOutcome.Accepted,
                "the v13 writer accepts Suspend");
            prior = await Drive(owner, image => image is { Revision: 3, Suspended: true });
            priorEpoch = ((Worker)owner).Epoch;
            priorBytes = await PhysicalBytes(prior.Actor.Id);
            Check(Version(prior) == 13 && Json.Read<NativeState>(prior.State!).Inboxes!.Values.Single().Closed is null &&
                priorBytes is > payloadLength and < 16_457_728,
                "the actual Native workflow checkpoints v13 near the limit with its invocation inbox open");

            Check((await basic.SendAsync(tenant, new Contract.Target.Ref(actor), "later", CompositeSignal.Kind,
                Json.Write(new CompositeSignal(frame, "approve", "Grace")), cancellationToken: stop)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "a following approval is durably accepted while the root is suspended");
            held = (await admission.ReadPendingDirectAsync(tenant.Tenant, actor.Id, stop)).Single();
        }

        await using (var older = KernelDb.ForPostgresRootOperators(options))
        {
            try
            {
                await EfStore.RequireStoreVersionsAsync(older, stop);
                throw new InvalidOperationException("An older operator protocol accepted the database.");
            }
            catch (StoreFormatException) { Console.WriteLine("ok the prior operator protocol cannot attach"); }
        }

        Check(await admission.AdmitRootOperatorAsync("other", actor.Id, operation, Contract.Kinds.Terminate, null, null, stop) is null &&
            (await basic.SendAsync(tenant, new Contract.Target.Ref(actor), operation, Contract.Kinds.Terminate,
                cancellationToken: stop)).Outcome == Contract.AdmissionOutcome.Refused,
            "the termination requires matching scope and operator permission");
        Check((await Control(tenant, operation, Contract.Kinds.Terminate)).Outcome == Contract.AdmissionOutcome.Accepted,
            "the authorized exact creation admits one Terminate");
        var command = (await admission.ReadDueRootOperatorAsync(tenant.Tenant, actor.Id, stop))!;
        await using (var owner = await Own("reader", reader))
        {
            Check(((Worker)owner).Epoch > priorEpoch, "the successor acquired a later fence");
            var ended = await Drive(owner, image => image?.Status == InstanceStatus.Cancelled);
            var terminalState = Json.Read<NativeState>(ended.State!);
            var retained = terminalState.Frames.Values.Single(item => item.Node == "approval")
                .State!.AsArray().Single()!.GetValue<string>();
            Check(ended is { Revision: 4, TerminalRevision: 4, TerminalAt: > 0 } && Version(ended) == native.StateVersion &&
                reader.UpgradedOnTerminate && reader.OrdinaryTurns == 0 &&
                terminalState.Inboxes!.Values.Single().Closed is not null &&
                retained.Length == payloadLength && retained.AsSpan().IndexOfAnyExcept('x') < 0,
                "the first upgraded turn cancels the root and closes the real invocation inbox");
            var bytes = await PhysicalBytes(ended.Actor.Id);
            Check(bytes <= 16_457_728 && (await HeldRow(command.InboxKey)).Done,
                "the encoded terminal checkpoint fits and the exact Terminate occurrence is done");
            Console.WriteLine($"encoded checkpoint bytes before {priorBytes} after {bytes}");
            var replay = await Control(tenant, operation, Contract.Kinds.Terminate);
            Check(replay is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var original } && original == actor,
                "replaying the operator id returns the original creation after completion");
            await owner.WokenAsync(null, stop);
            for (var pass = 0; pass < 8 && !(await HeldRow(held.InboxKey)).Done; pass++) await owner.RunAsync(stop);
            Check((await HeldRow(held.InboxKey)).Done && reader.OrdinaryTurns == 0 &&
                (await cold.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop))?.Revision == 4,
                "the withheld approval is disposed without an authored turn or a new revision");
            var history = await cold.ReadJournalOfAsync(0, ended.Actor.Id, 0, stop);
            Check(history.Last().Revision == 4 && history.Count(row => row.Revision == 4) == 1 &&
                !history.SelectMany(row => Json.Read<string[]>(row.Entries)).Contains("approved:later"),
                "the journal records one terminal revision and no withheld approval");
        }
        return 0;

        async Task<Contract.Admission> Control(Contract.BoundScope scope, string id, string kind) =>
            await api.SendAsync(scope, new Contract.Target.Ref(actor), id, kind, cancellationToken: stop);

        async Task<IScopeActivation> Own(string node, ObservedNative model)
        {
            var store = Store();
            var (roots, partitions) = await store.BindWorkerPartitionAsync(0, stop);
            var adapter = new ActorAdapter(new EfStore.RootHeaders(roots), new PublicActor([model]));
            var host = new DurableHost([new Worker(node, 0, store, adapter, new WorkerOptions { Partitions = partitions })]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("Expected the partition owner.");
        }

        async Task<EfStore.RootImage> Drive(IScopeActivation owner, Func<EfStore.RootImage?, bool> expected)
        {
            await owner.WokenAsync(null, stop);
            do
            {
                await owner.RunAsync(stop);
                var image = await cold.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop);
                if (image is not null && expected(image)) return image;
                await Task.Delay(5, stop);
            } while (!stop.IsCancellationRequested);
            throw new InvalidOperationException("The expected checkpoint did not land.");
        }

        async Task<int> PhysicalBytes(string id)
        {
            await using var db = Open();
            return await db.Database.SqlQuery<int>($"""
                SELECT octet_length("State") + octet_length("FoundationHeader") AS "Value"
                FROM "Instances" WHERE "Id" = {id}
                """).SingleAsync(stop);
        }

        async Task<MessageEntity> HeldRow(InboxKey key)
        {
            await using var db = Open();
            return await db.Messages.AsNoTracking().SingleAsync(row => row.InboxScope == key.Scope && row.Id == key.Id, stop);
        }
    }

    private static int Version(EfStore.RootImage image) => Json.Read<JsonElement>(image.TypeData).GetProperty("Version").GetInt32();

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }

    internal sealed class InvocationWait : CompositeActivity
    {
        protected override void Execute(CompositeContext context) => context.RegisterInputInbox(1, 1, TimeSpan.FromHours(1));
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
            throw new InvalidOperationException("This invocation inbox has no child.");
    }

    private sealed class ObservedNative(NativeExecutionModel native, int version) : PublicModel.IExecutionModel, PublicModel.IModelMetadata
    {
        private object? upgraded;
        public string Key => native.Key;
        public Type StateType => native.StateType;
        public int StateVersion => version;
        internal int OrdinaryTurns { get; private set; }
        internal bool UpgradedOnTerminate { get; private set; }
        public object CreateState(PublicModel.WorkflowStart start) => native.CreateState(start);
        public object UpgradeState(string state, int fromVersion)
        {
            var current = native.UpgradeState(state, fromVersion);
            if (fromVersion == 13) upgraded = current;
            return current;
        }
        public ValueTask TurnAsync(PublicModel.ITurn turn, CancellationToken cancellationToken)
        {
            OrdinaryTurns++;
            return native.TurnAsync(turn, cancellationToken);
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => native.ExecuteJobAsync(job, token);
        public Contract.RestorePlan RestoreWaits(PublicModel.InstanceInfo instance, object state) => native.RestoreWaits(instance, state);
        public long TerminalGrowth(PublicModel.InstanceInfo instance, object state) => native.TerminalGrowth(instance, state);
        public bool HandlesMetadata(Contract.Message message) => native.HandlesMetadata(message);
        public ValueTask MetadataAsync(PublicModel.ITurn turn, Contract.ActorStatus status, long? endedAt, CancellationToken token) =>
            native.MetadataAsync(turn, status, endedAt, token);
        public bool ProtectsDeletion(object state, long? endedAt, long now) => native.ProtectsDeletion(state, endedAt, now);
        public void TerminateMetadata(object state)
        {
            UpgradedOnTerminate = ReferenceEquals(upgraded, state);
            native.TerminateMetadata(state);
        }
    }
}
