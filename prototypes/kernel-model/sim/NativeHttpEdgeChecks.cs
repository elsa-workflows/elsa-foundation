using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

internal static partial class NativeHttpEdgeChecks
{
    internal static async Task<int> RunAsync(Flaw flaws)
    {
        const long initial = 1_703_400_000_000;
        const int requestTime = 2_000;
        const string retryId = "approval-retry";
        const string retryHeader = "X-Fixture-Event-Retry";
        long timestamp = initial;
        long Now() => Interlocked.Read(ref timestamp);
        var store = new InMemoryStore(1, Now);
        var scope = new Contract.BoundScope("http-edge");
        const string node = "http-edge-owner";
        var requests = new Requests(node);
        var reads = new ActorList(store, 16);
        Contract.IIngress ingress = new InMemoryIngress(store, requests, node);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var enteringCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refuseNextFinish = 0;
        var wire = new CountingStore(store, () => { }, beforeCommit: async batch =>
        {
            if (batch.Turns.Any(turn => turn.Expected is null && turn.Row?.Status == InstanceStatus.Running) && enteringCommit.TrySetResult())
                await releaseCommit.Task.WaitAsync(token);
            if (batch.Turns.Any(turn => turn.Row?.Status == InstanceStatus.Finished) && Interlocked.Exchange(ref refuseNextFinish, 0) != 0)
                throw new StoreException(Failure.Unknown, "The fixture lost its final response commit before execution.");
        });
        var transport = new LocalTransport(node, requests);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("approval", Wf.Sequence("main",
                Wf.HttpEndpoint("start", "begin"),
                Wf.Respond("start-response", "created", 201),
                Wf.HttpEndpoint("resume", "approve"),
                Wf.Respond("approval-response", Binding.Result("resume", "Body.name")))))
            .Add(Wf.Workflow("slow", Wf.Sequence("main",
                Wf.HttpEndpoint("start", "slow"),
                Wf.Delay("wait", 10_000),
                Wf.Respond("late-response", "late"))))
            .Add(Wf.Workflow("fault", Wf.Sequence("main",
                Wf.HttpEndpoint("start", "fault"),
                Wf.Respond("held-response", "should not reach the caller", 201),
                Wf.Node<Fails>("fail"))));
        var routes = new HttpRoutes(registry);
        var worker = new Worker(node, 0, wire, new ActorAdapter(store, new PublicWorkflow([new NativeExecutionModel(registry)]), (requests, node)),
            new WorkerOptions { Lease = TimeSpan.FromMinutes(2), Monotonic = () => Now() - initial, Transport = transport, Flaws = flaws });
        await using var activation = await new DurableHost([worker]).ActivateAsync(0, token)
            ?? throw new InvalidOperationException("The Embedded owner could not activate.");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.MapPost("/{**route}", (Delegate)HandleAsync);
        await app.StartAsync(token);
        var pump = RunOwnerAsync(worker, token);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        try
        {
            using (var missing = await PostApprovalRetryAsync("abandoned"))
                Require(missing.StatusCode == HttpStatusCode.NotFound &&
                    await missing.Content.ReadAsStringAsync(token) == "no workflow waits on this route" &&
                    store.FoundationEvents.Count == 0 && store.EventRows.Count == 0 && store.Messages.Count == 0 && requests.All.Count == 0,
                    $"An unmatched HTTP event with KeepFor returns 404 and leaves no publication, delivery or reply registration. Observed {(int)missing.StatusCode} and {store.FoundationEvents.Count} publications.");

            var begin = PostAsync("begin");
            await enteringCommit.Task.WaitAsync(token);
            var startRequest = requests.All.SingleOrDefault();
            Require(!begin.IsCompleted && transport.Responses == 0 && startRequest is not null && requests.IsOpen(startRequest) &&
                    (await reads.ListAsync(scope, new(Status: Contract.ActorStatus.Running), cancellationToken: token)).Items.Count == 0,
                "The HTTP response stays held while the first checkpoint commit is blocked.");
            releaseCommit.SetResult();
            using (var started = await begin)
                Require(started.StatusCode == HttpStatusCode.Created && await started.Content.ReadAsStringAsync(token) == "created",
                    "The Native start returns its authored HTTP 201.");
            var approval = (await reads.ListAsync(scope, new(Status: Contract.ActorStatus.Running), cancellationToken: token)).Items.Single();
            var birthJournal = await reads.TailAsync(scope, approval.Header.Ref, cancellationToken: token);
            Require(startRequest is { Responses: 1 } && birthJournal.Next is not null && birthJournal.Items.Count > 0 &&
                    birthJournal.Items.All(entry => entry.Revision == 1),
                "The caller receives one response after the waiting actor's first committed journal revision.");

            using (var resumed = await PostApprovalRetryAsync("Ada"))
            {
                var answer = await resumed.Content.ReadAsStringAsync(token);
                Require(resumed.StatusCode == HttpStatusCode.OK && answer == "Ada",
                    $"Retrying the same event ID delivers the new payload to the committed Native wait. Observed {(int)resumed.StatusCode} and '{answer}'.");
            }
            var resumedJournal = await reads.TailAsync(scope, approval.Header.Ref, birthJournal.Next, token);
            Require(await reads.GetAsync(scope, approval.Header.Ref, token) is { Header.Status: Contract.ActorStatus.Finished } &&
                    resumedJournal.Items.Count > 0 && resumedJournal.Items.All(entry => entry.Revision == 2),
                "The resumed workflow finished at the original public reference with its second committed journal revision.");
            using (var noLongerWaiting = await PostAsync("approve"))
                Require(noLongerWaiting.StatusCode == HttpStatusCode.NotFound,
                    "The completed wait cannot consume a second HTTP request.");

            var slow = PostAsync("slow");
            var waiting = await WaitForAsync(Contract.ActorStatus.Running);
            var expiring = requests.All.Single();
            Require(Now() < expiring.Deadline && requests.IsOpen(expiring) && !slow.IsCompleted,
                "The admitted HTTP request stays open before its recorded deadline.");
            Interlocked.Exchange(ref timestamp, expiring.Deadline + 1);
            using (var expired = await slow)
                Require(expired.StatusCode == HttpStatusCode.GatewayTimeout && expiring.Error == "timeout",
                    "Crossing the recorded deadline closes the live request with 504 while the workflow waits.");
            Interlocked.Add(ref timestamp, 10_000);
            var finished = await WaitForAsync(Contract.ActorStatus.Finished, waiting.Header.Ref);
            Require(finished.Header.Ref == waiting.Header.Ref, "The timed-out workflow continues to its original terminal reference.");

            using (var another = await PostAsync("begin"))
                Require(another.StatusCode == HttpStatusCode.Created, "A second approval workflow reaches its wait.");
            var local = await WaitForAsync(Contract.ActorStatus.Running);
            var originalEpoch = worker.Epoch;
            Interlocked.Exchange(ref refuseNextFinish, 1);
            using (var lost = await PostAsync("approve"))
                Require(lost.StatusCode == HttpStatusCode.ServiceUnavailable && Volatile.Read(ref refuseNextFinish) == 0,
                    "The receiving owner's reset returns 503 for the pending HTTP response.");
            var afterReset = await WaitForAsync(Contract.ActorStatus.Finished, local.Header.Ref);
            Require(afterReset.Header.Ref == local.Header.Ref && worker.Epoch > originalEpoch,
                "A fresh take finishes the approval after the receiving owner's reset.");

            using var disconnected = CancellationTokenSource.CreateLinkedTokenSource(token);
            var interrupted = client.PostAsync("slow", null, disconnected.Token);
            var stillRunning = await WaitForAsync(Contract.ActorStatus.Running);
            var abandoned = requests.All.Single();
            disconnected.Cancel();
            var disconnectedCall = false;
            try { using var received = await interrupted; }
            catch (OperationCanceledException) when (disconnected.IsCancellationRequested) { disconnectedCall = true; }
            Require(disconnectedCall, "The HTTP client abandons its pending call.");
            while (requests.IsOpen(abandoned)) await Task.Delay(10, token);
            Require(Now() < abandoned.Deadline && abandoned.Error == "disconnected",
                "The server closes the disconnected caller's live registration before its deadline.");
            Interlocked.Add(ref timestamp, 10_001);
            var afterDisconnect = await WaitForAsync(Contract.ActorStatus.Finished, stillRunning.Header.Ref);
            Require(afterDisconnect.Header.Ref == stillRunning.Header.Ref,
                "A disconnected caller leaves its accepted workflow running to completion.");

            using (var fault = await PostAsync("fault"))
                Require(fault.StatusCode == HttpStatusCode.InternalServerError &&
                    await fault.Content.ReadAsStringAsync(token) == "the workflow faulted",
                    "A parked response turn returns 500 in place of its held 201.");
            Require((await WaitForAsync(Contract.ActorStatus.Faulted)).Park is not null,
                "The faulted workflow has a durable Park.");
            Require(startRequest is { Responses: 1 } && expiring is { Responses: 0, Ends: 1, Error: "timeout" } &&
                    abandoned is { Responses: 0, Ends: 1, Error: "disconnected" },
                "The start registration accepted one response. Timed-out and disconnected registrations kept their original outcome.");
            Require(transport.WrongDestination is null, "Every transport reply targeted the receiving node.");
            return 0;
        }
        finally
        {
            releaseCommit.TrySetResult();
            deadline.Cancel();
            await pump;
            await app.StopAsync();
        }

