using System.Net;
using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Kernel.Native.Run;

internal static class KernelHttpWaitChecks
{
    internal static async Task<int> RunAsync(string? provider = null, bool inBatch = true, bool shipped = false)
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
        var fixture = await OpenAsync(provider, inBatch, shipped, model, temporary?.Path, token);
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
            if (provider is not null)
            {
                await CheckParkAndScopeAsync();
                var first = await StartAsync("native", "fanout");
                var second = await StartAsync("native", "fanout");
                using var response = await PostAsync("native", "fanout", "cancel");
                Require(response.StatusCode == HttpStatusCode.OK, "a held request receives the first live reply from the matched waits");
                await AwaitViewAsync(first, view => view.Header.Status == Contract.ActorStatus.Finished);
                await AwaitViewAsync(second, view => view.Header.Status == Contract.ActorStatus.Finished);
                Require(true, "both committed waits on one resolved key receive the held publication");
                if (fixture.EventRace is { } race) await CheckEventOrderAsync(race);
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
            if (response.StatusCode != HttpStatusCode.Accepted || body != order)
                throw new InvalidOperationException($"{kind}: expected the start to return 202 and '{order}', got {(int)response.StatusCode} '{body}'.");
            Require(true, $"{kind}: the start returns 202 with the exact route value.");
            return (await reads.ListAsync(scope, new(Status: Contract.ActorStatus.Running), cancellationToken: token)).Items
                .Single(row => !existing.Contains(row.Header.Ref)).Header.Ref;
        }

        Task<HttpResponseMessage> PostAsync(string kind, string order, string action) =>
            client.PostAsync($"{basePath}/{kind}/orders/{Uri.EscapeDataString(order)}/{action}", null, token);

        async Task<Contract.ActorStatus?> StateAsync(Contract.ActorRef actor) => (await reads.GetAsync(scope, actor, token))?.Header.Status;

        async Task<Contract.ActorView> AwaitViewAsync(Contract.ActorRef actor, Func<Contract.ActorView, bool> expected)
        {
            while (true)
            {
                if (await reads.GetAsync(scope, actor, token) is { } view && expected(view)) return view;
                await Task.Delay(25, token);
            }
        }

        async Task CheckParkAndScopeAsync()
        {
            const string order = "park-scope";
            const string operation = "reusable-held-operation";
            var reference = await StartAsync("native", order);
            var match = KernelHttpEdge.Route(serving, "POST", $"/native/orders/{order}/cancel")!;
            var wait = KernelHttpEdge.Wait(serving, match, new HttpRequest("POST", $"/native/orders/{order}/cancel", null))!;
            var before = await fixture.EventCount(token);
            Require((await PublishAsync(ContractScopes.Bind("other-tenant"))).Error == "no waiter" &&
                await fixture.EventCount(token) == before && await StateAsync(reference) == Contract.ActorStatus.Running,
                "another tenant cannot publish to the committed wait or leave a receipt");
            Require(await fixture.Ingress.SendAsync(scope, new Contract.Target.Ref(reference), "park-request", ParkProbe.InvalidWait, cancellationToken: token)
                is { Outcome: Contract.AdmissionOutcome.Accepted }, "the actor admits the fixture's faulting input");
            var parked = await AwaitViewAsync(reference, view => view.Park is not null);
            Require((await PublishAsync(scope)).Error == "no waiter" && await fixture.EventCount(token) == before,
                "the unresolved Park has no live wait and a held request leaves no durable receipt");
            Require(await fixture.Ingress.SendAsync(scope, new Contract.Target.Ref(reference), "recover-park", Contract.Kinds.Recover,
                System.Text.Json.JsonSerializer.Serialize(new Contract.OperatorCommand(parked.Selection)), cancellationToken: token)
                is { Outcome: Contract.AdmissionOutcome.Accepted }, "the provider admits Recover for the exact Park selection");
            await AwaitViewAsync(reference, view => view is { Park: null, Header.Status: Contract.ActorStatus.Running });
            var resumed = await PublishAsync(scope);
            Require(resumed is { Channel: "response", Error: null } &&
                KernelHttpEdge.Answer(resumed) is { StatusCode: 200, Body: order },
                "Recover restores the exact committed wait and the previously unmatched operation can deliver");
            await AwaitViewAsync(reference, view => view.Header.Status == Contract.ActorStatus.Finished);
            Require(await fixture.EventCount(token) == before + 1, "only the successfully matched request retains an event receipt");

            async Task<Contract.Reply> PublishAsync(Contract.BoundScope tenant) => await fixture.Ingress.RequestAsync(tenant,
                new Contract.Target.Event(wait.Key), operation, Contract.Kinds.Terminate, wait.Payload, null,
                new(null, await fixture.Clock(token) + 5_000), token);
        }

