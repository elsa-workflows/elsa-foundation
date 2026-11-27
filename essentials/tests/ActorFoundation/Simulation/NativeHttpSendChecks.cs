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
            .Add(Sends("credential", Wf.Http("send", "https://partner.example/ok", headers: Binding.Literal(new JsonObject { ["Authorization"] = "Bearer secret" }))))
            .Add(Sends("charset", Input(Wf.Http("send", "https://partner.example/ok", "plain words"), "ContentType", "text/plain; charset=utf-8")))
            .Add(Sends("json-text", Input(Wf.Http("send", "https://partner.example/ok", "{\"raw\":1}"), "ContentType", "application/json")))
            .Add(Sends("untyped-text", Wf.Http("send", "https://partner.example/ok", "as typed")))
            .Add(Sends("empty-text", Wf.Http("send", "https://partner.example/ok", "")))
            .Add(Sends("streamed", Wf.Http("send", "https://partner.example/stream")))
            .Add(Sends("reset", Wf.Http("send", "https://partner.example/reset")))
            .Add(Sends("iso-timeout", Input(Wf.Http("send", "https://partner.example/slow"), "Timeout", "PT0.2S")))
            .Add(Sends("bad-timeout", Input(Wf.Http("send", "https://partner.example/ok"), "Timeout", "soon")))
            .Add(Sends("string-ms", Input(Wf.Http("send", "https://partner.example/slow"), "Timeout", "200")))
            .Add(Sends("expr-timeout", Input(Wf.Http("send", "https://partner.example/stuck"), "Timeout", Binding.Expr("100 * 3"))))
            .Add(Sends("redirected", Wf.Http("send", "https://partner.example/redirect")))
            .Add(Sends("string-codes", Input(Wf.Http("send", "https://partner.example/ok"), "ExpectedStatusCodes", Binding.Literal(new JsonArray("200")))))
            .Add(Sends("content-type-header", Wf.Http("send", "https://partner.example/ok", headers: Binding.Literal(new JsonObject { ["Content-Type"] = "text/plain" }))))
            .Add(Sends("long-job", WithJobTimeout(Wf.Http("send", "https://partner.example/slow", timeout: TimeSpan.FromMilliseconds(200)), TimeSpan.FromSeconds(30))));
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
                var contentTypeHeader = await host.RunAsync("content-type-header", mode);
                Check(credential.Faulted && contentTypeHeader.Faulted, $"{mode}: an authored Authorization or Content-Type header faults the activity; credentials come through a Connection, the media type through ContentType");
                Check(ok.Response?["Headers"]?["set-cookie"] is null && ok.Response?["Headers"]?["x-trace"] is not null,
                    $"{mode}: the result leaves out the Set-Cookie a partner sends back");
                var charset = await host.RunAsync("charset", mode);
                var plain = handler.Sent.Last();
                Check(charset.Outcome == SendHttpRequest.DoneOutcome && plain.Body == "plain words" &&
                      plain.Headers.GetValueOrDefault("Content-Type")?.Contains("charset=utf-8", StringComparison.OrdinalIgnoreCase) == true,
                    $"{mode}: a ContentType with parameters sends a text body as that type ({charset.Outcome})");
                await host.RunAsync("json-text", mode);
                var jsonText = handler.Sent.Last();
                await host.RunAsync("untyped-text", mode);
                var untyped = handler.Sent.Last();
                var empty = await host.RunAsync("empty-text", mode);
                var none = handler.Sent.Last();
                Check(jsonText.Body == """{"raw":1}""" && jsonText.Headers.GetValueOrDefault("Content-Type")?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true &&
                      untyped.Body == "as typed" && untyped.Headers.GetValueOrDefault("Content-Type")?.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase) == true &&
                      empty.Outcome == SendHttpRequest.DoneOutcome && none.Body is null,
                    $"{mode}: a string body is sent as its text under any media type, text/plain without one, and an empty one sends no content ({jsonText.Body}, {untyped.Body}, {none.Body ?? "none"})");
                Volatile.Write(ref _pulled, 0);
                var streamed = await host.RunAsync("streamed", mode);
                Check(streamed.Outcome == SendHttpRequest.TooLargeOutcome && Volatile.Read(ref _pulled) < 64 * 1_024,
                    $"{mode}: a streamed body with no length past the cap is read only that far ({Volatile.Read(ref _pulled)} bytes pulled) and completes on ResponseTooLarge");
                var reset = await host.RunAsync("reset", mode);
                Check(reset.Outcome == SendHttpRequest.FailedOutcome, $"{mode}: a connection reset in the middle of the body completes on Failed ({reset.Outcome})");
                var iso = await host.RunAsync("iso-timeout", mode);
                var badTimeout = await host.RunAsync("bad-timeout", mode);
                var codes = await host.RunAsync("string-codes", mode);
                var stringMs = await host.RunAsync("string-ms", mode);
                Check(iso.Outcome == SendHttpRequest.TimeoutOutcome && stringMs.Outcome == SendHttpRequest.TimeoutOutcome && badTimeout.Faulted && codes.Outcome == "200",
                    $"{mode}: a timeout reads as milliseconds (a number or a string of one) or a time span (PT0.2S), and anything else faults; status codes may be numeric strings ({iso.Outcome}, {stringMs.Outcome}, {codes.Outcome})");
                // The partner ignores the cancel, so only the job's deadline ends the call early: under the 60 s default the
                // call would end when the partner answers, three seconds on.
                var before = Environment.TickCount64;
                var exprTimeout = await host.RunAsync("expr-timeout", mode);
                var took = Environment.TickCount64 - before;
                Check(exprTimeout.Outcome == SendHttpRequest.TimeoutOutcome && took < 2_000,
                    $"{mode}: a timeout an expression computes (a long behind the JSON number) is the job's deadline too ({exprTimeout.Outcome} in {took} ms)");
                var redirected = await host.RunAsync("redirected", mode);
                Check(redirected.Faulted, $"{mode}: a host client that followed a redirect faults the activity, whose headers it carried away");
                var started = Environment.TickCount64;
                var longJob = await host.RunAsync("long-job", mode);
                var elapsed = Environment.TickCount64 - started;
                Check(longJob.Outcome == SendHttpRequest.TimeoutOutcome && elapsed < 10_000,
                    $"{mode}: with a longer job deadline, the HTTP client's own timeout completes the activity on Timeout ({longJob.Outcome} in {elapsed} ms)");
            }

        foreach (var mode in new[] { Modes.Balanced, Modes.Durable })
            await CheckOwnerLossRerunAsync(mode, Check, token);
        using (var created = NativeHttpOptions.CreateClient())
        {
            var settings = typeof(HttpMessageInvoker).GetField("_handler", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(created) as SocketsHttpHandler;
            Check(created.Timeout == Timeout.InfiniteTimeSpan && settings is { AllowAutoRedirect: false, UseCookies: false },
                "NativeHttpOptions.CreateClient builds a client with no timeout of its own, no redirects and no cookies");
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

    private static long _pulled;

    private static Node Input(Node node, string name, Binding value)
    {
        node.Inputs[name] = value;
        return node;
    }

    private static Node WithJobTimeout(Node node, TimeSpan jobTimeout)
    {
        var timed = new Node(node.Id, node.Type) { JobTimeout = jobTimeout };
        foreach (var (name, binding) in node.Inputs) timed.Inputs[name] = binding;
        return timed;
    }

    // A body that never ends, counting what the reader pulls from it.
    private sealed class EndlessStream : ReadOnlyStream
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)'x', offset, count);
            Interlocked.Add(ref _pulled, count);
            return count;
        }
    }

    // A body that breaks off after a byte, as a reset connection does.
    private sealed class ResetStream : ReadOnlyStream
    {
        private int _reads;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_reads++ > 0) throw new IOException("The connection was reset.");
            buffer[offset] = (byte)'x';
            return 1;
        }
    }

    private abstract class ReadOnlyStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<HttpResponseMessage> PartnerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        switch (path)
        {
            case "/ok":
                var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json") };
                ok.Headers.Add("X-Trace", "t-1");
                ok.Headers.Add("Set-Cookie", "session=partner-secret");
                return ok;
            case "/redirect":
                // What a client that follows redirects does: it rewrites the request it sent to the URI it lands on.
                request.RequestUri = new Uri("https://elsewhere.example/landing");
                return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            case "/stream":
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) };
            case "/reset":
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ResetStream()) };
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
