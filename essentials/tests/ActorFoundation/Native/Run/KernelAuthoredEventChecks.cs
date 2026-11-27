using System.Text.Json.Nodes;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.Native;
using Kernel.Runtime.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Kernel.Native.Run;

internal static class KernelAuthoredEventChecks
{
    internal static async Task<int> RunAsync(string provider, bool inBatch)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var token = stop.Token;
        var scope = ContractScopes.Bind("authored-events");
        var other = ContractScopes.Bind("other");
        var registry = new WorkflowRegistry();
        registry.Add(NativeWaiter("native-wait", "Order"));
        registry.Add(NativeWaiter("other-case", "order"));
        registry.Add(NativeWaiter("overflow-wait", "overflow"));
        registry.Add(NativeWaiter("late", "miss"));
        registry.Add(Producer("publish", "Order", "first", "second"));
        registry.Add(Producer("empty", "miss", "before-wait"));
        registry.Add(NativeWaiter("rollback-wait", "rollback"));
        registry.Add(Wf.Workflow("detached-parent", Wf.Dispatch("dispatch", "overflow-publish", wait: false)));
        registry.Add(Wf.Workflow("overflow-publish", Wf.Sequence("root",
            Wf.Publish("fits", "rollback", "must-never-deliver"),
            Wf.Publish("too-large", "overflow", new string('x', 6 * 1024 * 1024)))));
        var chart = ExecutableOf("chart-wait", "chart-wait", Node("chart", Types.Flowchart,
            structure: Structure("elsa.flowchart.structure", new
            {
                startNodeId = "wait",
                connections = new[] { new { source = new { nodeId = "wait", port = "Done" }, target = new { nodeId = "seen" } } }
            }), slots: [Slot("Flowchart.Activities",
                Node("wait", Types.Event, [Literal("EventName", "Order", "String"), Literal("CanStartWorkflow", false, "Boolean")]),
                SetOutput("seen", "seen", Result("value", "wait", "Payload")))]));
        registry.Add(NativeExecutableMapper.Map(chart));
        var model = new PublicWorkflow([new NativeExecutionModel(registry)]);

