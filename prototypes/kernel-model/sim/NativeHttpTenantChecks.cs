using System.Net;
using System.Net.Http.Json;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static partial class NativeHttpEdgeChecks
{
    private static async Task CheckTenantRoutingAsync(Flaw flaws)
    {
        const long now = 1_703_400_000_000;
        const string receiver = "tenant-edge";
        const string ownerNode = "tenant-owner";
        var scopes = new Dictionary<string, Contract.BoundScope>(StringComparer.OrdinalIgnoreCase)
        {
            ["blue.fixture"] = new("blue"),
            ["red.fixture"] = new("red")
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var store = new InMemoryStore(1, () => now);
        var requests = new Requests(receiver);
        var ingress = new InMemoryIngress(store, requests, receiver);
        var reads = new ActorList(store, 16);
        var registry = new WorkflowRegistry().Add(Wf.Workflow("tenant-approval", Wf.Sequence("main",
            Wf.HttpEndpoint("start", "begin"),
            Wf.Respond("started", "created", 201),
            Wf.HttpEndpoint("resume", "approve"),
            Wf.Respond("approved", Binding.Result("resume", "Body.name")))));
        var routes = new HttpRoutes(registry);
        var firstTerminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wire = new CountingStore(store, () => { }, beforeCommit: async batch =>
        {
            if (FinishesApproval(batch) && firstTerminal.TrySetResult())
                await release.Task.WaitAsync(token);
        });
        var transport = new TenantReplyTransport(receiver, requests);
        var worker = new Worker(ownerNode, 0, wire,
            new ActorAdapter(store, new PublicWorkflow([new NativeExecutionModel(registry)]), (new Requests(ownerNode), ownerNode)),
            new WorkerOptions { Lease = TimeSpan.FromMinutes(1), Monotonic = () => 0, Transport = transport, Flaws = flaws });
        await using var activation = await new DurableHost([worker]).ActivateAsync(0, token)
            ?? throw new InvalidOperationException("The tenant fixture owner could not activate.");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        Task<IResult> HandleAsync(HttpContext context) => scopes.TryGetValue(context.Request.Host.Host, out var scope)
            ? DispatchAsync(context, scope, routes, ingress, now + 20_000, token)
            : Task.FromResult(Results.NotFound());
        app.MapPost("/{**route}", (Delegate)HandleAsync);
        await app.StartAsync(token);
        var pump = PumpOnceOwnedAsync(worker, token);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        try
        {
            using var blueStart = await PostAsync("blue.fixture", "begin");
            using var redStart = await PostAsync("red.fixture", "begin");
            Require(blueStart.StatusCode == HttpStatusCode.Created && redStart.StatusCode == HttpStatusCode.Created,
                "Two host-resolved tenants start on the same route through a separate logical owner.");
            var original = new Dictionary<string, (Contract.ActorRef Actor, Contract.PageToken? Cursor)>();
            foreach (var (host, scope) in scopes)
            {
                var actor = (await reads.ListAsync(scope, new(Status: Contract.ActorStatus.Running), cancellationToken: token))
                    .Items.Single().Header.Ref;
                var born = await reads.TailAsync(scope, actor, cancellationToken: token);
                Require(born.Next is not null && born.Items.Count > 0 && born.Items.All(entry => entry.Revision == 1),
                    $"The owner stores {scope.Tenant}'s approval wait at revision 1.");
                original.Add(host, (actor, born.Next));
            }

            var blueAnswer = PostAsync("blue.fixture", "approve", "Ada");
            await firstTerminal.Task.WaitAsync(token);
            var redAnswer = PostAsync("red.fixture", "approve", "Grace");
            Request? red;
            while ((red = requests.All.SingleOrDefault(call => call.Tenant == "red")) is null)
                await Task.Delay(5, token);
            var blue = requests.All.Single(call => call.Tenant == "blue");
            transport.RedirectTo = red.Registration;
            Require(requests.IsOpen(blue) && requests.IsOpen(red) && !blueAnswer.IsCompleted && !redAnswer.IsCompleted,
                "Both routes are live while the first tenant's commit is held.");
            release.SetResult();

            using var blueResponse = await blueAnswer;
            using var redResponse = await redAnswer;
            Require(blueResponse.StatusCode == HttpStatusCode.OK && await blueResponse.Content.ReadAsStringAsync(token) == "Ada" &&
                redResponse.StatusCode == HttpStatusCode.OK && await redResponse.Content.ReadAsStringAsync(token) == "Grace" &&
                blue.AcceptedResponse?.Tenant == "blue" && red.AcceptedResponse?.Tenant == "red",
                "Both responses reach only their own tenant and carry the owner-stamped tenant.");
            Require(transport.Redirected == 1 && requests.RejectedTenantReplies == 1,
                "The receiver drops and counts a response addressed to the other live tenant.");
            foreach (var (host, scope) in scopes)
            {
                var (actor, cursor) = original[host];
                var terminal = await reads.TailAsync(scope, actor, cursor, token);
                Require(await reads.GetAsync(scope, actor, token) is { Header.Status: Contract.ActorStatus.Finished } &&
                    terminal.Next is not null && terminal.Items.Count > 0 && terminal.Items.All(entry => entry.Revision == 2) &&
                    (await reads.TailAsync(scope, actor, terminal.Next, token)).Items.Count == 0,
                    $"The owner commits {scope.Tenant}'s approval once.");
            }
        }
        finally
        {
            deadline.Cancel();
            release.TrySetResult();
            await pump;
            await app.StopAsync();
        }

        async Task<HttpResponseMessage> PostAsync(string host, string path, string? name = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Host = host;
            if (name is not null) request.Content = JsonContent.Create(new { Name = name });
            return await client.SendAsync(request, token);
        }
    }

    private sealed class TenantReplyTransport(string receiver, Requests requests) : IReplyTransport
    {
        public string? RedirectTo;
        public int Redirected;

        public void Send(string node, Reply reply)
        {
            if (node != receiver) return;
            if (reply is { Tenant: "blue", Channel: Request.Response } &&
                Interlocked.Exchange(ref RedirectTo, null) is { } route)
            {
                Interlocked.Increment(ref Redirected);
                requests.Receive(reply with { Request = route });
            }
            requests.Receive(reply);
        }
    }
}
