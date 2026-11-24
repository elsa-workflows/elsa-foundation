using System.Text.Json;
using System.Text.Json.Nodes;
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
    internal static async Task<int> RunAsync(bool owned, bool inBatch, bool fallback = false)
    {
        const string operation = "terminate-copy";
        const string definition = "before-upgraded-turn";
        const int payloadLength = 16_405_000;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootTerminations(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stop = timeout.Token;
        await using (var db = Open())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the qualification uses an empty PostgreSQL database");
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
        var reader = new ObservedNative(native, native.StateVersion, fallback ? 400_000 : 0);
        var upgradedActor = new PublicActor([reader]);
        var actor = (await runtime.StartAsync(tenant, "start", NativeExecutionModel.Start(definition), cancellationToken: stop)).Actor!.Value;
        Contract.ActorRef? neighbor = fallback
            ? (await runtime.StartAsync(tenant, "neighbor", NativeExecutionModel.Start(definition), cancellationToken: stop)).Actor!.Value
            : null;
        EfStore.RootImage? neighborBefore = null;
        EfStore.RootImage prior;
        Message held;
        long priorEpoch;
        EncodedContent before;
        string inbox;
        var recoveryTargets = new HashSet<string>(StringComparer.Ordinal);
        await using (var owner = await Own("writer", new PublicActor([writer])))
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
            before = await ReadEncodedContent(prior.Actor.Id);
            var previousInbox = Json.Read<NativeState>(prior.State!).Inboxes!.Single();
            inbox = previousInbox.Key;
            Check(Version(prior) == 13 && previousInbox.Value.Closed is null &&
                before.Bytes is > payloadLength and < 16_457_728,
                "the actual Native workflow checkpoints v13 near the limit with its invocation inbox open");

            Check((await basic.SendAsync(tenant, new Contract.Target.Ref(actor), "later", CompositeSignal.Kind,
                Json.Write(new CompositeSignal(frame, "approve", "Grace")), cancellationToken: stop)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "a following approval is durably accepted while the root is suspended");
            held = (await admission.ReadPendingDirectAsync(tenant.Tenant, actor.Id, stop)).Single();
            if (neighbor is { } other)
            {
                neighborBefore = await Drive(owner, image => image is { Revision: 1 }, other);
                var otherFrame = Json.Read<NativeState>(neighborBefore.State!).Frames.Values.Single(item => item.Node == "approval").Id;
                Check((await basic.SendAsync(tenant, new Contract.Target.Ref(other), "neighbor-input", CompositeSignal.Kind,
                    Json.Write(new CompositeSignal(otherFrame, "approve", "held for the normal owner")), cancellationToken: stop)).Outcome ==
                    Contract.AdmissionOutcome.Accepted, "the neighboring actor has its own pending input");
            }
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
        if (fallback) priorEpoch = await ProveExpandedWriteRefused();
        var successor = fallback ? new PublicActor([]) : upgradedActor;
        await using (var owner = await Own("reader", successor, selected: fallback))
        {
            Check(((Worker)owner).Epoch > priorEpoch, "the successor acquired a later fence");
            var ended = await Drive(owner, image => image?.Status == InstanceStatus.Cancelled);
            Check(ended is { Revision: 4, TerminalRevision: 4, TerminalAt: > 0 } && reader.OrdinaryTurns == 0,
                "the terminal checkpoint lands before any ordinary upgraded turn");
            var encoded = await ReadEncodedContent(ended.Actor.Id);
            if (fallback)
            {
                Check(Version(ended) == 13 && ended.State == prior.State && ended.TypeData == prior.TypeData &&
                    ended.Labels.OrderBy(label => label.Key).SequenceEqual(prior.Labels.OrderBy(label => label.Key)) && encoded == before,
                    "the explicitly selected unavailable-model owner preserves the v13 state and headers byte for byte");
            }
            else
            {
                var terminalState = Json.Read<NativeState>(ended.State!);
                var retained = terminalState.Frames.Values.Single(item => item.Node == "approval")
                    .State!.AsArray().Single()!.GetValue<string>();
                var closed = terminalState.Inboxes!.Single();
                Check(Version(ended) == native.StateVersion && reader.UpgradedOnTerminate &&
                    closed.Key == inbox && closed.Value.Closed == "cancelled" &&
                    retained.Length == payloadLength && retained.AsSpan().IndexOfAnyExcept('x') < 0,
                    "the first upgraded turn cancels the root and closes the real invocation inbox");
            }
            Check(encoded.Bytes <= 16_457_728 && (await HeldRow(command.InboxKey)).Done,
                "the encoded terminal checkpoint fits and the exact Terminate occurrence is done");
            Console.WriteLine($"encoded checkpoint bytes before {before.Bytes} after {encoded.Bytes}");
            var replay = await Control(tenant, operation, Contract.Kinds.Terminate);
            Check(replay is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var original } && original == actor,
                "replaying the operator id returns the original creation after completion");
            Check(!(await HeldRow(held.InboxKey)).Done, "the withheld input is still pending at the terminal checkpoint");
            await CheckDirectReplay("before the withheld approval is disposed");
            await owner.WokenAsync(null, stop);
            for (var pass = 0; pass < 8 && !(await HeldRow(held.InboxKey)).Done; pass++) await owner.RunAsync(stop);
            Check((await HeldRow(held.InboxKey)).Done && reader.OrdinaryTurns == 0 &&
                (await cold.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop))?.Revision == 4,
                "the withheld approval is disposed without an authored turn or a new revision");
            await CheckDirectReplay("after the withheld approval is disposed");
            var history = await cold.ReadJournalOfAsync(0, ended.Actor.Id, 0, stop);
            var entries = history.SelectMany(row => Json.Read<string[]>(row.Entries)).ToArray();
            Check(history.Last() is { Revision: 4, Entries: """["end"]""" } && history.Count(row => row.Revision == 4) == 1 &&
                entries.Count(entry => entry == "approved:first") == 1 && !entries.Contains("approved:later"),
                "the journal records the original approval once and the exact ending entry");
            if (neighbor is { } other && neighborBefore is { } neighborImage)
            {
                var unchanged = (await cold.ReadFirstRootAsync(tenant.Tenant, other.Id, stop))!;
                Check(unchanged.Status == neighborImage.Status && unchanged.Revision == neighborImage.Revision &&
                    unchanged.TypeData == neighborImage.TypeData && unchanged.State == neighborImage.State &&
                    (await admission.ReadPendingDirectAsync(tenant.Tenant, other.Id, stop)).Single().Id == "neighbor-input" &&
                    recoveryTargets.SetEquals([prior.Actor.Id]),
                    "the selected fallback owner leaves the adjacent actor and its input untouched");
            }
            var legacyReader = new EfStore(() => KernelDb.ForPostgresRootOperators(options), postgres: true);
            try
            {
                await legacyReader.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop);
                throw new InvalidOperationException("The prior operator reader decoded a format-11 checkpoint.");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite && error.Message == "Unsupported qualified actor checkpoint.")
            {
                Console.WriteLine("ok the prior reader refuses the persisted terminal checkpoint");
            }
        }
        if (neighbor is { } remaining)
        {
            await using var normal = await Own("normal-owner", new PublicActor([new ObservedNative(native, native.StateVersion)]));
            var advanced = await Drive(normal, image => image is { Revision: 2 }, remaining);
            Check(advanced.Status == InstanceStatus.Running &&
                (await admission.ReadPendingDirectAsync(tenant.Tenant, remaining.Id, stop)).Count == 0 &&
                (await cold.ReadJournalOfAsync(0, advanced.Actor.Id, 0, stop)).SelectMany(row => Json.Read<string[]>(row.Entries))
                    .Count(entry => entry == "approved:neighbor-input") == 1,
                "releasing the selected owner lets the neighboring actor resume exactly once");
        }
        return 0;

        async Task<long> ProveExpandedWriteRefused()
        {
            var upgraded = reader.UpgradeState(prior.State!, 13);
            reader.TerminateMetadata(upgraded);
            var expandedState = JsonSerializer.Serialize(upgraded, native.StateType);
            Check(reader.UpgradedOnTerminate && expandedState.Length > 16 * 1024 * 1024 - 24 * 1024 &&
                expandedState.AsSpan().IndexOfAnyExceptInRange((char)1, (char)127) < 0,
                "the actual upgraded terminal state alone exceeds PostgreSQL's encoded content budget");
            var probe = Store();
            await using var probeOwner = await Own("size-probe", upgradedActor, probe, selected: true);
            var fence = ((Worker)probeOwner).Fences.Single();
            Check(fence.Epoch > priorEpoch, "the oversized candidate uses a current selected fence");
            var stored = (await probe.LoadAsync([prior.Actor.Id], stop)).Single();
            var header = JsonNode.Parse(prior.TypeData)!.AsObject();
            header["Version"] = native.StateVersion;
            var labels = prior.Labels.ToDictionary(StringComparer.Ordinal);
            labels["version"] = native.StateVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var change = new FoundationHeaderChange(header.ToJsonString(), labels);
            var expanded = stored.WithFoundationState(expandedState) with
            {
                Status = InstanceStatus.Cancelled, Revision = stored.Revision + 1,
                FoundationDispatchHeader = stored.FoundationDispatchHeader! with { Header = change }
            };
            TurnCommit candidate = new(stored.Id, expanded, stored.Revision, [], [command.InboxKey], [], [], [], """["end"]""", Unsubscribe: true)
                { FoundationRootOperator = command, FoundationHeader = change };
            try
            {
                await probe.CommitAsync(new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [candidate], [], []), stop);
                throw new InvalidOperationException("The provider accepted the oversized terminal write.");
            }
            catch (StoreException error) when (error is
                { Failure: Failure.Definite, Message: "The encoded PostgreSQL checkpoint exceeds its storage budget." })
            {
                Console.WriteLine("ok PostgreSQL definitely refuses the expanded terminal write");
            }
            var checkpoint = (await cold.ReadFirstRootAsync(tenant.Tenant, actor.Id, stop))!;
            await using var db = Open();
            Check(checkpoint.Revision == prior.Revision && checkpoint.Status == prior.Status && checkpoint.Suspended &&
                checkpoint.State == prior.State && checkpoint.TypeData == prior.TypeData && await ReadEncodedContent(stored.Id) == before &&
                !(await HeldRow(command.InboxKey)).Done && !(await HeldRow(held.InboxKey)).Done &&
                (await cold.ReadJournalOfAsync(fence.Partition, stored.Id, 0, stop)).All(entry => entry.Revision < expanded.Revision) &&
                await db.Partitions.Where(partition => partition.Id == fence.Partition).Select(partition => partition.Position).SingleAsync(stop) ==
                    fence.Position,
                "the refusal preserves the checkpoint, both inputs, journal and fence position");
            return fence.Epoch;
        }

        async Task CheckDirectReplay(string phase)
        {
            var target = new Contract.Target.Ref(actor);
            var old = await basic.SendAsync(tenant, target, held.Id, held.Kind, held.Payload, cancellationToken: stop);
            var fresh = await basic.SendAsync(tenant, target, "new-approval", held.Kind, held.Payload, cancellationToken: stop);
            Check(old.Outcome == Contract.AdmissionOutcome.Duplicate && fresh.Outcome == Contract.AdmissionOutcome.Refused,
                $"replay keeps its receipt and fresh input is refused {phase}");
        }

        async Task<Contract.Admission> Control(Contract.BoundScope scope, string id, string kind) =>
            await api.SendAsync(scope, new Contract.Target.Ref(actor), id, kind, cancellationToken: stop);

        async Task<IScopeActivation> Own(string node, PublicActor model, EfStore? existingStore = null, bool selected = false)
        {
            var store = existingStore ?? Store();
            var roots = selected ? await store.BindWorkerRootsAsync([(tenant.Tenant, actor.Id)], stop)
                : (await store.BindWorkerPartitionAsync(0, stop)).Scope;
            var adapter = new ActorAdapter(new EfStore.RootHeaders(roots), model);
            var workerOptions = new WorkerOptions
            {
                Partitions = 1,
                OnObservedDelivery = fallback && node == "reader" ? item => recoveryTargets.Add(item.Message.Target.Id) : null
            };
            var host = new DurableHost([new Worker(node, 0, store, adapter, workerOptions)]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("Expected the partition owner.");
        }

        async Task<EfStore.RootImage> Drive(IScopeActivation owner, Func<EfStore.RootImage?, bool> expected, Contract.ActorRef? reference = null)
        {
            await owner.WokenAsync(null, stop);
            do
            {
                await owner.RunAsync(stop);
                var image = await cold.ReadFirstRootAsync(tenant.Tenant, (reference ?? actor).Id, stop);
                if (image is not null && expected(image)) return image;
                await Task.Delay(5, stop);
            } while (!stop.IsCancellationRequested);
            throw new InvalidOperationException("The expected checkpoint did not land.");
        }

        async Task<EncodedContent> ReadEncodedContent(string id)
        {
            await using var db = Open();
            return await db.Database.SqlQuery<EncodedContent>($"""
                SELECT md5("State") AS "StateHash", md5("FoundationHeader") AS "HeaderHash",
                    octet_length("State") + octet_length("FoundationHeader") AS "Bytes"
                FROM "Instances" WHERE "Id" = {id}
                """).SingleAsync(stop);
        }

        async Task<MessageEntity> HeldRow(InboxKey key)
        {
            await using var db = Open();
            return await db.Messages.AsNoTracking().SingleAsync(row => row.InboxScope == key.Scope && row.Id == key.Id, stop);
        }
    }

    private sealed record EncodedContent(string StateHash, string HeaderHash, int Bytes);

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

    private sealed class ObservedNative(NativeExecutionModel native, int version, int padding = 0) : PublicModel.IExecutionModel, PublicModel.IModelMetadata
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
            if (fromVersion == 13)
            {
                if (padding != 0) ((NativeState)current).Variables.Add("upgrade-padding", JsonValue.Create(new string('x', padding)));
                upgraded = current;
            }
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
