using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("wait", Wf.Node<PostgresRootTerminateChecks.InvocationWait>("receive")))
            .Add(Wf.Workflow("approve", Wf.Node<PostgresFlowchartPreview.Approval>("approve")));
        var actorType = new PublicActor([new SeededReceipt(new NativeExecutionModel(registry), reference => reference == protectedTarget)]);

        var remove = await Start("remove");
        var keep = await Start("keep");
        protectedTarget = keep;
        var endsFirst = await Start("ends-first");
        var finishesNaturally = await Start("naturally-finished", "approve");
        EfStore.RootImage removable;
        EfStore.RootImage protectedImage;
        EfStore.RootImage later;
        EfStore.RootImage beforeFinish;
        long originalEpoch;
        await using (var owner = await Own("original"))
        {
            removable = await Drive(owner, remove, InstanceStatus.Running);
            protectedImage = await Drive(owner, keep, InstanceStatus.Running);
            later = await Drive(owner, endsFirst, InstanceStatus.Running);
            beforeFinish = await Drive(owner, finishesNaturally, InstanceStatus.Running);
            originalEpoch = ((Worker)owner).Epoch;
        }
        var selected = (await reads.GetAsync(tenant, remove, stop))!;
        var selectedKeep = (await reads.GetAsync(tenant, keep, stop))!;
        var selectedLate = (await reads.GetAsync(tenant, endsFirst, stop))!;
        var selectedBeforeFinish = (await reads.GetAsync(tenant, finishesNaturally, stop))!;
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

        await CheckWireFraming(removable);
        await VerifyRollback(removable, RollbackFault.SuppressMarker);
        await VerifyRollback(removable, RollbackFault.RetainWireBeforeGuard);
        await VerifyRollback(removable, RollbackFault.RetainWireAfterGuard);
        await VerifyRollback(removable, RollbackFault.ChangeTerminalTime);
        EfStore.RootImage endedBeforeDelivery;
        EfStore.RootImage finishedBeforeAdmission;
        await using (var owner = await Own("successor"))
        {
            Check(((Worker)owner).Epoch > originalEpoch, "a newer owner processes the admitted operations");
            endedBeforeDelivery = await Drive(owner, endsFirst, InstanceStatus.Cancelled);
            var approval = Json.Read<NativeState>(beforeFinish.State!).Frames.Values.Single(frame => frame.Node == "approve").Id;
            Check((await ingress.SendAsync(tenant, new Contract.Target.Ref(finishesNaturally), "natural-approval", CompositeSignal.Kind,
                Json.Write(new CompositeSignal(approval, "approve", "Ada")), cancellationToken: stop)).Outcome ==
                    Contract.AdmissionOutcome.Accepted, "the ordinary Native completion input is admitted");
            finishedBeforeAdmission = await Drive(owner, finishesNaturally, InstanceStatus.Finished);
            Check((await Then("finished-remove", selectedBeforeFinish)).Outcome == Contract.AdmissionOutcome.Accepted,
                "the earlier running selection still admits ThenDelete after the exact root finishes naturally");
            while (await reads.GetAsync(tenant, remove, stop) is not null ||
                   await reads.GetAsync(tenant, keep, stop) is not { Header.Status: Contract.ActorStatus.Cancelled } ||
                   await reads.GetAsync(tenant, endsFirst, stop) is not null ||
                   await reads.GetAsync(tenant, finishesNaturally, stop) is not null)
                await Next(owner);

            var protectedEnd = (await cold.ReadFirstRootAsync(tenant.Tenant, keep.Id, stop))!;
            Check((await Then("keep-after-end", selectedKeep)).Outcome == Contract.AdmissionOutcome.Accepted,
                "the earlier selection admits another authorized request to the protected ended root");
            while (await admission.ReadDueRootOperatorAsync(tenant.Tenant, keep.Id, stop) is not null)
                await Next(owner);
            var unchanged = (await cold.ReadFirstRootAsync(tenant.Tenant, keep.Id, stop))!;
            Check(unchanged.Revision == protectedEnd.Revision && unchanged.TerminalAt == protectedEnd.TerminalAt &&
                  unchanged.State == protectedEnd.State,
                "the ended-root decision respects retained Native receipt protection");
        }

        await using (var db = Open())
        {
            var marker = await ReadMarker(db, removable.Actor.Id);
            var lateMarker = await ReadMarker(db, later.Actor.Id);
            var finishedMarker = await ReadMarker(db, beforeFinish.Actor.Id);
            var history = await cold.ReadJournalOfAsync(0, removable.Actor.Id, 0, stop);
            var lateHistory = await cold.ReadJournalOfAsync(0, later.Actor.Id, 0, stop);
            Check(marker is { Retired: true, Status: InstanceStatus.Cancelled, State: "null", WireBytes: null, Header: null, TerminalAt: > 0 } &&
                marker.Revision == removable.Revision + 1 && marker.TerminalRevision == marker.Revision &&
                history is [{ Revision: -1, Entries: """["delete"]""" }],
                "an unprotected live root retires in one revision and leaves only a deletion marker");
            Check(lateMarker is { Retired: true, Status: InstanceStatus.Cancelled } && lateMarker.Revision == later.Revision + 2 &&
                lateMarker.TerminalRevision == endedBeforeDelivery.TerminalRevision && lateMarker.TerminalAt == endedBeforeDelivery.TerminalAt &&
                lateHistory is [{ Revision: -1 }],
                "an actor ended before delivery retires while preserving both fields of the earlier terminal mark");
            Check(finishedMarker is { Retired: true, Status: InstanceStatus.Finished, WireBytes: null } &&
                finishedMarker.Revision == finishedBeforeAdmission.Revision + 1 &&
                finishedMarker.TerminalRevision == finishedBeforeAdmission.TerminalRevision &&
                finishedMarker.TerminalAt == finishedBeforeAdmission.TerminalAt,
                "an actor ended before admission keeps its finished status and both fields of the terminal mark");
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

        async Task CheckWireFraming(EfStore.RootImage current)
        {
            await using var db = Open();
            var row = await db.Messages.AsNoTracking().SingleAsync(message => message.Target == current.Actor.Id && !message.Done, stop);
            Check(row.Start is not null && !row.Start.StartsWith("!kernel-operator/1:", StringComparison.Ordinal) &&
                PostgresRootAdmission.DecodeOperator(row, remove.Id, true, true, true).Operator?.RootThenDelete == true,
                "the combined command has its own envelope and the new reader recognizes it");
            try
            {
                PostgresRootAdmission.DecodeOperator(row, remove.Id, true, true, false);
                throw new InvalidOperationException("The earlier format accepted the combined command.");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite)
            {
                Console.WriteLine("ok the prior-format decoder configuration rejects that persisted envelope");
            }
        }

        async Task VerifyRollback(EfStore.RootImage current, RollbackFault fault)
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
                if (fault == RollbackFault.SuppressMarker)
                    await db.Database.ExecuteSqlRawAsync("""
                        CREATE FUNCTION fixture_skip_termination_marker() RETURNS trigger LANGUAGE plpgsql AS $fn$
                        BEGIN RETURN NULL; END
                        $fn$;
                        CREATE TRIGGER fixture_skip_termination_marker BEFORE INSERT ON "Journal"
                        FOR EACH ROW WHEN (NEW."Revision" = -1) EXECUTE FUNCTION fixture_skip_termination_marker();
                        """, stop);
                else
                {
                    await db.Database.ExecuteSqlRawAsync("""
                        CREATE FUNCTION fixture_change_retirement_marker() RETURNS trigger LANGUAGE plpgsql AS $fn$
                        BEGIN
                            IF TG_ARGV[0] = 'time' THEN
                                NEW."FoundationTerminalAt" = NEW."FoundationTerminalAt" + 1000;
                            ELSE
                                NEW."FoundationStateWire" = OLD."FoundationStateWire";
                            END IF;
                            RETURN NEW;
                        END
                        $fn$;
                        """, stop);
                    // PostgreSQL runs same-event triggers by name. Also exercise retention after the checkpoint guard.
                    await db.Database.ExecuteSqlRawAsync(fault switch
                    {
                        RollbackFault.RetainWireBeforeGuard => """
                            CREATE TRIGGER fixture_change_retirement_marker BEFORE UPDATE ON "Instances"
                            FOR EACH ROW WHEN (NEW."Retired") EXECUTE FUNCTION fixture_change_retirement_marker('wire');
                            """,
                        RollbackFault.RetainWireAfterGuard => """
                            CREATE TRIGGER z_fixture_change_retirement_marker BEFORE UPDATE ON "Instances"
                            FOR EACH ROW WHEN (NEW."Retired") EXECUTE FUNCTION fixture_change_retirement_marker('wire');
                            """,
                        _ => """
                            CREATE TRIGGER z_fixture_change_retirement_marker BEFORE UPDATE ON "Instances"
                            FOR EACH ROW WHEN (NEW."Retired") EXECUTE FUNCTION fixture_change_retirement_marker('time');
                            """
                    }, stop);
                }
                var stopped = false;
                try
                {
                    var result = await store.CommitAsync(new(0, lease.Epoch, lease.Position, Defaults.Lease, false, [turn], [], []), stop);
                    stopped = fault != RollbackFault.RetainWireBeforeGuard && !result.Landed;
                }
                catch (StoreException error) when (fault == RollbackFault.RetainWireBeforeGuard && error.Failure == Failure.Definite &&
                    error.InnerException?.GetBaseException() is PostgresException
                        { SqlState: PostgresErrorCodes.CheckViolation, MessageText: "Qualified actor checkpoint requires a qualified update" })
                {
                    stopped = true;
                }
                var after = await cold.ReadFirstRootAsync(tenant.Tenant, remove.Id, stop);
                Check(stopped && after is { Status: InstanceStatus.Running, TerminalRevision: null, TerminalAt: null } &&
                    after.Revision == current.Revision && after.State == current.State &&
                    (await reads.GetAsync(tenant, remove, stop))?.Header.Status == Contract.ActorStatus.Running &&
                    (await admission.ReadDueRootOperatorAsync(tenant.Tenant, remove.Id, stop)) == command &&
                    (await cold.ReadJournalOfAsync(0, current.Actor.Id, 0, stop)).SequenceEqual(history) &&
                    await db.Partitions.Select(partition => partition.Position).SingleAsync(stop) == lease.Position,
                    $"{fault} rolls back termination, checkpoint, operator input, journal, projection and fence");
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var db = Open();
                await db.Database.ExecuteSqlRawAsync("""
                    DROP TRIGGER IF EXISTS fixture_skip_termination_marker ON "Journal";
                    DROP FUNCTION IF EXISTS fixture_skip_termination_marker();
                    DROP TRIGGER IF EXISTS fixture_change_retirement_marker ON "Instances";
                    DROP TRIGGER IF EXISTS z_fixture_change_retirement_marker ON "Instances";
                    DROP FUNCTION IF EXISTS fixture_change_retirement_marker();
                    """, cleanup.Token);
                await store.ReleaseAsync(0, lease.Epoch, cleanup.Token);
            }
        }

        async Task<Contract.ActorRef> Start(string id, string definition = "wait") =>
            (await runtime.StartAsync(tenant, id, NativeExecutionModel.Start(definition), cancellationToken: stop)).Actor!.Value;

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
            SELECT "State", octet_length("FoundationStateWire") AS "WireBytes", "FoundationHeader" AS "Header",
                "Retired", "Status", "Revision", "FoundationTerminalRevision" AS "TerminalRevision", "FoundationTerminalAt" AS "TerminalAt"
            FROM "Instances" WHERE "Id" = {id}
            """).SingleAsync(stop);
    }

    private enum RollbackFault { SuppressMarker, RetainWireBeforeGuard, RetainWireAfterGuard, ChangeTerminalTime }

    private sealed record PhysicalMarker(string State, int? WireBytes, string? Header, bool Retired, InstanceStatus Status, long Revision, long? TerminalRevision, long? TerminalAt);

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }

    // Seed an actual Native inbox receipt during fixture setup. The termination and protection hooks are unmodified.
    internal sealed class SeededReceipt(NativeExecutionModel model, Func<Contract.ActorRef, bool> retain) :
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
