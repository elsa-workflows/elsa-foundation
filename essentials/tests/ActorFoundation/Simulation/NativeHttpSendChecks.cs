using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;
using PublicWorkflow = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Sim;

/// <summary>A host HTTP client for the checks: it answers each request with a function and keeps what it was sent.</summary>
internal sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public ConcurrentQueue<(string Url, Dictionary<string, string> Headers, string? Body)> Sent { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        Sent.Enqueue((request.RequestUri!.ToString(), headers, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        return await respond(request, cancellationToken);
    }
}

/// <summary>
/// SendHttpRequest as a background job whose response completes the activity (design card 2, option A), on the public
/// contract host, which keeps job deadlines. Each workflow answers its own request with the outcome it took and the
/// response it got.
/// </summary>
internal static partial class NativeHttpEdgeChecks
{
    private static readonly string[] SendOutcomes = ["200", "404", SendHttpRequest.UnmatchedOutcome, SendHttpRequest.DoneOutcome,
        SendHttpRequest.FailedOutcome, SendHttpRequest.TimeoutOutcome, SendHttpRequest.TooLargeOutcome];

    internal static async Task<int> RunSendAsync()
    {
        var handler = new RecordingHandler(PartnerAsync);
        var http = new NativeHttpOptions
        {
            Client = new HttpClient(handler),
            MaxResponseBytes = 1_024,
            Connection = (name, _) => new(name == "partner"
                ? new Dictionary<string, string> { ["Authorization"] = "Bearer from-connection" }
                : throw new InvalidOperationException($"No connection '{name}'.")),
        };
        var registry = new WorkflowRegistry()
            .Add(Sends("ok", Wf.Http("send", "https://partner.example/ok", Binding.Literal(new JsonObject { ["n"] = 1 }), expectedStatusCodes: [200, 404])))
            .Add(Sends("unmatched", Wf.Http("send", "https://partner.example/status/500", expectedStatusCodes: [200])))
            .Add(Sends("done", Wf.Http("send", "https://partner.example/status/204")))
            .Add(Sends("failed", Wf.Http("send", "https://partner.example/status/503")))
            .Add(Sends("transport", Wf.Http("send", "https://partner.example/down")))
            .Add(Sends("client-timeout", Wf.Http("send", "https://partner.example/slow", timeout: TimeSpan.FromMilliseconds(200))))
            .Add(Sends("job-deadline", Wf.Http("send", "https://partner.example/stuck", timeout: TimeSpan.FromMilliseconds(300))))
            .Add(Sends("too-large", Wf.Http("send", "https://partner.example/large")))
            .Add(Sends("keyed", Wf.Http("send", "https://partner.example/ok", headers: Binding.Literal(new JsonObject { ["Idempotency-Key"] = "mine", ["X-Order"] = 7 }))))
            .Add(Sends("connection", Wf.Http("send", "https://partner.example/ok", connection: "partner")))
            .Add(Sends("credential", Wf.Http("send", "https://partner.example/ok", headers: Binding.Literal(new JsonObject { ["Authorization"] = "Bearer secret" }))));
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = stopping.Token;
        var failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }

        await using (var host = await SendHost.StartAsync(registry, http, token))
            foreach (var mode in new[] { Modes.Ephemeral, Modes.Balanced, Modes.Durable })
            {
                var ok = await host.RunAsync("ok", mode);
                var sent = handler.Sent.LastOrDefault(s => s.Url.EndsWith("/ok", StringComparison.Ordinal));
                Check(ok is { Outcome: "200" } && ok.Response?["StatusCode"]?.GetValue<int>() == 200 && ok.Response["Body"]?["ok"]?.GetValue<bool>() == true &&
                      ok.Response["Headers"]?["x-trace"]?.GetValue<string>() == "t-1" && sent.Body == """{"n":1}""" &&
                      sent.Headers.GetValueOrDefault(SendHttpRequest.IdempotencyKeyHeader) is { } key && key.EndsWith($"/send/{EffectKey.RootPath}/0", StringComparison.Ordinal),
                    $"{mode}: an expected status completes on its own outcome with the parsed body and headers, and the request carries the job's effect key ({ok.Outcome})");
                var unmatched = await host.RunAsync("unmatched", mode);
                var done = await host.RunAsync("done", mode);
                var failed = await host.RunAsync("failed", mode);
                var transport = await host.RunAsync("transport", mode);
                Check(unmatched.Outcome == SendHttpRequest.UnmatchedOutcome && unmatched.Response?["StatusCode"]?.GetValue<int>() == 500 &&
                      done.Outcome == SendHttpRequest.DoneOutcome && failed.Outcome == SendHttpRequest.FailedOutcome &&
                      failed.Response?["StatusCode"]?.GetValue<int>() == 503 && transport.Outcome == SendHttpRequest.FailedOutcome,
                    $"{mode}: an unexpected status, a 2xx and a non-2xx without expected codes, and a transport error take their outcomes without faulting ({unmatched.Outcome}, {done.Outcome}, {failed.Outcome}, {transport.Outcome})");
                var clientTimeout = await host.RunAsync("client-timeout", mode);
                var jobDeadline = await host.RunAsync("job-deadline", mode);
                Check(clientTimeout.Outcome == SendHttpRequest.TimeoutOutcome && jobDeadline.Outcome == SendHttpRequest.TimeoutOutcome,
                    $"{mode}: the Timeout input times out the HTTP call and is the job's deadline too; either one completes on Timeout ({clientTimeout.Outcome}, {jobDeadline.Outcome})");
                var tooLarge = await host.RunAsync("too-large", mode);
                Check(tooLarge.Outcome == SendHttpRequest.TooLargeOutcome && tooLarge.Response?["StatusCode"]?.GetValue<int>() == 200 && tooLarge.Response["Body"] is null,
                    $"{mode}: a body over the host's MaxResponseBytes completes on ResponseTooLarge with the status and no body ({tooLarge.Outcome})");
                await host.RunAsync("keyed", mode);
                var keyed = handler.Sent.Last();
                Check(keyed.Headers.GetValueOrDefault(SendHttpRequest.IdempotencyKeyHeader) == "mine" && keyed.Headers.GetValueOrDefault("X-Order") == "7",
                    $"{mode}: an authored Idempotency-Key is kept, and authored headers go out");
                var connection = await host.RunAsync("connection", mode);
                var resolved = handler.Sent.Last();
                Check(connection.Outcome == SendHttpRequest.DoneOutcome && resolved.Headers.GetValueOrDefault("Authorization") == "Bearer from-connection" &&
                      !host.Stored().Contains("from-connection", StringComparison.Ordinal),
                    $"{mode}: a Connection's credentials are resolved when the job runs and never sit in stored messages or state");
                var credential = await host.RunAsync("credential", mode);
                Check(credential.Faulted, $"{mode}: an authored Authorization header faults the activity; credentials come through a Connection");
            }

        await using (var bare = await SendHost.StartAsync(new WorkflowRegistry().Add(Sends("unhosted", Wf.Http("send", "https://partner.example/ok"))), null, token))
            Check((await bare.RunAsync("unhosted", Modes.Balanced)).Faulted, "without a host HTTP client the activity faults");
        Console.WriteLine(failures == 0 ? "native http send: all passed" : $"native http send: {failures} failed");
        return failures == 0 ? 0 : 1;
    }

    // A Flowchart that answers its caller with the outcome SendHttpRequest completed on and the response it returned.
    private static Workflow Sends(string id, Node send) => Wf.Workflow(id, Wf.Flowchart("flow",
        [send, .. SendOutcomes.Select(outcome => Wf.Respond($"on-{outcome}", Binding.Result(send.Id), contentType: "application/json",
            headers: Binding.Literal(new JsonObject { ["X-Outcome"] = outcome })))],
        [.. SendOutcomes.Select(outcome => new Connection(send.Id, $"on-{outcome}", outcome))]));

    private static async Task<HttpResponseMessage> PartnerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        switch (path)
        {
            case "/ok":
                var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json") };
                ok.Headers.Add("X-Trace", "t-1");
                return ok;
            case "/down":
                throw new HttpRequestException("connection refused");
            case "/slow":
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            case "/stuck":
                await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);     // a partner that does not honour the cancel
                return new HttpResponseMessage(HttpStatusCode.OK);
            case "/large":
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 2_000), Encoding.UTF8, "text/plain") };
            default:
                return new HttpResponseMessage((HttpStatusCode)int.Parse(path["/status/".Length..]));
        }
    }

    private sealed record SendRun(string? Outcome, JsonNode? Response, bool Faulted);

    // One Embedded owner on the public contract, reached through its ingress as an HTTP edge reaches it.
    private sealed class SendHost : IAsyncDisposable
    {
        private const string Node = "http-send-owner";
        private readonly InMemoryStore _store;
        private readonly Contract.IIngress _ingress;
        private readonly Contract.BoundScope _scope = ContractScopes.Bind("http-send");
        private readonly IAsyncDisposable _activation;
        private readonly CancellationTokenSource _stop;
        private readonly Task _pump;
        private int _runs;

        private SendHost(InMemoryStore store, Contract.IIngress ingress, IAsyncDisposable activation, CancellationTokenSource stop, Task pump) =>
            (_store, _ingress, _activation, _stop, _pump) = (store, ingress, activation, stop, pump);

        public static async Task<SendHost> StartAsync(WorkflowRegistry registry, NativeHttpOptions? http, CancellationToken token)
        {
            var store = new InMemoryStore(1, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var requests = new Requests(Node);
            var model = new NativeExecutionModel(registry, new NativeExecutionOptions { Http = http });
            var worker = new Worker(Node, 0, store, new ActorAdapter(store, new PublicWorkflow([model]), (requests, Node)),
                new WorkerOptions { Lease = TimeSpan.FromMinutes(2), Transport = new LocalTransport(Node, requests) });
            var activation = await new DurableHost([worker]).ActivateAsync(0, token) ?? throw new InvalidOperationException("The owner could not activate.");
            var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            return new(store, new InMemoryIngress(store, requests, Node), activation, stop, RunOwnerAsync(worker, stop.Token));
        }

        public async Task<SendRun> RunAsync(string workflow, string mode)
        {
            var id = $"{workflow}-{mode}-{Interlocked.Increment(ref _runs)}";
            var reply = await _ingress.RequestAsync(_scope, new Contract.Target.Start(PublicWorkflow.Start(NativeExecutionModel.Start(workflow, mode))),
                id, Contract.Kinds.Start, null, null, new Contract.RequestOptions(null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 20_000, false), _stop.Token);
            return reply switch
            {
                { Error: null, Channel: "response", Item: JsonNode item } when Values.To<Kernel.Native.HttpResponse>(item) is { } answer =>
                    new(answer.Headers.GetValueOrDefault("x-outcome"), answer.Body, false),
                { Status: Contract.ActorStatus.Faulted } => new(null, null, true),
                _ => new($"no answer: {reply.Error ?? reply.Status?.ToString()}", null, false),
            };
        }

        public string Stored() => string.Join("\n", _store.Messages.Values.Select(m => m.Message.Payload)) + string.Join("\n", _store.Instances.Values.Select(i => i.State));

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _pump;
            await _activation.DisposeAsync();
            _stop.Dispose();
        }
    }
}
