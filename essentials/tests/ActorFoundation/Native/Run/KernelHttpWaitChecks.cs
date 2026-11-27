using System.Net;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.KernelHosting;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Kernel.Native.Run;

internal static class KernelHttpWaitChecks
{
    internal static async Task<int> RunAsync()
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

        const string node = "http-wait-owner";
        var scope = ContractScopes.Bind("kernel-http-waits");
        long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var store = new InMemoryStore(1, Now);
        var requests = new Requests(node);
        var ingress = new InMemoryIngress(store, requests, node);
        var reads = new ActorList(store, 16);
        var model = new PublicWorkflow([new NativeExecutionModel(serving.Registry)]);
        var worker = new Worker(node, 0, store, new ActorAdapter(store, model, (requests, node)),
            new WorkerOptions { Lease = TimeSpan.FromMinutes(1), Transport = new HttpWaitTransport(requests) });
        await using var owner = await new DurableHost([worker]).ActivateAsync(0, token)
            ?? throw new InvalidOperationException("The fixture owner failed to activate.");

        const string basePath = "/workflows/http";
        var handler = new KernelHttpRequestHandler(serving, _ => ValueTask.FromResult<Contract.IIngress>(ingress), _ => new(Now()),
            scope, basePath, TimeSpan.FromSeconds(5), 64 * 1024, enforceRequestTimeout: true);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.Map(basePath + "/{**path}", handler.HandleAsync);
        await app.StartAsync(token);
        var pump = PumpAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        try
        {
            foreach (var kind in new[] { "native", "chart" })
            {
                const string first = "A/b%41";
                const string second = "a/b%41";
                var before = store.FoundationEvents.Count;
                using (var missing = await PostAsync(kind, first, "cancel"))
                    Require(missing.StatusCode == HttpStatusCode.NotFound && store.FoundationEvents.Count == before && requests.All.Count == 0,
                        $"{kind}: a matched route without a committed waiter returns 404 and publishes nothing.");

                var a = await StartAsync(kind, first);
                var b = await StartAsync(kind, second);
                Require(await StateAsync(a) == Contract.ActorStatus.Running && await StateAsync(b) == Contract.ActorStatus.Running,
                    $"{kind}: both starts commit independent running waits.");
                using (var wrongCase = await PostAsync(kind, "A/B%41", "cancel"))
                    Require(wrongCase.StatusCode == HttpStatusCode.NotFound && store.FoundationEvents.Count == before,
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
            Require(response.StatusCode == HttpStatusCode.Accepted && await response.Content.ReadAsStringAsync(token) == order,
                $"{kind}: the start responds 202 with the exact decoded route value.");
            return (await reads.ListAsync(scope, new(Status: Contract.ActorStatus.Running), cancellationToken: token)).Items
                .Single(row => !existing.Contains(row.Header.Ref)).Header.Ref;
        }

        Task<HttpResponseMessage> PostAsync(string kind, string order, string action) =>
            client.PostAsync($"{basePath}/{kind}/orders/{Uri.EscapeDataString(order)}/{action}", null, token);

        async Task<Contract.ActorStatus?> StateAsync(Contract.ActorRef actor) => (await reads.GetAsync(scope, actor, token))?.Header.Status;

        async Task PumpAsync()
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await owner.WokenAsync(null, token);
                    await owner.RunAsync(token);
                    await Task.Delay(5, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
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
}
