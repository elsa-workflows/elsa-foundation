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

internal static class PostgresRootRetirementChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootRetirement(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stop = timeout.Token;
        await using (var db = Open())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the retirement fixture uses an empty store");
        var cold = Store();
        await cold.CreateAsync(1);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admission.InstallAsync(stop);
        var tenant = EmbeddedTenant.Bind("retirement");
        var cursor = await admission.OpenJournalCursorAsync(stop);
        var reads = new PostgresRootReads(cold, cursor, 12);
        var allowDelete = false;
        var ingress = await cold.OpenSelectedRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1),
            (scope, _) => ReferenceEquals(scope, tenant), stop,
            (scope, _) => allowDelete && ReferenceEquals(scope, tenant));
        var runtime = new WorkflowRuntime(ingress, reads);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("finish", Wf.Sequence("instant")))
            .Add(Wf.Workflow("protect", Wf.Node<PostgresRootTerminateChecks.InvocationWait>("receive")));
        var model = new ProtectedReceiptModel(new NativeExecutionModel(registry));
        var actorType = new PublicActor([model]);

        var target = await Start("first", "finish");
        var neighbor = await Start("neighbor", "finish");
        var protectedActor = await Start("protected", "protect");
        const string pendingId = "not-due";
        Check((await ingress.SendAsync(tenant, new Contract.Target.Ref(target), pendingId, "ordinary", "queued secret",
            new(DueAt: long.MaxValue - 1), stop)).Outcome == Contract.AdmissionOutcome.Accepted,
            "a future input is admitted before the actor completes");
        var pending = (await admission.ReadPendingDirectAsync(tenant.Tenant, target.Id, stop)).Single();

        EfStore.RootImage before;
        EfStore.RootImage protectedBefore;
        EfStore.RootImage untouched;
        long staleEpoch;
        await using (var owner = await Own("original"))
        {
            before = await Drive(owner, target, InstanceStatus.Finished);
            untouched = await Drive(owner, neighbor, InstanceStatus.Finished);
            var active = await Drive(owner, protectedActor, InstanceStatus.Running);
            var chosen = (await reads.GetAsync(tenant, protectedActor, stop))!;
            Check((await runtime.TerminateAsync(tenant, "stop-protected", chosen, cancellationToken: stop)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "a protected model can reach an ordinary ended checkpoint");
            protectedBefore = await Drive(owner, protectedActor, InstanceStatus.Cancelled);
            Check(protectedBefore.Revision == active.Revision + 1 &&
                protectedBefore.TerminalAt is not null && Json.Read<NativeState>(protectedBefore.State!).Inboxes?.Count == 1,
                "the protected Native state and terminal clock are persisted");
            staleEpoch = ((Worker)owner).Epoch;
        }

        var chosenTarget = (await reads.GetAsync(tenant, target, stop))!;
        var chosenProtected = (await reads.GetAsync(tenant, protectedActor, stop))!;
        var stale = chosenTarget with
        {
            Selection = PostgresRootSelection.SealTerminal(cursor, tenant.Tenant, target.Id, before.TerminalRevision!.Value,
                before.TerminalAt!.Value + 1)
        };
        Check((await Delete(target, "delete-target", chosenTarget.Selection)).Outcome == Contract.AdmissionOutcome.Refused,
            "termination permission alone does not authorize deletion");
        allowDelete = true;
        Check((await Delete(target, "delete-target", stale.Selection)).Outcome == Contract.AdmissionOutcome.Refused &&
              (await Delete(neighbor, "delete-target", chosenTarget.Selection)).Outcome == Contract.AdmissionOutcome.Refused &&
              await admission.ReadDueRootOperatorAsync(tenant.Tenant, target.Id, stop) is null &&
              await admission.ReadDueRootOperatorAsync(tenant.Tenant, neighbor.Id, stop) is null,
            "another target and a stale terminal time leave both queues untouched");
        Check((await Delete(target, "delete-target", chosenTarget.Selection)).Outcome == Contract.AdmissionOutcome.Accepted &&
              (await Delete(protectedActor, "delete-protected", chosenProtected.Selection)).Outcome == Contract.AdmissionOutcome.Accepted,
            "authorized terminal selections admit two exact deletion commands");
        var pendingDelete = (await admission.ReadDueRootOperatorAsync(tenant.Tenant, target.Id, stop))!;
        var pendingProtected = (await admission.ReadDueRootOperatorAsync(tenant.Tenant, protectedActor.Id, stop))!;
        var neighborHistory = await cold.ReadJournalOfAsync(0, untouched.Actor.Id, 0, stop);
        await LateMarkerRefusalRollsBack(before, pendingDelete, pending, suppress: false);
        await LateMarkerRefusalRollsBack(before, pendingDelete, pending, suppress: true);

        var shadow = await OpenShadowAsync();
        await using (var owner = await Own("successor", shadow))
        {
            Check(((Worker)owner).Epoch > staleEpoch, "a successor owns the deletion partition");
            do
            {
                await owner.WokenAsync(null, stop);
                await owner.RunAsync(stop);
            }
            while (!(await Inbox(pendingProtected)).Done || !(await Inbox(pendingDelete)).Done);
        }

        var kept = await cold.ReadFirstRootAsync(tenant.Tenant, protectedActor.Id, stop);
        Check(kept is not null && kept.Revision == protectedBefore.Revision && kept.State == protectedBefore.State &&
              kept.TerminalAt == protectedBefore.TerminalAt && model.ObservedTerminalTime == protectedBefore.TerminalAt &&
              (await cold.ReadJournalOfAsync(0, protectedBefore.Actor.Id, 0, stop)).All(entry => entry.Revision > 0),
            "the model's protection keeps its checkpoint, terminal mark and journal intact");

        await using (var db = Open())
        {
            var marker = await db.Database.SqlQuery<PhysicalMarker>($"""
                SELECT "State", octet_length("FoundationStateWire") AS "WireBytes", "FoundationHeader" AS "Header", "Retired", "Revision"
                FROM "Instances" WHERE "Id" = {before.Actor.Id}
                """).SingleAsync(stop);
            var history = await cold.ReadJournalOfAsync(0, before.Actor.Id, 0, stop);
            var position = await db.Partitions.Select(partition => partition.Position).SingleAsync(stop);
            Check(marker is { State: "null", WireBytes: null, Header: null, Retired: true } && marker.Revision == before.Revision + 1 &&
                history is [{ Revision: -1, Entries: """["delete"]""", Position: var deletion }] && deletion == position,
                "retirement erases the physical state and replaces history with one fenced deletion marker");
            var done = await Inbox(pending);
            Check(done.Done && done.Payload is null && done.Start is null && done.KeepUntil == pending.KeepUntil,
                "the retirement clears future payloads and preserves their done window");
        }

        Check(await reads.GetAsync(tenant, target, stop) is null &&
            (await reads.ListAsync(tenant, new(), cancellationToken: stop)).Items.All(view => view.Header.Ref != target) &&
            (await reads.GetAsync(tenant, neighbor, stop))?.Header.Ref == neighbor &&
            (await cold.ReadJournalOfAsync(0, untouched.Actor.Id, 0, stop)).SequenceEqual(neighborHistory),
            "the committed projection removes only the retired actor");
        Check((await Delete(target, "delete-target", chosenTarget.Selection)) is
                { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var original } && original == target &&
              (await Delete(target, "delete-again", chosenTarget.Selection)).Outcome == Contract.AdmissionOutcome.Refused,
            "the original operation retains its receipt and a fresh deletion is refused");
        Check((await ingress.SendAsync(tenant, new Contract.Target.Ref(target), pendingId, "ordinary", "queued secret",
                  cancellationToken: stop)).Outcome == Contract.AdmissionOutcome.Duplicate &&
              (await ingress.SendAsync(tenant, new Contract.Target.Ref(target), "new-input", "ordinary", "queued secret",
                  cancellationToken: stop)).Outcome == Contract.AdmissionOutcome.Refused,
            "the retired creation replays its original direct receipt and refuses a new input");

        await using (var owner = await Own("same-owner"))
        {
            var same = await Start("same-owner-finish", "finish");
            var sameBefore = await Drive(owner, same, InstanceStatus.Finished);
            var selected = (await reads.GetAsync(tenant, same, stop))!;
            var epoch = ((Worker)owner).Epoch;
            Check((await Delete(same, "same-owner-delete", selected.Selection)).Outcome == Contract.AdmissionOutcome.Accepted &&
                  (await Delete(same, "same-owner-other", selected.Selection)).Outcome == Contract.AdmissionOutcome.Accepted,
                "two distinct qualified deletes are admitted to the retained owner's ended root");
            var command = (await admission.ReadDueRootOperatorAsync(tenant.Tenant, same.Id, stop))!;
            do
            {
                await owner.WokenAsync(null, stop);
                await owner.RunAsync(stop);
            }
            while (!(await Inbox(command)).Done);
            await owner.WokenAsync(null, stop);
            await owner.RunAsync(stop);

            var probe = await Start("after-delete", "finish");
            await Drive(owner, probe, InstanceStatus.Finished);
            Check(((Worker)owner).Epoch == epoch && await reads.GetAsync(tenant, same, stop) is null &&
                  (await cold.ReadJournalOfAsync(0, sameBefore.Actor.Id, 0, stop)) is [{ Revision: -1 }],
                "same-owner deletion uses the committed stamp and drops a second polled delete without resetting");
        }
        return 0;

        async Task<Contract.ActorRef> Start(string id, string definition) =>
            (await runtime.StartAsync(tenant, id, NativeExecutionModel.Start(definition), cancellationToken: stop)).Actor!.Value;

        ValueTask<Contract.Admission> Delete(Contract.ActorRef actor, string operation, string? selection) =>
            ingress.SendAsync(tenant, new Contract.Target.Ref(actor), operation, Contract.Kinds.Delete,
                JsonSerializer.Serialize(new Contract.OperatorCommand(selection)), cancellationToken: stop);

        async Task<EfStore> OpenShadowAsync()
        {
            await using var db = Open();
            var schema = await db.Database.SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"").SingleAsync(stop);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE SCHEMA retirement_shadow;
                CREATE TABLE retirement_shadow."Instances" AS TABLE "Instances";
                """, stop);
            var altered = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE retirement_shadow."Instances" AS target
                SET "State" = source."State", "FoundationStateWire" = source."FoundationStateWire"
                FROM "Instances" AS source WHERE source."Id" = {before.Actor.Id} AND target."Id" = {protectedBefore.Actor.Id}
                """, stop);
            Check(altered == 1, "the shadow source keeps the terminal stamp and omits the protected invocation");
            using var identifiers = new NpgsqlCommandBuilder();
            var shadowConnection = new NpgsqlConnectionStringBuilder(connection)
            {
                SearchPath = "retirement_shadow," + identifiers.QuoteIdentifier(schema)
            };
            var shadowOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(shadowConnection.ConnectionString).Options;
            return new(() => KernelDb.ForPostgresRootRetirement(shadowOptions), postgres: true, fenceInBatch: inBatch)
                { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        }

        async Task LateMarkerRefusalRollsBack(EfStore.RootImage current, Message command, Message future, bool suppress)
        {
            var store = Store();
            await store.BindWorkerPartitionAsync(0, stop);
            var lease = (await store.TakeAsync(0, "late-refusal", Defaults.Lease, stop))!;
            var priorHistory = await cold.ReadJournalOfAsync(0, current.Actor.Id, 0, stop);
            var instance = new InstanceRow(current.Actor.Id, current.Model, current.Definition, current.Status,
                current.State!, current.Revision, null, Creation: current.Actor.Creation);
            var retirement = new TurnCommit(instance.Id, CreationProtocol.Retire(instance), current.Revision,
                [], [command.InboxKey], [], [], [], """["delete"]""", Unsubscribe: true, Delete: true)
                { FoundationRootOperator = command };

            try
            {
                await using var db = Open();
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION fixture_refuse_deletion_marker() RETURNS trigger LANGUAGE plpgsql AS $fn$
                    BEGIN
                        IF TG_ARGV[0] = 'throw' THEN
                            RAISE EXCEPTION 'fixture refused deletion marker' USING ERRCODE = '23514';
                        END IF;
                        RETURN NULL;
                    END
                    $fn$;
                    """, stop);
                await db.Database.ExecuteSqlRawAsync(suppress ? """
                    CREATE TRIGGER fixture_refuse_deletion_marker BEFORE INSERT ON "Journal"
                    FOR EACH ROW WHEN (NEW."Revision" = -1) EXECUTE FUNCTION fixture_refuse_deletion_marker('suppress');
                    """ : """
                    CREATE TRIGGER fixture_refuse_deletion_marker BEFORE INSERT ON "Journal"
                    FOR EACH ROW WHEN (NEW."Revision" = -1) EXECUTE FUNCTION fixture_refuse_deletion_marker('throw');
                    """, stop);
                try
                {
                    var result = await store.CommitAsync(new(0, lease.Epoch, lease.Position, Defaults.Lease, false, [retirement], [], []), stop);
                    if (!suppress || result.Landed) throw new InvalidOperationException("The marker refusal did not roll back the commit.");
                }
                catch (StoreException error) when (!suppress && error.Failure == Failure.Definite &&
                    error.InnerException?.GetBaseException() is PostgresException
                        { SqlState: PostgresErrorCodes.CheckViolation, MessageText: "fixture refused deletion marker" }) { }

                var preserved = await cold.ReadFirstRootAsync(tenant.Tenant, target.Id, stop);
                var waiting = await Inbox(future);
                Check(preserved is not null && preserved.State == current.State && preserved.Revision == current.Revision &&
                    preserved.TerminalAt == current.TerminalAt && (await reads.GetAsync(tenant, target, stop))?.Header.Ref == target &&
                    (await cold.ReadJournalOfAsync(0, current.Actor.Id, 0, stop)).SequenceEqual(priorHistory) &&
                    !(await Inbox(command)).Done && !waiting.Done && waiting.Payload == future.Payload &&
                    await db.Partitions.Select(partition => partition.Position).SingleAsync(stop) == lease.Position,
                    suppress ? "a suppressed marker rolls back the checkpoint, inbox, projection, journal and fence" :
                        "a late marker refusal rolls back the checkpoint, inbox, projection, journal and fence");
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await using var db = Open();
                await db.Database.ExecuteSqlRawAsync("""
                    DROP TRIGGER IF EXISTS fixture_refuse_deletion_marker ON "Journal";
                    DROP FUNCTION IF EXISTS fixture_refuse_deletion_marker();
                    """, cleanup.Token);
                await store.ReleaseAsync(0, lease.Epoch, cleanup.Token);
            }
        }

        async Task<IScopeActivation> Own(string node, EfStore? selectedStore = null)
        {
            var store = selectedStore ?? Store();
            var scope = (await store.BindWorkerPartitionAsync(0, stop)).Scope;
            var adapter = new ActorAdapter(new EfStore.RootHeaders(scope), actorType);
            var host = new DurableHost([new Worker(node, 0, store, adapter, new WorkerOptions { Partitions = 1 })]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture owner could not take its partition.");
        }

        async Task<EfStore.RootImage> Drive(IScopeActivation owner, Contract.ActorRef reference, InstanceStatus status)
        {
            while (true)
            {
                await owner.WokenAsync(null, stop);
                await owner.RunAsync(stop);
                if (await cold.ReadFirstRootAsync(tenant.Tenant, reference.Id, stop) is { } image && image.Status == status)
                    return image;
            }
        }

        async Task<MessageEntity> Inbox(Message message)
        {
            await using var db = Open();
            return await db.Messages.AsNoTracking().SingleAsync(row => row.InboxScope == message.InboxKey.Scope &&
                row.Id == message.InboxKey.Id, stop);
        }
    }

    private sealed record PhysicalMarker(string State, int? WireBytes, string? Header, bool Retired, long Revision);

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }

    private sealed class ProtectedReceiptModel(NativeExecutionModel model) : PublicModel.IExecutionModel, PublicModel.IModelMetadata
    {
        public string Key => model.Key;
        public Type StateType => model.StateType;
        public int StateVersion => model.StateVersion;
        internal long? ObservedTerminalTime { get; private set; }
        public object CreateState(PublicModel.WorkflowStart start) => model.CreateState(start);
        public object UpgradeState(string state, int version) => model.UpgradeState(state, version);
        public ValueTask TurnAsync(PublicModel.ITurn turn, CancellationToken stop) => model.TurnAsync(turn, stop);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken stop) => model.ExecuteJobAsync(job, stop);
        public Contract.RestorePlan RestoreWaits(PublicModel.InstanceInfo instance, object state) => model.RestoreWaits(instance, state);
        public long TerminalGrowth(PublicModel.InstanceInfo instance, object state) => model.TerminalGrowth(instance, state);
        public bool HandlesMetadata(Contract.Message message) => model.HandlesMetadata(message);
        public ValueTask MetadataAsync(PublicModel.ITurn turn, Contract.ActorStatus status, long? endedAt, CancellationToken stop) =>
            model.MetadataAsync(turn, status, endedAt, stop);
        public void TerminateMetadata(object state) => model.TerminateMetadata(state);

        // Deterministic model policy for the physical writer fixture.
        public bool ProtectsDeletion(object state, long? endedAt, long now)
        {
            if (state is not NativeState { Inboxes.Count: > 0 }) return false;
            ObservedTerminalTime = endedAt;
            return endedAt is not null;
        }
    }
}