        Task<IResult> HandleAsync(HttpContext context) => context.Request.Headers.ContainsKey(retryHeader)
            ? DispatchAsync(context, scope, routes, ingress, Now() + requestTime, token,
                retryId, new(KeepFor: TimeSpan.FromMinutes(1)))
            : DispatchAsync(context, scope, routes, ingress, Now() + requestTime, token);
        Task<HttpResponseMessage> PostAsync(string path) => client.PostAsync(path, null, token);

        async Task<HttpResponseMessage> PostApprovalRetryAsync(string name)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "approve") { Content = JsonContent.Create(new { Name = name }) };
            request.Headers.Add(retryHeader, "true");
            return await client.SendAsync(request, token);
        }

        async Task<Contract.ActorView> WaitForAsync(Contract.ActorStatus status, Contract.ActorRef? actor = null)
        {
            while (true)
            {
                var page = await reads.ListAsync(scope, new(Status: status), cancellationToken: token);
                var row = page.Items.SingleOrDefault(item => actor is null || item.Header.Ref == actor);
                if (row is not null) return row;
                await Task.Delay(10, token);
            }
        }
    }

    private static async Task<IResult> DispatchAsync(HttpContext context, Contract.BoundScope scope, HttpRoutes routes,
        Contract.IIngress ingress, long deadline, CancellationToken stopping,
        string? eventId = null, Contract.AddOptions? eventOptions = null)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
        var token = stop.Token;
        var request = context.Request;
        var body = context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true
            ? await JsonNode.ParseAsync(request.Body, cancellationToken: token) : null;
        var message = new Kernel.Native.HttpRequest(request.Method, request.Path, body);
        var id = Guid.NewGuid().ToString("N");
        var options = new Contract.RequestOptions(null, deadline, routes.BeforeCommit(message.Method, message.Path));
        var reply = routes.TryStart(message.Method, message.Path, out var route)
            ? await ingress.RequestAsync(scope, new Contract.Target.Start(PublicWorkflow.Start(
                    NativeExecutionModel.Start(route.Workflow, triggerNode: route.Node, trigger: Values.From(message)))),
                id, Contract.Kinds.Start, null, null, options, token)
            : await ingress.RequestAsync(scope, new Contract.Target.Event(HttpRoutes.Key(message.Method, message.Path)),
                eventId ?? id, Contract.Kinds.Event, JsonSerializer.Serialize(message), eventOptions, options, token);

        return reply switch
        {
            { Error: "no waiter" or "missing" } => Results.Text("no workflow waits on this route", statusCode: 404),
            { Error: "timeout" } => Results.Text("the workflow did not answer in time", statusCode: 504),
            { Error: "lost" } => Results.Text("the workflow's owner was lost", statusCode: 503),
            { Status: Contract.ActorStatus.Faulted } => Results.Text("the workflow faulted", statusCode: 500),
            { Error: null, Channel: "response", Item: JsonNode response } when Values.To<Kernel.Native.HttpResponse>(response) is { } answer =>
                Results.Text(answer.Body is JsonValue scalar && answer.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
                    scalar.TryGetValue<string>(out var text) ? text : answer.Body?.ToJsonString(), answer.ContentType, statusCode: answer.StatusCode),
            { Error: null, Item: null } => Results.NoContent(),
            _ => Results.StatusCode(500)
        };
    }

    private static async Task RunOwnerAsync(Worker owner, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!owner.Owns) await owner.TakeAsync(token);
                await owner.WokenAsync(null, token);
                await owner.RunAsync(token);
                await Task.Delay(5, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine(description);
    }

    private sealed class LocalTransport(string localNode, Requests requests) : IReplyTransport
    {
        private int _responses;
        private string? _wrongDestination;
        internal string? WrongDestination => Volatile.Read(ref _wrongDestination);
        internal int Responses => Volatile.Read(ref _responses);

        public void Send(string node, Reply reply)
        {
            if (!string.Equals(node, localNode, StringComparison.Ordinal))
            {
                Interlocked.CompareExchange(ref _wrongDestination, node, null);
                return;
            }
            if (reply.Channel == "response" && reply.Item is JsonNode) Interlocked.Increment(ref _responses);
            requests.Receive(reply);
        }
    }

    public sealed class Fails : Activity
    {
        protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) =>
            throw new InvalidOperationException("The authored response turn fails.");
    }
}
