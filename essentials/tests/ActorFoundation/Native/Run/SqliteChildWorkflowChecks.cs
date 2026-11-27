using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class SqliteChildWorkflowChecks
{
    internal static async Task<int> RunAsync(bool fenceInBatch)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
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
                [new Connection("dispatch", "use-child", "Completed")])));
        var model = new WorkflowActor.WorkflowActor([new NativeExecutionModel(aliases)]);
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
            var replacementIngress = await replacement.OpenIngressAsync(token);
            Check(await replacementIngress.SendAsync(tenant, new Contract.Target.Ref(childRef), "release-1", CompositeSignal.Kind,
                Json.Write(new CompositeSignal(gate.Id, "release", "continue")), cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the replacement owner accepts the exact child signal");
            var finished = await EventuallyAsync(successor, () => store.ReadFirstRootAsync(tenant.Tenant, parentRef.Id, token),
                row => row is { Status: InstanceStatus.Finished });
            Check(finished is { Revision: 2, Children: null } &&
                Json.Read<NativeState>(finished.State!).Variables.GetValueOrDefault("result")?.GetValue<string>() == "from-child",
                "the final child output reaches the committed Flowchart continuation exactly once");
            Check(await store.ReadFirstRootAsync(tenant.Tenant, childRef.Id, token) is { Status: InstanceStatus.Finished, Revision: 2 } &&
                !await db.Messages.AnyAsync(message => !message.Done, token),
                "the child finishes once and creator and notice are settled");
            await successor.StopAsync(token);

            var shipped = new SqliteEmbeddedFoundation(path, "other-format", model.Type);
            var refused = false;
            try { await shipped.InstallShippedAsync(token); }
            catch (StoreFormatException) { refused = true; }
            Check(refused, "the shipped SQLite installer refuses the private child profile");
            return 0;

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
