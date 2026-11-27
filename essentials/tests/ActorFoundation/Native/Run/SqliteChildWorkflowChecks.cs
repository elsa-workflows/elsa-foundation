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
    internal static async Task<int> RunAsync(bool fenceInBatch)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        var folder = Directory.CreateTempSubdirectory("sqlite-child-workflows-");
        var path = Path.Combine(folder.FullName, "kernel.db");
        var connection = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false };
        var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(connection.ToString()).Options;
        var definitions = new SqliteAdmissionDefinitions();
        KernelDb Open() => KernelDb.ForSqliteRootRuntime(options, definitions, StoreFormat.SqliteRootChildrenQualification);
        var store = new EfStore(Open, postgres: false, fenceInBatch: fenceInBatch);
        var tenant = EmbeddedTenant.Bind("workflow-children");
        var aliases = new WorkflowRegistry()
            .Add(Wf.Workflow("child", Wf.Sequence("child-sequence",
                Wf.Node<PostgresChildWorkflowChecks.ReleaseChild>("gate"), Wf.SetOutput("return", "child-value", "from-child"))))
            .Add(Wf.Workflow("parent", Wf.Flowchart("chart",
                [Wf.Dispatch("dispatch", "child", wait: true), Wf.Set("use-child", "result", Binding.Result("dispatch", "Output.child-value"))],
                [new Connection("dispatch", "use-child", "Completed")])))
            .Add(Wf.Workflow("cancel-parent", Wf.Parallel("race", 1,
                Wf.Dispatch("dispatch", "child", wait: true), Wf.Node<PostgresChildWorkflowChecks.ReleaseChild>("win"))))
            .Add(Wf.Workflow("nested-parent", Wf.Dispatch("dispatch", "parent", wait: true)))
            .Add(Wf.Workflow("bulk-parent", Wf.Node<PostgresChildWorkflowChecks.ReleaseChild>("wait")));
        var model = new Fixture(new WorkflowActor.WorkflowActor([new NativeExecutionModel(aliases)]));
        var loop = new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(25),
            Window = TimeSpan.FromMilliseconds(400)
        };

        try
        {
            var foundation = SqliteEmbeddedFoundation.ForChildQualification(path, "child-owner", model.Type);
            await foundation.InstallShippedAsync(token);
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
            await service.StopAsync(token);

            var replacement = SqliteEmbeddedFoundation.ForChildQualification(path, "replacement-child-owner", model.Type);
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

            var terminalChild = (await reads.GetAsync(tenant, childRef, token))!.Selection;
            await SendAsync(childRef, "delete-child", Contract.Kinds.Delete, Json.Write(new Contract.OperatorCommand(terminalChild)));
            await EventuallyAsync(successor, async () => await reads.GetAsync(tenant, childRef, token), view => view is null);
            Check(await reads.GetAsync(tenant, parentRef, token) is { Header.Status: Contract.ActorStatus.Finished },
                "retiring the attached terminal child preserves the parent continuation");

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
            await WaitAsync(middle, row => row.ParkAt is not null);
            await SendAsync(nestedParent, "terminate-middle", Fixture.TerminateChild, middle.Id);
            await WaitAsync(middle, row => row is { Status: InstanceStatus.Cancelled, ParkAt: null });
            await WaitAsync(descendant, row => row.Status == InstanceStatus.Cancelled);
            await WaitAsync(nestedParent, row => row.Children is null);
            Check(true, "the parent's exact Terminate closes the parked child's lifecycle and cascades to its live descendant");
            await successor.StopAsync(token);

            var shipped = new SqliteEmbeddedFoundation(path, "other-format", model.Type);
            var refused = false;
            try { await shipped.InstallShippedAsync(token); }
            catch (StoreFormatException) { refused = true; }
            Check(refused, "the shipped SQLite installer refuses the private child profile");
            return 0;

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
                var reference = await db.Database.SqlQuery<string>($"""
                    SELECT "Reference" AS "Value" FROM "FoundationRootBirths" WHERE "Actor" = {exact.Id} AND "Tenant" = {tenant.Tenant}
                    """).SingleAsync(token);
                Contract.ActorRef childRef = new(reference);
                await WaitAsync(childRef, row => row.Status == InstanceStatus.Running);
                return (parent, childRef);
            }

            async Task<EfStore.RootImage> WaitAsync(Contract.ActorRef target, Func<EfStore.RootImage, bool> ready) =>
                (await EventuallyAsync(successor, () => store.ReadFirstRootAsync(tenant.Tenant, target.Id, token),
                    row => row is not null && ready(row)))!;

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