        var directory = Directory.CreateTempSubdirectory("authored-events");
        try
        {
            var (open, ingress, reads, owner) = await OpenAsync(model, provider, Path.Combine(directory.FullName, "store.db"), inBatch, token);
            await using var owned = owner;
            var native = await StartAsync(scope, "native-wait", "native");
            var flowchart = await StartAsync(scope, "chart-wait", "flowchart");
            var wrongCase = await StartAsync(scope, "other-case", "case");
            var wrongTenant = await StartAsync(other, "native-wait", "cross-tenant");
            await UntilAsync(async () => await WaitCountAsync(scope, "Order") == 2 &&
                await WaitCountAsync(scope, "order") == 1 && await WaitCountAsync(other, "Order") == 1);
            Check(true, "Native and mapped Flowchart have committed exact ordinary waits before publication");

            var publisher = await StartAsync(scope, "publish", "publication");
            await UntilAsync(async () => (await ViewAsync(scope, publisher))?.Header.Status == Contract.ActorStatus.Finished);
            await CheckOutputAsync(native, "first");
            await CheckOutputAsync(flowchart, "first");
            Check((await ViewAsync(scope, wrongCase))?.Header.Status == Contract.ActorStatus.Running &&
                  (await ViewAsync(other, wrongTenant))?.Header.Status == Contract.ActorStatus.Running,
                "publication reaches both exact waiters in effect order and keeps other case and tenant waiting");

            var duplicate = await ingress.SendAsync(scope, new Contract.Target.Start(Spec("publish")), "publication", Contract.Kinds.Start,
                cancellationToken: token);
            await using (var db = open())
            {
                var receipts = await db.Events.Where(e => e.Key == "authored-events_event:Order").ToListAsync(token);
                Check(duplicate.Outcome == Contract.AdmissionOutcome.Duplicate && duplicate.Actor == publisher &&
                    receipts.Count == 2 && receipts.All(e => e.Id.StartsWith('~') && e.Payload is null),
                    "a replayed creator resolves to the same publisher and the two authored receipts have reserved stable IDs");
            }

            var empty = await StartAsync(scope, "empty", "no-wait");
            await UntilAsync(async () => (await ViewAsync(scope, empty))?.Header.Status == Contract.ActorStatus.Finished);
            var late = await StartAsync(scope, "late", "late-wait");
            await UntilAsync(async () => await WaitCountAsync(scope, "miss") == 1);
            Check((await ViewAsync(scope, late))?.Header.Status == Contract.ActorStatus.Running,
                "a completed publication with no eligible waits stays settled when an ordinary wait later registers");

            var overflowWaits = new List<Contract.ActorRef>();
            for (var i = 0; i < 3; i++) overflowWaits.Add(await StartAsync(scope, "overflow-wait", "overflow-" + i));
            var rolledBack = await StartAsync(scope, "rollback-wait", "rollback");
            await UntilAsync(async () => await WaitCountAsync(scope, "overflow") == 3 &&
                await WaitCountAsync(scope, "rollback") == 1);
            var oversized = await StartAsync(scope, "overflow-publish", "oversized");
            await CheckParkAndRollbackAsync(oversized);

            var issuingParent = await StartAsync(scope, "detached-parent", "detached");
            await UntilAsync(async () => (await ViewAsync(scope, issuingParent))?.Header.Status == Contract.ActorStatus.Finished);
            string? detached = null;
            await UntilAsync(async () =>
            {
                await using var db = open();
                detached = await db.Database.SqlQuery<string>($"""
                    SELECT child."FoundationReference" AS "Value"
                    FROM "Instances" parent
                    JOIN "Messages" creator ON creator."Sender" = parent."Id" AND creator."InboxScope" = 0
                        AND creator."Kind" = {Contract.Kinds.Start} AND creator."Done" AND creator."FoundationKeptBy" = 0
                    JOIN "Instances" child ON child."Id" = creator."Target" AND child."Partition" = creator."Partition"
                    WHERE parent."FoundationReference" = {issuingParent.Id} AND parent."FoundationTenant" = {scope.Tenant}
                        AND child."FoundationTenant" = parent."FoundationTenant" AND child."Parent" IS NULL
                        AND child."FoundationParkAt" IS NOT NULL
                    """).SingleOrDefaultAsync(token);
                return detached is not null;
            });
            var detachedPublisher = new Contract.ActorRef(detached!);
            Check((await ViewAsync(scope, detachedPublisher))?.Header.Parent is null,
                "the detached creator has the exact retained issuing sender and no parent attachment");
            await CheckParkAndRollbackAsync(detachedPublisher);
            foreach (var waiter in overflowWaits.Append(rolledBack))
                if ((await ViewAsync(scope, waiter))?.Header.Status != Contract.ActorStatus.Running)
                    throw new InvalidOperationException("The refused publisher woke a member of the rolled-back cohort.");
            Check(true, "every waiter from the refused turn remains available to a fresh publication");
            await stop.CancelAsync();
            return 0;

            async Task<Contract.ActorRef> StartAsync(Contract.BoundScope tenant, string workflow, string operation)
            {
                var result = await ingress.SendAsync(tenant, new Contract.Target.Start(Spec(workflow)), operation, Contract.Kinds.Start,
                    cancellationToken: token);
                return result is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } reference } ? reference :
                    throw new InvalidOperationException($"Start {workflow} was refused: {result}");
            }

            async ValueTask<Contract.ActorView?> ViewAsync(Contract.BoundScope tenant, Contract.ActorRef reference) =>
                await reads.GetAsync(tenant, reference, token);

            async Task CheckOutputAsync(Contract.ActorRef actor, string value)
            {
                Contract.ActorView? view = null;
                await UntilAsync(async () => (view = await ViewAsync(scope, actor))?.Header.Status == Contract.ActorStatus.Finished);
                var output = await ((Contract.IActorTerminalOutputReads)reads)
                    .ReadTerminalOutputAsync(scope, actor, view!.Header.EndedAt!.Value, includeValues: true, token);
                Check(output?.Values is { } values && JsonNode.Parse(values)?["seen"]?.GetValue<string>() == value,
                    "the first exact received event carries the authored payload to the committed output");
            }

            async Task CheckParkAndRollbackAsync(Contract.ActorRef publisher)
            {
                await UntilAsync(async () => (await ViewAsync(scope, publisher))?.Park is not null);
                await using var db = open();
                Check((await ViewAsync(scope, publisher))?.Header.Fault?.Contains("atomic delivery limit", StringComparison.Ordinal) == true &&
                      !await db.Events.AnyAsync(e => e.Key == "authored-events_event:overflow" ||
                          e.Key == "authored-events_event:rollback", token) &&
                      !await db.Messages.AnyAsync(m => m.Kind == Contract.Kinds.Event &&
                          m.Start != null && m.Start.Contains("authored-events_event:rollback"), token),
                    "an oversized fanout Parks the publisher and rolls back an earlier event receipt and exact delivery in the same turn");
            }

            async Task<int> WaitCountAsync(Contract.BoundScope tenant, string key)
            {
                await using var db = open();
                return await db.Subscriptions.CountAsync(s => s.Key == tenant.Tenant + "_event:" + key, token);
            }

            async Task UntilAsync(Func<Task<bool>> done)
            {
                while (!await done()) await Task.Delay(20, token);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static Workflow NativeWaiter(string id, string name) => Wf.Workflow(id, Wf.Sequence("root",
        Wf.Event("wait", name), Wf.SetOutput("seen", "seen", Binding.Result("wait", "Payload"))));

    private static Workflow Producer(string id, string key, params string[] payloads) => Wf.Workflow(id,
        Wf.Sequence("root", payloads.Select((value, index) => Wf.Publish("publish-" + index, key, value)).ToArray()));

    private static Contract.StartSpec Spec(string workflow) => PublicWorkflow.Start(NativeExecutionModel.Start(workflow));

    private static async Task<(Func<KernelDb> Open, Contract.IIngress Ingress, Contract.IActorReads Reads, IAsyncDisposable Owner)>
        OpenAsync(PublicWorkflow model, string provider, string path, bool inBatch, CancellationToken token)
    {
        const string node = "authored-event-owner";
        var loop = new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(25),
            Window = TimeSpan.FromMilliseconds(400)
        };
        static void EndProcess(string reason) => throw new InvalidOperationException(reason);

        if (provider == "sqlite")
        {
            var sqlite = SqliteEmbeddedFoundation.ForAuthoredEventsQualification(path, node, model.Type);
            await sqlite.InstallShippedAsync(token);
            var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(new SqliteConnectionStringBuilder
                { DataSource = path, Pooling = false }.ConnectionString).Options;
            KernelDb Open() => KernelDb.ForSqliteRootShipped(options, StoreFormat.RootAuthoredEventsQualification);
            var owner = sqlite.CreateHostedService(model, EndProcess, loop, inBatch);
            await owner.StartAsync(token);
            return (Open, await sqlite.OpenIngressAsync(token), sqlite.CreateReads(), owner);
        }

        var pg = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
            throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.")).Options;
        KernelDb OpenPostgres() => KernelDb.ForPostgresRootAuthoredEventsQualification(pg);
        await using var database = OpenPostgres();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(database, token), "the PostgreSQL fixture uses a dedicated empty database");
        var cold = new EfStore(OpenPostgres, postgres: true);
        await cold.CreateAsync(1);
        await cold.InstallPreviewRootSchemaAsync(token);
        var requests = PostgresPreviewHosting.CreateRequests(OpenPostgres, node, model.Type);
        var foundation = await PostgresPreviewHosting.OpenFoundationAsync(OpenPostgres, model.Type, requests, cancellationToken: token);
        var hosted = await PostgresPreviewHosting.StartAsync(OpenPostgres, node, model, EndProcess, loop, token,
            Console.Error.WriteLine, (IReplyTransport)requests, fenceInBatch: inBatch);
        return (OpenPostgres, foundation, foundation, hosted);
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
