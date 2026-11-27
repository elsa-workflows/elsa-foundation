using System.Net;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.KernelHosting;
using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.Native;
using Kernel.Runtime.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Kernel.Native.Run;

internal static class KernelHttpWaitChecks
{
    internal static async Task<int> RunAsync(string? provider = null, bool inBatch = true)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = stop.Token;
        var serving = new KernelServing(capabilities: new(Schedules: false, HttpWaits: true));
        await serving.ServeHostWorkflowAsync(Wf.Workflow("native-orders", Wf.Sequence("root",
            Wf.HttpEndpoint("start", "native/orders/{ID}/ship"),
            Wf.Respond("accepted", Binding.Result("start", "RouteValues.id"), status: 202),
            Wf.HttpEndpoint("wait", "native/orders/{ID}/cancel", routeValues: Binding.Result("start", "RouteValues")),
            Wf.Respond("cancelled", Binding.Result("wait", "RouteValues.id")))), token);
        var chart = ExecutableOf("chart-orders@1", "chart-orders", Node("chart", Types.Flowchart,
            structure: Structure("elsa.flowchart.structure", new
            {
                startNodeId = "start",
                connections = new[] { Edge("start", "accepted"), Edge("accepted", "wait"), Edge("wait", "cancelled") }
            }), slots: [Slot("Flowchart.Activities",
                Node("start", Types.HttpEndpoint, EndpointInputs("chart/orders/{ID}/ship", start: true)),
                Node("accepted", Types.WriteHttpResponse, [Literal("StatusCode", 202, "Int32"), ResultMember("Body", "start", "RouteData", "ID")]),
                Node("wait", Types.HttpEndpoint, [.. EndpointInputs("chart/orders/{ID}/cancel", start: false), Result("RouteValues", "start", "RouteData")]),
                Node("cancelled", Types.WriteHttpResponse, [ResultMember("Body", "wait", "RouteData", "ID")]))]));
        await serving.ServeHostWorkflowAsync(NativeExecutableMapper.Map(chart, serving.Capabilities), token);

        var scope = ContractScopes.Bind("kernel-http-waits");
        var model = new PublicWorkflow([new NativeExecutionModel(serving.Registry)]);
        using var temporary = provider == "sqlite" ? new TemporaryDatabase() : null;
        var fixture = await OpenAsync(provider, inBatch, model, temporary?.Path, token);
        await using var owner = fixture.Owner;
        var reads = fixture.Reads;

        const string basePath = "/workflows/http";
        var handler = new KernelHttpRequestHandler(serving, _ => ValueTask.FromResult(fixture.Ingress), fixture.Clock,
            scope, basePath, TimeSpan.FromSeconds(5), 64 * 1024, enforceRequestTimeout: true);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.Map(basePath + "/{**path}", handler.HandleAsync);
        await app.StartAsync(token);
        var pump = fixture.Pump?.Invoke() ?? Task.CompletedTask;
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        try
        {
            foreach (var kind in new[] { "native", "chart" })
            {
                const string first = "A/b%41";
                const string second = "a/b%41";
                var before = await fixture.EventCount(token);
                using (var missing = await PostAsync(kind, first, "cancel"))
                    Require(missing.StatusCode == HttpStatusCode.NotFound && await fixture.EventCount(token) == before && fixture.NoPendingCallers(),
                        $"{kind}: a matched route without a committed waiter returns 404 and publishes nothing.");

                var a = await StartAsync(kind, first);
                var b = await StartAsync(kind, second);
                Require(await StateAsync(a) == Contract.ActorStatus.Running && await StateAsync(b) == Contract.ActorStatus.Running,
                    $"{kind}: both starts commit independent running waits.");
                using (var wrongCase = await PostAsync(kind, "A/B%41", "cancel"))
                    Require(wrongCase.StatusCode == HttpStatusCode.NotFound && await fixture.EventCount(token) == before,
                        $"{kind}: the resolved route preserves parameter case and refuses a different key.");
                using (var resumed = await PostAsync(kind, second, "cancel"))
                    Require(resumed.StatusCode == HttpStatusCode.OK && await resumed.Content.ReadAsStringAsync(token) == second &&
                        await StateAsync(a) == Contract.ActorStatus.Running && await StateAsync(b) == Contract.ActorStatus.Finished,
                        $"{kind}: the resolved key wakes only its run and the response receives the exact decoded value.");
                using (var resumed = await PostAsync(kind, first, "cancel"))
                    Require(resumed.StatusCode == HttpStatusCode.OK && await resumed.Content.ReadAsStringAsync(token) == first &&
                        await StateAsync(a) == Contract.ActorStatus.Finished,
                        $"{kind}: an escaped slash and percent are decoded once and the original wait remains independently reachable.");
            }
        }
        finally
        {
            await stop.CancelAsync();
            await pump;
        }
        return 0;

