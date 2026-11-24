using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicModel = Kernel.Model.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresRootThenDeleteChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootTerminateThenDelete(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stop = deadline.Token;
        await using (var db = Open())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the fixture uses an empty PostgreSQL database");
        var cold = Store();
        await cold.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admission.InstallAsync(stop);
        var tenant = EmbeddedTenant.Bind("then-delete");
        var reads = new PostgresRootReads(cold, await admission.OpenJournalCursorAsync(stop), 12);
        Contract.ActorRef? protectedTarget = null;
        var allowTerminate = true;
        var allowDelete = false;
        var ingress = await cold.OpenSelectedRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1),
            (scope, _) => allowTerminate && ReferenceEquals(scope, tenant), stop,
            (scope, _) => allowDelete && ReferenceEquals(scope, tenant));
        var runtime = new WorkflowRuntime(ingress, reads);
        var registry = new WorkflowRegistry().Add(Wf.Workflow("wait", Wf.Node<PostgresRootTerminateChecks.InvocationWait>("receive")));
        var actorType = new PublicActor([new SeededReceipt(new NativeExecutionModel(registry), reference => reference == protectedTarget)]);

        var remove = await Start("remove");
        var keep = await Start("keep");
        protectedTarget = keep;
        var endsFirst = await Start("ends-first");
        EfStore.RootImage removable;
        EfStore.RootImage protectedImage;
        EfStore.RootImage later;
        long originalEpoch;
        await using (var owner = await Own("original"))
        {
            removable = await Drive(owner, remove, InstanceStatus.Running);
            protectedImage = await Drive(owner, keep, InstanceStatus.Running);
            later = await Drive(owner, endsFirst, InstanceStatus.Running);
            originalEpoch = ((Worker)owner).Epoch;
        }
        var selected = (await reads.GetAsync(tenant, remove, stop))!;
        var selectedKeep = (await reads.GetAsync(tenant, keep, stop))!;
        var selectedLate = (await reads.GetAsync(tenant, endsFirst, stop))!;
        Check(Json.Read<NativeState>(protectedImage.State!).Inboxes!.Values.Single().Receipts.ContainsKey("retained"),
            "the actual Native checkpoint has a retained receipt");
        Check((await Then("remove", selected)).Outcome == Contract.AdmissionOutcome.Refused,
            "terminate permission alone does not admit ThenDelete");
        allowTerminate = false;
        allowDelete = true;
        Check((await Then("remove", selected)).Outcome == Contract.AdmissionOutcome.Refused,
            "delete permission alone does not admit ThenDelete");
        allowTerminate = true;
        var wrong = selected with { Header = selected.Header with { Ref = keep } };
        Check((await Then("remove", wrong)).Outcome == Contract.AdmissionOutcome.Refused &&
            (await ingress.SendAsync(tenant, new Contract.Target.Ref(remove), "remove", Contract.Kinds.Terminate,
                JsonSerializer.Serialize(new Contract.OperatorCommand(selected.Selection, ThenDelete: true)), cancellationToken: stop)).Outcome ==
                Contract.AdmissionOutcome.Refused, "the command requires the exact running selection and target");
        Check((await Then("remove", selected)).Outcome == Contract.AdmissionOutcome.Accepted &&
              (await Then("keep", selectedKeep)).Outcome == Contract.AdmissionOutcome.Accepted,
            "both authorized live targets admit ThenDelete");
        Check((await runtime.TerminateAsync(tenant, "end-first", selectedLate, cancellationToken: stop)).Outcome ==
                Contract.AdmissionOutcome.Accepted &&
            (await Then("remove-late", selectedLate)).Outcome == Contract.AdmissionOutcome.Accepted,
            "the earlier Terminate and the selected ThenDelete are admitted before the owner runs");

        await RejectMarkerAfterTermination(removable);
        await using (var owner = await Own("successor"))
        {
            Check(((Worker)owner).Epoch > originalEpoch, "a newer owner processes the admitted operations");
            while (await reads.GetAsync(tenant, remove, stop) is not null ||
                   await reads.GetAsync(tenant, keep, stop) is not { Header.Status: Contract.ActorStatus.Cancelled } ||
                   await reads.GetAsync(tenant, endsFirst, stop) is not null)
                await Next(owner);
        }

        await using (var db = Open())
        {
            var marker = await ReadMarker(db, removable.Actor.Id);
            var lateMarker = await ReadMarker(db, later.Actor.Id);
            var history = await cold.ReadJournalOfAsync(0, removable.Actor.Id, 0, stop);
            var lateHistory = await cold.ReadJournalOfAsync(0, later.Actor.Id, 0, stop);
            Check(marker is { Retired: true, Status: InstanceStatus.Cancelled, State: "null", TerminalAt: > 0 } &&
                marker.Revision == removable.Revision + 1 && marker.TerminalRevision == marker.Revision &&
                history is [{ Revision: -1, Entries: """["delete"]""" }],
                "an unprotected live root retires in one revision and leaves only a deletion marker");
            Check(lateMarker is { Retired: true, Status: InstanceStatus.Cancelled } && lateMarker.Revision == later.Revision + 2 &&
                lateMarker.TerminalRevision == later.Revision + 1 && lateHistory is [{ Revision: -1 }],
                "an actor ended before delivery retires while preserving the earlier terminal mark");
        }
        var retained = (await cold.ReadFirstRootAsync(tenant.Tenant, keep.Id, stop))!;
        var inbox = Json.Read<NativeState>(retained.State!).Inboxes!.Values.Single();
        Check(retained.Revision == protectedImage.Revision + 1 && retained.TerminalRevision == retained.Revision &&
            retained.TerminalAt is not null && inbox is { Closed: "cancelled", Pending.Count: 0 } &&
            inbox.Receipts["retained"] is { Status: "NotProcessed", Clock: "workflow:end" } &&
            (await cold.ReadJournalOfAsync(0, protectedImage.Actor.Id, 0, stop)).Last() is { Revision: 2 },
            "real Native receipt protection keeps the cancelled checkpoint and its terminal journal");
        Check((await Then("remove", selected)) is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var actor } && actor == remove &&
              (await Then("new-operation", selected)).Outcome == Contract.AdmissionOutcome.Refused,
            "the retired root replays its original admission and refuses a fresh operation");
        return 0;

        async Task RejectMarkerAfterTermination(EfStore.RootImage current)
        {
            var command = (await admission.ReadDueRootOperatorAsync(tenant.Tenant, remove.Id, stop))!;
            var store = Store();
            await store.BindWorkerPartitionAsync(0, stop);
            var lease = (await store.TakeAsync(0, "rollback", Defaults.Lease, stop))!;
            var history = await cold.ReadJournalOfAsync(0, current.Actor.Id, 0, stop);
            var terminated = new InstanceRow(current.Actor.Id, current.Model, current.Definition, InstanceStatus.Cancelled,
                current.State!, current.Revision, null, Creation: current.Actor.Creation);
            var turn = new TurnCommit(current.Actor.Id, CreationProtocol.Retire(terminated), current.Revision,
                [], [command.InboxKey], [], [], [], """["delete"]""", Unsubscribe: true, Delete: true)
                { FoundationRootOperator = command };
            try
            {
                await using var db = Open();
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION fixture_skip_termination_marker() RETURNS trigger LANGUAGE plpgsql AS $fn$
                    BEGIN RETURN NULL; END
                    $fn$;
                    CREATE TRIGGER fixture_skip_termination_marker BEFORE INSERT ON "Journal"
                    FOR EACH ROW WHEN (NEW."Revision" = -1) EXECUTE FUNCTION fixture_skip_termination_marker();
                    """, stop);
                var result = await store.CommitAsync(new(0, lease.Epoch, lease.Position, Defaults.Lease, false, [turn], [], []), stop);
                var after = await cold.ReadFirstRootAsync(tenant.Tenant, remove.Id, stop);
                Check(!result.Landed && after is { Status: InstanceStatus.Running, TerminalRevision: null, TerminalAt: null } &&
                    after.Revision == current.Revision && after.State == current.State &&
                    (await reads.GetAsync(tenant, remove, stop))?.Header.Status == Contract.ActorStatus.Running &&
                    (await admission.ReadDueRootOperatorAsync(tenant.Tenant, remove.Id, stop)) == command &&
                    (await cold.ReadJournalOfAsync(0, current.Actor.Id, 0, stop)).SequenceEqual(history) &&
                    await db.Partitions.Select(partition => partition.Position).SingleAsync(stop) == lease.Position,
                    "a suppressed deletion marker rolls back termination, checkpoint, operator input, journal, projection and fence");
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var db = Open();
                await db.Database.ExecuteSqlRawAsync("""
                    DROP TRIGGER IF EXISTS fixture_skip_termination_marker ON "Journal";
                    DROP FUNCTION IF EXISTS fixture_skip_termination_marker();
                    """, cleanup.Token);
                await store.ReleaseAsync(0, lease.Epoch, cleanup.Token);
            }
        }

        async Task<Contract.ActorRef> Start(string id) =>
            (await runtime.StartAsync(tenant, id, NativeExecutionModel.Start("wait"), cancellationToken: stop)).Actor!.Value;

        ValueTask<Contract.Admission> Then(string id, Contract.ActorView at) =>
            runtime.TerminateThenDeleteAsync(tenant, id, at, stop);

        async Task<IScopeActivation> Own(string node)
        {
            var store = Store();
            var roots = (await store.BindWorkerPartitionAsync(0, stop)).Scope;
            var host = new DurableHost([new Worker(node, 0, store, new ActorAdapter(new EfStore.RootHeaders(roots), actorType),
                new WorkerOptions { Partitions = 1 })]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture owner could not take its partition.");
        }

        async Task Next(IScopeActivation owner)
        {
            await owner.WokenAsync(null, stop);
            await owner.RunAsync(stop);
        }

        async Task<EfStore.RootImage> Drive(IScopeActivation owner, Contract.ActorRef target, InstanceStatus status)
        {
            while (true)
            {
                await Next(owner);
                if (await cold.ReadFirstRootAsync(tenant.Tenant, target.Id, stop) is { } image && image.Status == status) return image;
            }
        }

        Task<PhysicalMarker> ReadMarker(KernelDb db, string id) => db.Database.SqlQuery<PhysicalMarker>($"""
            SELECT "State", "Retired", "Status", "Revision", "FoundationTerminalRevision" AS "TerminalRevision", "FoundationTerminalAt" AS "TerminalAt"
            FROM "Instances" WHERE "Id" = {id}
            """).SingleAsync(stop);
    }

    private sealed record PhysicalMarker(string State, bool Retired, InstanceStatus Status, long Revision, long? TerminalRevision, long? TerminalAt);

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }

    // Seed an actual Native inbox receipt during fixture setup. The termination and protection hooks are unmodified.
    private sealed class SeededReceipt(NativeExecutionModel model, Func<Contract.ActorRef, bool> retain) :
        PublicModel.IExecutionModel, PublicModel.IModelMetadata
    {
        public string Key => model.Key;
        public Type StateType => model.StateType;
        public int StateVersion => model.StateVersion;
        public object CreateState(PublicModel.WorkflowStart start) => model.CreateState(start);
        public object UpgradeState(string state, int version) => model.UpgradeState(state, version);
        public async ValueTask TurnAsync(PublicModel.ITurn turn, CancellationToken token)
        {
            await model.TurnAsync(turn, token);
            if (retain(turn.Instance.Ref) && turn.State is NativeState { Inboxes.Count: 1 } state)
            {
                var (frame, inbox) = state.Inboxes.Single();
                inbox.Receipts.Add("retained", new("seeded", ++inbox.Next, Retention: inbox.Retention));
                inbox.Pending.Add(new InvocationInput(frame, "retained", "protect", "queued payload"));
            }
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => model.ExecuteJobAsync(job, token);
        public Contract.RestorePlan RestoreWaits(PublicModel.InstanceInfo instance, object state) => model.RestoreWaits(instance, state);
        public long TerminalGrowth(PublicModel.InstanceInfo instance, object state) => model.TerminalGrowth(instance, state);
        public bool HandlesMetadata(Contract.Message message) => model.HandlesMetadata(message);
        public ValueTask MetadataAsync(PublicModel.ITurn turn, Contract.ActorStatus status, long? endedAt, CancellationToken token) =>
            model.MetadataAsync(turn, status, endedAt, token);
        public void TerminateMetadata(object state) => model.TerminateMetadata(state);
        public bool ProtectsDeletion(object state, long? endedAt, long now) => model.ProtectsDeletion(state, endedAt, now);
    }
}