        async Task CheckEventOrderAsync(PostgresEventRace race)
        {
            const string order = "registration-race";
            const string operation = "one-held-operation";
            var reference = await StartAsync("native", order);
            var route = $"/native/orders/{order}/cancel";
            var wait = KernelHttpEdge.Wait(serving, KernelHttpEdge.Route(serving, "POST", route)!, new HttpRequest("POST", route, null))!;
            var before = await fixture.EventCount(token);
            race.PauseNextMatch();
            var first = PublishAsync(5_000);
            Task<Contract.ActorRef> later;
            try
            {
                await race.EnteringMatch.Task.WaitAsync(token);
                race.ObserveNextKeyLock();
                later = StartAsync("native", order);
                var pid = await race.EnteringKeyLock.Task.WaitAsync(TimeSpan.FromSeconds(4), token);
                while (!await race.IsBlockedAsync(pid, token)) await Task.Delay(15, token);
                Require(!later.IsCompleted, "the new wait cannot commit while a publication owns the key's transaction lock");
            }
            finally { race.ContinueMatch.TrySetResult(); }
            Require(await first is { Channel: "response", Error: null }, "the publication reaches the original committed wait");
            var registeredLater = await later;
            await AwaitViewAsync(reference, view => view.Header.Status == Contract.ActorStatus.Finished);
            Require(await PublishAsync(500) is { Error: "timeout" } && await fixture.EventCount(token) == before + 1 &&
                await StateAsync(registeredLater) == Contract.ActorStatus.Running,
                "the replay sees the receipt without publishing to the wait that committed later");
            using var resumed = await PostAsync("native", order, "cancel");
            Require(resumed.IsSuccessStatusCode, "a new publication reaches the later registered wait");

            async Task<Contract.Reply> PublishAsync(long waitFor) => await fixture.Ingress.RequestAsync(scope,
                new Contract.Target.Event(wait.Key), operation, Contract.Kinds.Event, wait.Payload, null,
                new(null, await fixture.Clock(token) + waitFor), token);
        }

    }

    private sealed record WaitFixture(IAsyncDisposable Owner, Contract.IIngress Ingress, Contract.IActorReads Reads,
        Func<CancellationToken, ValueTask<long>> Clock, Func<CancellationToken, Task<int>> EventCount,
        Func<bool> NoPendingCallers, Func<Task>? Pump = null, PostgresEventRace? EventRace = null);

    private static async Task<WaitFixture> OpenAsync(string? provider, bool inBatch, bool shipped, PublicWorkflow model, string? path, CancellationToken token)
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
            var race = new PostgresEventRace();
            var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
                throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.")).AddInterceptors(race).Options;
            KernelDb Open() => shipped ? KernelDb.ForPostgresRootShipped(options) : KernelDb.ForPostgresRootLiveEventQualification(options);
            race.IsBlockedAsync = async (pid, ct) =>
            {
                await using var db = Open();
                return await db.Database.SqlQuery<bool>($"""
                    SELECT cardinality(pg_catalog.pg_blocking_pids({pid})) > 0 AS "Value"
                    """).SingleAsync(ct);
            };
            await using var database = Open();
            Require(await PostgresFixtureDatabase.IsEmptyAsync(database, token), "the PostgreSQL event fixture uses a dedicated empty database");
            var cold = new EfStore(Open, postgres: true);
            await cold.CreateAsync(1);
            await cold.InstallPreviewRootSchemaAsync(token);
            var requests = PostgresPreviewHosting.CreateRequests(Open, node, model.Type);
            var foundation = await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(Open, model.Type, Authorize,
                requests, cancellationToken: token);
            var owner = await PostgresPreviewHosting.StartAsync(Open, node, new ParkProbe(model), EndProcess, loop, token, Console.Error.WriteLine,
                (IReplyTransport)requests, fenceInBatch: inBatch);
            return new(owner, foundation, foundation, requests.ReadClockAsync, ct => EventCountAsync(Open, ct),
                () => requests.Pending.All.Count == 0, EventRace: race);
        }

        if (provider == "sqlite")
        {
            var foundation = shipped ? new SqliteEmbeddedFoundation(path!, node, model.Type) :
                SqliteEmbeddedFoundation.ForLiveEventQualification(path!, node, model.Type);
            await foundation.InstallShippedAsync(token);
            var owner = foundation.CreateHostedService(new ParkProbe(model), EndProcess, loop, inBatch);
            await owner.StartAsync(token);
            var ingress = await foundation.OpenIngressWithOperatorsAsync(Authorize, token);
            var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                { DataSource = path, Pooling = false }.ConnectionString).Options;
            KernelDb Open() => KernelDb.ForSqliteRootShipped(options, shipped ? null : StoreFormat.RootLiveEventQualification);
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

        static bool Authorize(Contract.BoundScope scope, Contract.ActorRef _, string kind) =>
            scope.Tenant == "kernel-http-waits" && kind == Contract.Kinds.Recover;
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("http-waits");
        internal string Path => System.IO.Path.Combine(directory.FullName, "waits.db");
        public void Dispose() => directory.Delete(recursive: true);
    }

    private sealed class PostgresEventRace : DbCommandInterceptor
    {
        private int _pause;
        private int _observe;
        internal TaskCompletionSource EnteringMatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ContinueMatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<int> EnteringKeyLock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Func<int, CancellationToken, Task<bool>> IsBlockedAsync { get; set; } = null!;

        internal void PauseNextMatch() => Volatile.Write(ref _pause, 1);
        internal void ObserveNextKeyLock() => Volatile.Write(ref _observe, 1);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal) && Interlocked.Exchange(ref _observe, 0) == 1)
                EnteringKeyLock.TrySetResult(((Npgsql.NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (command.CommandText.Contains("matches AS MATERIALIZED", StringComparison.Ordinal) && Interlocked.Exchange(ref _pause, 0) == 1)
            {
                EnteringMatch.TrySetResult();
                await ContinueMatch.Task.WaitAsync(token);
            }
            return result;
        }
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

    private sealed class ParkProbe(PublicWorkflow model) : Contract.IActorType
    {
        internal const string InvalidWait = "fixture-invalid-live-wait";
        public string Type => model.Type;
        public bool Withholds(Contract.ActorHeader header) => model.Withholds(header);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => model.ExecuteJobAsync(job, token);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => model.RestoreWaits(header, state);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => model.TerminalGrowth(header, state);
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => model.ProtectsDeletion(header, state, now);

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.Inbox.Any(input => input.Kind == InvalidWait))
                turn.Subscribe("invalid\0key", "fixture");
            return model.TurnAsync(turn, token);
        }
    }
}