        async Task<Contract.ActorRef> StartAsync(string kind, string order)
        {
            var existing = (await reads.ListAsync(scope, new(Status: Contract.ActorStatus.Running), cancellationToken: token)).Items
                .Select(row => row.Header.Ref).ToHashSet();
            using var response = await PostAsync(kind, order, "ship");
            var body = await response.Content.ReadAsStringAsync(token);
            Require(response.StatusCode == HttpStatusCode.Accepted && body == order,
                $"{kind}: expected the start to return 202 and '{order}', got {(int)response.StatusCode} '{body}'.");
            return (await reads.ListAsync(scope, new(Status: Contract.ActorStatus.Running), cancellationToken: token)).Items
                .Single(row => !existing.Contains(row.Header.Ref)).Header.Ref;
        }

        Task<HttpResponseMessage> PostAsync(string kind, string order, string action) =>
            client.PostAsync($"{basePath}/{kind}/orders/{Uri.EscapeDataString(order)}/{action}", null, token);

        async Task<Contract.ActorStatus?> StateAsync(Contract.ActorRef actor) => (await reads.GetAsync(scope, actor, token))?.Header.Status;

    }

    private sealed record WaitFixture(IAsyncDisposable Owner, Contract.IIngress Ingress, Contract.IActorReads Reads,
        Func<CancellationToken, ValueTask<long>> Clock, Func<CancellationToken, Task<int>> EventCount,
        Func<bool> NoPendingCallers, Func<Task>? Pump = null);

    private static async Task<WaitFixture> OpenAsync(string? provider, bool inBatch, PublicWorkflow model, string? path, CancellationToken token)
    {
        const string node = "http-wait-owner";
        var loop = new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(25),
            Window = TimeSpan.FromMilliseconds(400)
        };
        static void EndProcess(string reason) => throw new InvalidOperationException(reason);

        if (provider == "postgres")
        {
            var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
                throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.")).Options;
            KernelDb Open() => KernelDb.ForPostgresRootLiveEventQualification(options);
            await using var database = Open();
            Require(await PostgresFixtureDatabase.IsEmptyAsync(database, token), "the PostgreSQL event fixture uses a dedicated empty database");
            var cold = new EfStore(Open, postgres: true);
            await cold.CreateAsync(1);
            await cold.InstallPreviewRootSchemaAsync(token);
            var requests = PostgresPreviewHosting.CreateRequests(Open, node, model.Type);
            var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, model.Type, requests, cancellationToken: token);
            var owner = await PostgresPreviewHosting.StartAsync(Open, node, model, EndProcess, loop, token, Console.Error.WriteLine,
                (IReplyTransport)requests, fenceInBatch: inBatch);
            return new(owner, foundation, foundation, requests.ReadClockAsync, ct => EventCountAsync(Open, ct), () => requests.Pending.All.Count == 0);
        }

        if (provider == "sqlite")
        {
            var foundation = SqliteEmbeddedFoundation.ForLiveEventQualification(path!, node, model.Type);
            await foundation.InstallShippedAsync(token);
            var owner = foundation.CreateHostedService(model, EndProcess, loop, inBatch);
            await owner.StartAsync(token);
            var ingress = await foundation.OpenIngressAsync(token);
            var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                { DataSource = path, Pooling = false }.ConnectionString).Options;
            KernelDb Open() => KernelDb.ForSqliteRootShipped(options, StoreFormat.RootLiveEventQualification);
            return new(owner, ingress, foundation.CreateReads(), foundation.ReadClockAsync, ct => EventCountAsync(Open, ct), () => true);
        }

        long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var memory = new InMemoryStore(1, Now);
        var pending = new Requests(node);
        var worker = new Worker(node, 0, memory, new ActorAdapter(memory, model, (pending, node)),
            new WorkerOptions { Lease = TimeSpan.FromMinutes(1), Transport = new HttpWaitTransport(pending) });
        var memoryOwner = await new DurableHost([worker]).ActivateAsync(0, token)
            ?? throw new InvalidOperationException("The fixture owner failed to activate.");
        return new(memoryOwner, new InMemoryIngress(memory, pending, node), new ActorList(memory, 16), _ => new(Now()),
            _ => Task.FromResult(memory.FoundationEvents.Count), () => pending.All.Count == 0, PumpAsync);

        async Task PumpAsync()
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await memoryOwner.WokenAsync(null, token);
                    await memoryOwner.RunAsync(token);
                    await Task.Delay(5, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }

        static async Task<int> EventCountAsync(Func<KernelDb> open, CancellationToken token)
        {
            await using var db = open();
            return await db.Events.CountAsync(token);
        }
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("http-waits");
        internal string Path => System.IO.Path.Combine(directory.FullName, "waits.db");
        public void Dispose() => directory.Delete(recursive: true);
    }

    private static Elsa.Workflows.Runtime.Core.Models.RuntimeInputBinding[] EndpointInputs(string path, bool start) =>
        [Literal("Path", path, "String"), Literal("SupportedMethods", new[] { "POST" }), Literal("CanStartWorkflow", start, "Boolean")];

    private static object Edge(string from, string to) => new { source = new { nodeId = from, port = "Done" }, target = new { nodeId = to } };

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine(description);
    }

    private sealed class HttpWaitTransport(Requests requests) : IReplyTransport
    {
        public void Send(string node, Reply reply) => requests.Receive(reply);
    }
}
