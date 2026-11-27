using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using Fixture = Kernel.Native.Run.PostgresChildWorkflowChecks.ChildFixtureActor;

namespace Kernel.Native.Run;

internal static class SqliteChildWorkflowChecks
{
    internal static async Task<int> RunAsync(bool fenceInBatch, bool shipped = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        var folder = Directory.CreateTempSubdirectory("sqlite-child-workflows-");
        var path = Path.Combine(folder.FullName, "kernel.db");
        var connection = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false };
        var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(connection.ToString()).Options;
        var definitions = new SqliteAdmissionDefinitions();
        KernelDb Open() => KernelDb.ForSqliteRootRuntime(options, definitions, shipped ? null : StoreFormat.SqliteRootChildrenQualification);
        var store = new EfStore(Open, postgres: false, fenceInBatch: fenceInBatch);
        var tenant = EmbeddedTenant.Bind("workflow-children");
        const string creatorFaultChild = "creator-fault-child";
        var aliases = new WorkflowRegistry()
            .Add(Wf.Workflow("child", Wf.Sequence("child-sequence",
                Wf.Node<PostgresChildWorkflowChecks.ReleaseChild>("gate"), Wf.SetOutput("return", "child-value", "from-child"))))
            .Add(Wf.Workflow("parent", Wf.Flowchart("chart",
                [Wf.Dispatch("dispatch", "child", wait: true), Wf.Set("use-child", "result", Binding.Result("dispatch", "Output.child-value"))],
                [new Connection("dispatch", "use-child", "Completed")])))
            .Add(Wf.Workflow("cancel-parent", Wf.Parallel("race", 1,
                Wf.Dispatch("dispatch", "child", wait: true), Wf.Node<PostgresChildWorkflowChecks.ReleaseChild>("win"))))
            .Add(Wf.Workflow("nested-parent", Wf.Dispatch("dispatch", "parent", wait: true)))
            .Add(Wf.Workflow("creator-fault-parent", Wf.Dispatch("dispatch", creatorFaultChild, wait: true)))
            .Add(Wf.Workflow(creatorFaultChild, Wf.SetOutput("finish", "child-value", "unused")))
            .Add(Wf.Workflow("job-parent", Wf.Dispatch("dispatch", "job-child", wait: true)))
            .Add(Wf.Workflow("job-child", Wf.Http("fetch", "https://partner.example/stuck", timeout: TimeSpan.FromMinutes(2))))
            .Add(Wf.Workflow("bulk-parent", Wf.Node<PostgresChildWorkflowChecks.ReleaseChild>("wait")));
        var partner = new PostgresChildWorkflowChecks.BlockingPartner();
        using var http = new HttpClient(partner);
        var model = new Fixture(new WorkflowActor.WorkflowActor([new NativeExecutionModel(aliases,
            new NativeExecutionOptions { Http = new NativeHttpOptions { Client = http } })]), creatorFaultChild);
        var loop = new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(25),
            Window = TimeSpan.FromMilliseconds(400)
        };

        try
        {
            var foundation = OpenFoundation("child-owner");
            await foundation.InstallShippedAsync(token);
            if (shipped)
                Check(SqliteEmbeddedHosting.TakesChildren, "the SQLite shipped host advertises owned child starts");
            await using var service = foundation.CreateHostedService(model, Escalate, loop, fenceInBatch);
            await service.StartAsync(token);
            var ingress = await foundation.OpenIngressAsync(token);
            var admitted = await ingress.SendAsync(tenant, new Contract.Target.Start(
                WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start("parent"))), "parent-1", Contract.Kinds.Start, cancellationToken: token);
            Check(admitted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the authored parent was admitted");
            var parentRef = admitted.Actor!.Value;
            var waiting = await EventuallyAsync(service, () => store.ReadFirstRootAsync(tenant.Tenant, parentRef.Id, token),
                row => row is { Status: InstanceStatus.Running, Children: not null });
            await using var db = Open();
            var childId = await EventuallyAsync(service, () => db.Database.SqlQuery<string>($"""
                SELECT "FoundationReference" AS "Value" FROM "Instances"
                WHERE "Parent" = {waiting!.Actor.Id} AND "FoundationTenant" = {tenant.Tenant}
                """).SingleOrDefaultAsync(token), value => value is not null);
            var childRef = new Contract.ActorRef(childId!);
            var child = await EventuallyAsync(service, () => store.ReadFirstRootAsync(tenant.Tenant, childRef.Id, token),
                row => row is { Status: InstanceStatus.Running });
            var gate = Json.Read<NativeState>(child!.State!).Frames.Values.Single(frame => frame.Node == "gate");
            Check(waiting!.Revision == 1 && child.Revision == 1 && child.Parent == waiting.Actor.Id,
                "the parent waits at its first committed revision and the child commits its own identity");

            var reads = foundation.CreateReads();
            Check(await reads.GetAsync(tenant, childRef, token) is { Header.Parent: { } parent } && parent == parentRef &&
                (await reads.ListAsync(tenant, new(), cancellationToken: token)).Items
                    .Any(item => item.Header.Ref == childRef && item.Header.Parent == parentRef),
                "point and paged reads return the exact parent reference");
            Check(await reads.GetAsync(ContractScopes.Bind("other-tenant"), childRef, token) is null,
                "another tenant cannot see the child");
            var birth = await ChildBirthProofChecks.ReadBirthAsync(Open, tenant, parentRef, childRef, 1, token);
            var abandoned = await ChildBirthProofChecks.PlanAbsentAsync(Open, birth, long.MaxValue, long.MaxValue, token);
            Check(birth.Reference == childRef &&
                await birth.ConfirmAsync(Open, tenant, token) == FoundationChildBirthProof.Unresolved &&
                await abandoned.ConfirmAsync(Open, tenant, token) == FoundationChildBirthProof.Unresolved,
                "a live child and an absent issuance from a still-eligible parent both retain their pins");
            await service.StopAsync(token);

            var replacement = OpenFoundation("replacement-child-owner");
            await replacement.InstallShippedAsync(token);
            await using var successor = replacement.CreateHostedService(model, Escalate, loop, fenceInBatch);
            await successor.StartAsync(token);
            var replacementIngress = await replacement.OpenIngressWithOperatorsAsync(
                (scope, _, kind) => ReferenceEquals(scope, tenant) &&
                    kind is Contract.Kinds.Terminate or Contract.Kinds.Delete or Contract.Kinds.Recover, token);
            await SendAsync(childRef, "arm-child-fault", Fixture.ArmFault);
            var parked = await WaitAsync(childRef, row => row.ParkAt is not null);
            var waitingAfterPark = await WaitAsync(parentRef, row => row.Revision > 1);
            Check(parked.Revision == 3 && waitingAfterPark is { Status: InstanceStatus.Running, Revision: 2, Children: not null },
                "the timer parks the child and delivers a non-final notice without advancing Dispatch");
            Check(await abandoned.ConfirmAsync(Open, tenant, token) == FoundationChildBirthProof.IssuanceFencedAbsent &&
                await birth.ConfirmAsync(Open, tenant, token) == FoundationChildBirthProof.Unresolved,
                "a replacement epoch proves exact absence without releasing a committed parked child");
            var displaced = await ChildBirthProofChecks.PlanAbsentAsync(Open, birth, 3, long.MaxValue - 1, token, atCurrentEpoch: true);
            Check(await displaced.ConfirmAsync(Open, tenant, token) == FoundationChildBirthProof.Unresolved,
                "the replacement epoch can still commit its next exact parent revision");
            var childSelection = (await reads.GetAsync(tenant, childRef, token))!.Selection;
            await SendAsync(childRef, "recover-child", Contract.Kinds.Recover, Json.Write(new Contract.OperatorCommand(childSelection)));
            await WaitAsync(childRef, row => row is { Status: InstanceStatus.Running, Revision: 4 });

            Check(await replacementIngress.SendAsync(tenant, new Contract.Target.Ref(childRef), "release-1", CompositeSignal.Kind,
                Json.Write(new CompositeSignal(gate.Id, "release", "continue")), cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the replacement owner accepts the exact child signal");
            var finished = await EventuallyAsync(successor, () => store.ReadFirstRootAsync(tenant.Tenant, parentRef.Id, token),
                row => row is { Status: InstanceStatus.Finished });
            Check(finished is { Revision: 3, Children: null } &&
                Json.Read<NativeState>(finished.State!).Variables.GetValueOrDefault("result")?.GetValue<string>() == "from-child",
                "the final child output reaches the committed Flowchart continuation exactly once");
            Check(await store.ReadFirstRootAsync(tenant.Tenant, childRef.Id, token) is { Status: InstanceStatus.Finished, Revision: 5 } &&
                !await db.Messages.AnyAsync(message => !message.Done, token),
                "the child finishes once and creator and notice are settled");

            Check(await ChildBirthProofChecks.ReadEpochAsync(Open, displaced, token) == displaced.Body.Fence().Epoch &&
                await displaced.ConfirmAsync(Open, tenant, token) == FoundationChildBirthProof.IssuanceFencedAbsent &&
                await birth.ConfirmAsync(Open, tenant, token) == FoundationChildBirthProof.Unresolved,
                "the committed parent checkpoint disarms the absent issue in the same epoch; child finish alone does not release");
            var terminalChild = (await reads.GetAsync(tenant, childRef, token))!.Selection;
            await SendAsync(childRef, "delete-child", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(terminalChild)));
            await EventuallyAsync(successor, async () => await reads.GetAsync(tenant, childRef, token), view => view is null);
            Check(await reads.GetAsync(tenant, parentRef, token) is { Header.Status: Contract.ActorStatus.Finished },
                "retiring the attached terminal child preserves the parent continuation");
            Check(await birth.ConfirmAsync(Open, tenant, token) == FoundationChildBirthProof.BirthRetired &&
                await ChildBirthProofChecks.InvalidFingerprint(birth).ConfirmAsync(Open, tenant, token) == FoundationChildBirthProof.Unresolved &&
                await ChildBirthProofChecks.InvalidSignature(birth).ConfirmAsync(Open, tenant, token) == FoundationChildBirthProof.Unresolved &&
                await birth.ConfirmAsync(Open, ContractScopes.Bind("other-tenant"), token) == FoundationChildBirthProof.Unresolved,
                "only the exact committed and retired birth authorizes release");

            var raceParent = await StartAsync("cancel-parent", "race-parent");
            var (raceWait, raceChild) = await ChildAsync(raceParent);
            var win = Json.Read<NativeState>(raceWait.State!).Frames.Values.Single(frame => frame.Node == "win");
            await SendAsync(raceParent, "win-race", CompositeSignal.Kind, Json.Write(new CompositeSignal(win.Id, "release", "continue")));
            var cancelled = await WaitAsync(raceChild, row => row.Status == InstanceStatus.Cancelled);
            var endedRace = await WaitAsync(raceParent, row => row is { Status: InstanceStatus.Finished, Children: null });
            Check(cancelled.Revision == 2 && endedRace.Revision == 2, "the losing child cancels and its final notice closes the terminal registry without a new revision");

            var nestedParent = await StartAsync("nested-parent", "nested-parent");
            var (_, middle) = await ChildAsync(nestedParent);
            var (_, descendant) = await ChildAsync(middle);
            await SendAsync(middle, "park-middle", Fixture.Fault);
            var retained = await WaitAsync(middle, row => row.ParkAt is not null);
            var target = (await reads.GetAsync(tenant, middle, token))!.Selection;
            await SendAsync(middle, "delete-attached-park", Contract.Kinds.Delete,
                Json.Write(new Contract.OperatorCommand(target)));
            await WaitAsync(middle, row => row.ParkAt == retained.ParkAt && row.Revision == retained.Revision + 1);
            await SendAsync(middle, "delete-after-terminate-attached-park", Contract.Kinds.Terminate,
                Json.Write(new Contract.OperatorCommand(target, ThenDelete: true, ExpectedTarget: middle)));
            await WaitAsync(middle, row => row.ParkAt == retained.ParkAt && row.Revision == retained.Revision + 2);
            Check(true, "the SQLite owner durably refuses Delete and compound retirement of an attached Park");
            await SendAsync(nestedParent, "terminate-middle", Fixture.TerminateChild, middle.Id);
            await WaitAsync(middle, row => row is { Status: InstanceStatus.Cancelled, ParkAt: null });
            await WaitAsync(descendant, row => row.Status == InstanceStatus.Cancelled);
            await WaitAsync(nestedParent, row => row.Children is null);
            Check(true, "the parent's exact Terminate closes the parked child's lifecycle and cascades to its live descendant");

            var jobParent = await StartAsync("job-parent", "job-parent");
            var (_, jobChild) = await ChildAsync(jobParent);
            await partner.Entered.Task.WaitAsync(token);
            await SendAsync(jobChild, "park-job-child", Fixture.Fault);
            var jobPark = await WaitAsync(jobChild, row => row.ParkAt is not null && row.Jobs is not null);
            var parkSelection = (await reads.GetAsync(tenant, jobChild, token))!.Selection;
            await SendAsync(jobChild, "terminate-job-child", Contract.Kinds.Terminate, Json.Write(new Contract.OperatorCommand(parkSelection)));
            await WaitAsync(jobChild, row => row is { Status: InstanceStatus.Cancelled, Jobs: null, ParkAt: null });
            await WaitAsync(jobParent, row => row.Children is null);
            await partner.Cancelled.Task.WaitAsync(token);
            Check(!await db.Messages.AnyAsync(row => row.Target == jobPark.Actor.Id && row.InboxScope == EfStore.RootTimedJobScope, token),
                "selected Terminate closes the live job footprint and cancels the entered callback");

            var earlyParent = await StartAsync("creator-fault-parent", "creator-fault-parent");
            var earlyRegistered = await WaitAsync(earlyParent, row => row.Children is not null);
            var earlyChildRef = await ReferenceAsync(ChildRegistry.Read(earlyRegistered.Children).Single());
            var earlyChild = await WaitAsync(earlyChildRef, row => row is { Revision: 1, ParkAt: not null });
            var earlyNotice = await WaitAsync(earlyParent, row => row.Revision == 2);
            Check(earlyChild.Parent == earlyRegistered.Actor.Id && earlyNotice is { Status: InstanceStatus.Running, Children: not null } &&
                await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "Messages"
                        WHERE "Target" = {earlyChild.Actor.Id} AND "InboxScope" = 0 AND "Done"
                            AND "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}) AS "Value"
                    """).SingleAsync(token), "a fault in the child's creator turn retains its creator and commits a non-final parent notice");
            var creatorPark = await ChildBirthProofChecks.ReadBirthAsync(Open, tenant, earlyParent, earlyChildRef, 1, token);
            Check(await creatorPark.ConfirmAsync(Open, tenant, token) == FoundationChildBirthProof.Unresolved,
                "a child parked at its first creator checkpoint keeps the existing birth pin");
            await SendAsync(earlyParent, "terminate-faulted-creator", Fixture.TerminateChild, earlyChildRef.Id);
            await WaitAsync(earlyChildRef, row => row is { Status: InstanceStatus.Cancelled, ParkAt: null });
            await WaitAsync(earlyParent, row => row.Children is null);

            var bulkParent = await StartAsync("bulk-parent", "bulk-parent");
            await WaitAsync(bulkParent, row => row.Status == InstanceStatus.Running);
            await SendAsync(bulkParent, "spawn-page-overflow", Fixture.Spawn);
            var bulk = await WaitAsync(bulkParent, row => ChildRegistry.Read(row.Children).Count > CascadeProtocol.PageLimit);
            var lastChild = await ReferenceAsync(ChildRegistry.Read(bulk.Children)[^1]);
            var bulkSelection = (await reads.GetAsync(tenant, bulkParent, token))!.Selection;
            await SendAsync(bulkParent, "terminate-bulk", Contract.Kinds.Terminate, Json.Write(new Contract.OperatorCommand(bulkSelection)));
            await WaitAsync(bulkParent, row => row is { Status: InstanceStatus.Cancelled, Children: null });
            await WaitAsync(lastChild, row => row.Status == InstanceStatus.Cancelled);
            Check(await db.Database.SqlQuery<long>($"""
                SELECT count(*) AS "Value" FROM "FoundationRootChildCascades" WHERE "Reference" = {bulkParent.Id}
                """).SingleAsync(token) == 0, "the persisted second cascade page reaches its last live child and closes the ledger");

            var guardParent = await StartAsync("parent", "registry-guard");
            var (_, guardChild) = await ChildAsync(guardParent);
            var guardRunning = await WaitAsync(guardChild, row => row.Status == InstanceStatus.Running);
            await SendAsync(guardParent, "end-with-child", Fixture.Finish);
            var guardEnded = await WaitAsync(guardParent, row => row is { Status: InstanceStatus.Finished, Children: not null });
            await successor.StopAsync(token);
            await CheckRegistryGuardAsync(guardParent, guardRunning, guardEnded);

            var mismatched = shipped
                ? SqliteEmbeddedFoundation.ForChildQualification(path, "other-format", model.Type)
                : new SqliteEmbeddedFoundation(path, "other-format", model.Type);
            var refused = false;
            try { await mismatched.InstallShippedAsync(token); }
            catch (StoreFormatException) { refused = true; }
            Check(refused, "the SQLite installer refuses a child store from a different protocol");
            return 0;

            SqliteEmbeddedFoundation OpenFoundation(string node) => shipped
                ? new SqliteEmbeddedFoundation(path, node, model.Type)
                : SqliteEmbeddedFoundation.ForChildQualification(path, node, model.Type);

            async Task<Contract.ActorRef> StartAsync(string definition, string operation)
            {
                var result = await replacementIngress.SendAsync(tenant, new Contract.Target.Start(
                    WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start(definition))), operation, Contract.Kinds.Start, cancellationToken: token);
                if (result is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } reference })
                    throw new InvalidOperationException($"The SQLite host returned {result.Outcome} for {operation}: {result.Reason}.");
                return reference;
            }

            async Task SendAsync(Contract.ActorRef target, string operation, string kind, string? payload = null)
            {
                var result = await replacementIngress.SendAsync(tenant, new Contract.Target.Ref(target), operation, kind, payload, cancellationToken: token);
                if (result.Outcome != Contract.AdmissionOutcome.Accepted)
                    throw new InvalidOperationException($"The SQLite host returned {result.Outcome} for {operation}: {result.Reason}.");
            }

            async Task<(EfStore.RootImage Parent, Contract.ActorRef Child)> ChildAsync(Contract.ActorRef parentRef)
            {
                var parent = await WaitAsync(parentRef, row => row.Children is not null);
                var exact = ChildRegistry.Read(parent.Children).Single();
                var childRef = await ReferenceAsync(exact);
                await WaitAsync(childRef, row => row.Status == InstanceStatus.Running);
                return (parent, childRef);
            }

            async Task<Contract.ActorRef> ReferenceAsync(CreationAddress exact) => new(await db.Database.SqlQuery<string>($"""
                SELECT "Reference" AS "Value" FROM "FoundationRootBirths" WHERE "Actor" = {exact.Id} AND "Tenant" = {tenant.Tenant}
                """).SingleAsync(token));

            async Task<EfStore.RootImage> WaitAsync(Contract.ActorRef target, Func<EfStore.RootImage, bool> ready) =>
                (await EventuallyAsync(successor, () => store.ReadFirstRootAsync(tenant.Tenant, target.Id, token),
                    row => row is not null && ready(row)))!;

            async Task CheckRegistryGuardAsync(Contract.ActorRef parent, EfStore.RootImage child, EfStore.RootImage ended)
            {
                await using var witness = await db.Database.BeginTransactionAsync(token);
                var terminalRevision = child.Revision + 1;
                Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Instances" SET "Status" = {(int)InstanceStatus.Finished}, "Revision" = {terminalRevision},
                        "FoundationWriteRevision" = {terminalRevision}, "FoundationTerminalAt" = 1,
                        "FoundationTerminalRevision" = {terminalRevision}
                    WHERE "Id" = {child.Actor.Id}
                    """, token) == 1, "the negative witness names the exact child's terminal checkpoint");
                var handle = CreationProtocol.TerminalNoticeId(child.Actor.Id, child.Actor.Creation, terminalRevision);
                var key = parent.Id + "/child/notice/" + handle;
                var body = new FoundationChildNotice(child.Actor.Id, InstanceStatus.Finished, null, child.Actor.Creation, Final: true);
                (string Id, object Payload)[] invalid =
                [
                    (key + "0", DurableKeyJson.Write(body)),
                    (key, DurableKeyJson.Write(body with { Final = false })),
                    (key, body)
                ];
                foreach (var (id, payload) in invalid)
                {
                    await witness.CreateSavepointAsync("notice", token);
                    var wire = "!kernel-child-notice/1:" + DurableKeyJson.Write(new { Id = handle, Payload = payload, Final = true });
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "Messages" ("Id","Target","Partition","InboxScope","Kind","Due","Start","Sender","Done","Seen",
                            "Attempts","Trial","JobGeneration","Seq")
                        VALUES ({id}, {ended.Actor.Id}, {new InstanceRef(ended.Actor.Id).Partition}, {InboxKey.FoundationOccurrenceScope},
                            {Kinds.ChildFinished}, 0, {wire}, {child.Actor.Id}, false, 0, 0, 0, 0,
                            (SELECT coalesce(max("Seq"),0) + 1 FROM "Messages"))
                        """, token);
                    var rejected = false;
                    try
                    {
                        await db.Database.ExecuteSqlInterpolatedAsync($"""
                            UPDATE "Instances" SET "Children" = NULL WHERE "Id" = {ended.Actor.Id}
                            """, token);
                    }
                    catch (SqliteException error) when (error.SqliteErrorCode == 19 &&
                        error.Message.Contains("Qualified actor checkpoint requires a qualified update", StringComparison.Ordinal)) { rejected = true; }
                    await witness.RollbackToSavepointAsync("notice", token);
                    await witness.ReleaseSavepointAsync("notice", token);
                    if (!rejected) throw new InvalidOperationException("An invalid final child occurrence could shrink the terminal registry.");
                }
                await witness.RollbackAsync(token);
                Check(true, "the guard refuses wrong occurrence identity, non-final content and an object in place of the encoded payload");
            }

            async Task<T> EventuallyAsync<T>(SqliteEmbeddedHostedService owner, Func<Task<T>> read, Func<T, bool> match)
            {
                while (true)
                {
                    if (owner.LastError is { } fault) throw new InvalidOperationException("The SQLite owner failed.", fault);
                    var value = await read();
                    if (match(value)) return value;
                    await Task.Delay(15, token);
                }
            }
        }
        finally { folder.Delete(recursive: true); }
    }

    private static void Escalate(string error) => throw new InvalidOperationException(error);

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
