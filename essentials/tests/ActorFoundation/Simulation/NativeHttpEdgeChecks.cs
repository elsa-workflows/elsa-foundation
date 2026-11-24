using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Foundation.InMemory;
using Kernel.Native;
using Kernel.Native.Run;
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
        var approvalKey = HttpRoutes.Key("POST", "approve");
        var retention = TimeSpan.FromMinutes(1);
        long timestamp = initial;
        long Now() => Interlocked.Read(ref timestamp);
        var store = new InMemoryStore(1, Now);
        var scope = ContractScopes.Bind("http-edge");
        const string node = "http-edge-owner";
        var requests = new Requests(node);
        var reads = new ActorList(store, 16);
        var ingress = new RecordingIngress(new InMemoryIngress(store, requests, node), approvalKey);
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
                Wf.Node<Fails>("fail"))))
            .Add(Wf.Workflow("hello", Wf.Sequence("main",
                Wf.HttpEndpoint("start", "hello", "GET"),
                Wf.Respond("greeting", Binding.Expr("'hello ' + $start.Query.name"), headers: Binding.Literal(new JsonObject
                    { ["X-Greeting"] = "hi", ["Cache-Control"] = "no-store", ["Retry-After"] = 120 })))))
            .Add(Wf.Workflow("echo-headers", Wf.Sequence("main",
                Wf.HttpEndpoint("start", "echo-headers"),
                Wf.Respond("echo", Binding.Result("start", "Headers"), contentType: "application/json"))))
            .Add(Wf.Workflow("greet-later", Wf.Sequence("main",
                Wf.HttpEndpoint("start", "greet-later"),
                Wf.Respond("waiting", "waiting"),
                Wf.HttpEndpoint("resume", "greet-later/resume", "GET"),
                Wf.Respond("greeting", Binding.Expr("$resume.Query.name + '@' + $resume.Headers['x-tenant']"),
                    headers: Binding.Literal(new JsonObject { ["X-Resumed"] = "yes" })))))
            .Add(Wf.Workflow("designed", Wf.Flowchart("flow",
                [Wf.Respond("accepted", Binding.Expr("'accepted ' + $start.Query.id"), status: 201), Wf.HttpEndpoint("start", "designed")],
                [new Connection("start", "accepted")])))
            .Add(Wf.Workflow("enrich", Wf.Sequence("main", Wf.HttpEndpoint("start", "orders/{id}/enrich"),
                Wf.Respond("enriched", Binding.Expr("'enrich ' + $start.RouteValues.id")))))
            .Add(Wf.Workflow("list", Wf.Sequence("main", Wf.HttpEndpoint("start", "items/list"), Wf.Respond("listed", "the list"))))
            .Add(Wf.Workflow("item", Wf.Sequence("main", Wf.HttpEndpoint("start", "items/{id}"), Wf.Respond("item-out", Binding.Expr("'item ' + $start.RouteValues.id")))))
            .Add(Wf.Workflow("ship", Wf.Sequence("main", Wf.HttpEndpoint("start", "orders/{id}/ship"),
                Wf.Respond("shipping", Binding.Expr("'shipping ' + $start.RouteValues.id"), 202),
                Wf.HttpEndpoint("cancel", "orders/{id}/cancel", routeValues: Binding.Result("start", "RouteValues")),
                Wf.Respond("cancelled", Binding.Expr("'cancelled ' + $start.RouteValues.id + ' by ' + $cancel.RouteValues.id")))))
            .Add(Wf.Workflow("ship-computed", Wf.Sequence("main", Wf.HttpEndpoint("start", "ship-computed").Capture("order", "Body"),
                Wf.Respond("computed-waiting", "waiting"), Wf.HttpEndpoint("cancel", Binding.Expr("'orders/' + order.id + '/cancel'")),
                Wf.Respond("computed-cancelled", Binding.Expr("'computed ' + order.id"))), ("order", null)))
            .Add(Wf.Workflow("fetch", Wf.Sequence("main", Wf.HttpEndpoint("start", "fetch/{*url}"),
                Wf.Respond("fetched", Binding.Expr("'fetch ' + $start.RouteValues.url")))))
            .Add(Wf.Workflow("bad-headers", Wf.Sequence("main",
                Wf.HttpEndpoint("start", "bad-headers"),
                Wf.Respond("refused", "never sent", headers: Binding.Literal(new JsonObject { ["Content-Type"] = "text/html" })))));
        var routes = new HttpRoutes(registry);
        var worker = new Worker(node, 0, wire, new ActorAdapter(store, new PublicWorkflow([new NativeExecutionModel(registry)]), (requests, node)),
            new WorkerOptions { Lease = TimeSpan.FromMinutes(2), Monotonic = () => Now() - initial, Transport = transport, Flaws = flaws });
        await using var activation = await new DurableHost([worker]).ActivateAsync(0, token)
            ?? throw new InvalidOperationException("The Embedded owner could not activate.");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            // Kestrel's own limits sit below the model's; raised here so the model's bound is the one that refuses.
            options.Limits.MaxRequestHeaderCount = 2 * HttpFields.MaxFields;
            options.Limits.MaxRequestHeadersTotalSize = 4 * HttpFields.MaxBytes;
        });
        await using var app = builder.Build();
        app.MapMethods("/{**route}", ["GET", "POST"], (Delegate)HandleAsync);
        await app.StartAsync(token);
        var pump = RunOwnerAsync(worker, token);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        try
        {
            using (var missing = await PostApprovalRetryAsync("abandoned"))
                Require(missing.StatusCode == HttpStatusCode.NotFound &&
                    await missing.Content.ReadAsStringAsync(token) == "no workflow waits on this route" &&
                    store.FoundationEvents.Count == 0 && store.EventRows.Count == 0 && store.Messages.Count == 0 && requests.All.Count == 0 &&
                    ingress.Attempts.ToArray() is [var miss] && miss == new EventAttempt(retryId, retention),
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
            Require(ingress.Attempts.ToArray() is [var first, var retry] && first == new EventAttempt(retryId, retention) && retry == first &&
                store.FoundationEvents.TryGetValue(($"{scope.Tenant}_{approvalKey}", retryId), out var publication) &&
                publication.KeepUntil == Now() + (long)retention.TotalMilliseconds,
                "Both HTTP attempts reach event admission with the same ID and KeepFor. Only the retry leaves a retained publication.");
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

            using (var hello = await client.GetAsync("hello?name=Ada&Name=Other&name=Lin&=nameless", token))
            {
                var greeting = await hello.Content.ReadAsStringAsync(token);
                Require(hello.StatusCode == HttpStatusCode.OK && greeting == "hello Ada,Lin" &&
                        hello.Headers.TryGetValues("x-greeting", out var said) && said.Single() == "hi" &&
                        hello.Headers.CacheControl?.NoStore == true && hello.Headers.RetryAfter?.Delta == TimeSpan.FromSeconds(120) &&
                        hello.Content.Headers.ContentType?.MediaType == "text/plain",
                    $"A GET start reads its repeated query parameter under the name as sent, a nameless pair left out, and the response carries its authored headers. Observed {(int)hello.StatusCode} '{greeting}'.");
            }
            var rawEcho = await RawPostAsync("echo-headers", "X-Tenant: acme", "x-tenant: beta");
            Require(rawEcho.Status == 200 && JsonNode.Parse(rawEcho.Body)?["x-tenant"]?.GetValue<string>() == "acme, beta",
                $"A header sent on two lines reaches the workflow once, its values joined with ', '. Observed {rawEcho.Status} {rawEcho.Body}.");
            using (var parked = await PostAsync("greet-later"))
                Require(parked.StatusCode == HttpStatusCode.OK, "A workflow waits at a GET endpoint in the middle.");
            using (var resume = new HttpRequestMessage(HttpMethod.Get, "greet-later/resume?name=Ada"))
            {
                resume.Headers.Add("X-Tenant", "acme");
                resume.Headers.Add("Authorization", "Bearer secret");
                resume.Headers.Add(retryHeader, "true");       // kept for a minute, so its publication can be read back
                using var resumed = await client.SendAsync(resume, token);
                var said = await resumed.Content.ReadAsStringAsync(token);
                Require(resumed.StatusCode == HttpStatusCode.OK && said == "Ada@acme" &&
                        resumed.Headers.TryGetValues("x-resumed", out var resumedHeader) && resumedHeader.Single() == "yes",
                    $"A request that resumes a wait as an event carries its query and headers, and its answer carries its headers. Observed {(int)resumed.StatusCode} '{said}'.");
                var resumeKey = HttpRoutes.Key("GET", "greet-later/resume");
                var kept = store.FoundationEvents.Where(e => e.Key.Key.EndsWith(resumeKey, StringComparison.Ordinal)).Select(e => e.Value.Payload).ToArray();
                Require(kept is [{ } payload] && payload.Contains("acme", StringComparison.Ordinal) && !payload.Contains("secret", StringComparison.Ordinal),
                    $"The resuming request's retained publication keeps its other headers and none of its credentials. Observed {kept.Length} publication(s).");
            }
            using (var echoed = await SendWithHeadersAsync("echo-headers", ("X-Tenant", "acme"), ("x-tenant", "beta"), ("Authorization", "Bearer secret"),
                       ("Cookie", "session=secret"), ("X-Api-Key", "secret")))
            {
                var json = await echoed.Content.ReadAsStringAsync(token);
                var seen = JsonNode.Parse(json)?.AsObject();
                Require(echoed.StatusCode == HttpStatusCode.OK && seen?["x-tenant"]?.GetValue<string>() == "acme, beta" &&
                        seen.Select(field => field.Key).All(name => name == name.ToLowerInvariant()) && !json.Contains("secret", StringComparison.Ordinal),
                    $"Request headers arrive under lower-case names with repeats joined, and credentials do not reach the workflow. Observed {json}.");
            }
            using (var tooMany = await SendWithHeadersAsync("echo-headers",
                       [.. Enumerable.Range(0, HttpFields.MaxFields + 1).Select(i => ($"X-Field-{i}", "v"))]))
                Require(tooMany.StatusCode == HttpStatusCode.RequestHeaderFieldsTooLarge &&
                        (await tooMany.Content.ReadAsStringAsync(token)).Contains($"at most {HttpFields.MaxFields}", StringComparison.Ordinal),
                    $"A request over the model's header bound is refused with 431 before it reaches a workflow. Observed {(int)tooMany.StatusCode}.");
            var control = await RawPostAsync("echo-headers", "X-Bad: a\u0001b");
            Require(control.Status == 400,
                $"A request header the model's rules refuse, here a control character Kestrel lets through, is answered 400. Observed {control.Status}.");
            using (var designed = await PostAsync("designed?id=7"))
                Require(designed.StatusCode == HttpStatusCode.Created && await designed.Content.ReadAsStringAsync(token) == "accepted 7",
                    $"A request starts a designer-shaped root Flowchart at its HttpEndpoint start node through the public ingress. Observed {(int)designed.StatusCode}.");
            using (var enriched = await PostAsync("Orders/42/Enrich"))
                Require(enriched.StatusCode == HttpStatusCode.OK && await enriched.Content.ReadAsStringAsync(token) == "enrich 42",
                    "A start on a route template gets its route value through the Kestrel edge.");
            using (var listed = await PostAsync("items/list"))
            using (var item = await PostAsync("items/7"))
                Require(await listed.Content.ReadAsStringAsync(token) == "the list" && await item.Content.ReadAsStringAsync(token) == "item 7",
                    "The most specific template wins: items/list goes to its literal route, items/7 to items/{id}.");
            foreach (var order in new[] { "41", "42", "a b", "A", "%41", "A/42" })
                using (var shipping = await PostAsync($"orders/{Uri.EscapeDataString(order)}/ship"))
                    Require(shipping.StatusCode == HttpStatusCode.Accepted && await shipping.Content.ReadAsStringAsync(token) == $"shipping {order}",
                        $"Order '{order}' is shipping and waits for its own cancel.");
            using (var cancelled = await PostAsync("orders/42/cancel"))
                Require(await cancelled.Content.ReadAsStringAsync(token) == "cancelled 42 by 42",
                    "A cancel on orders/42/cancel resumes only the instance waiting for order 42.");
            using (var again = await PostAsync("orders/42/cancel"))
            using (var spaced = await PostAsync("orders/a%20b/cancel"))
            using (var other = await PostAsync("orders/41/cancel"))
                Require(again.StatusCode == HttpStatusCode.NotFound && await spaced.Content.ReadAsStringAsync(token) == "cancelled a b by a b" &&
                        await other.Content.ReadAsStringAsync(token) == "cancelled 41 by 41",
                    $"A finished order's cancel finds nothing, and the other order is still there for its own cancel. Observed {(int)again.StatusCode}.");
            using (var percent = await PostAsync("orders/%2541/cancel"))
            using (var plain = await PostAsync("orders/A/cancel"))
            using (var slashed = await PostAsync("orders/A%2F42/cancel"))
                Require(await percent.Content.ReadAsStringAsync(token) == "cancelled %41 by %41" && await plain.Content.ReadAsStringAsync(token) == "cancelled A by A" &&
                        await slashed.Content.ReadAsStringAsync(token) == "cancelled A/42 by A/42",
                    "Escaped ids are decoded exactly once: %2541 is order '%41', not order 'A', and A%2F42 is order 'A/42'.");
            var dots = await RawPostAsync("orders/%2E%2E/cancel");
            Require(dots.Status == 404, $"A '..' segment sent as the client wrote it is no id for the matcher. Observed {dots.Status}.");
            foreach (var order in new[] { "%41", "A" })
                using (var shipping = await PostAsync($"orders/{Uri.EscapeDataString(order)}/ship"))
                    Require(shipping.StatusCode == HttpStatusCode.Accepted, $"Order '{order}' ships again.");
            var absolute = await RawPostAsync($"{client.BaseAddress!.GetLeftPart(UriPartial.Authority)}/orders/%2541/cancel");
            var stepped = await RawPostAsync("orders/x/../A/cancel");
            var url = await RawPostAsync("fetch/https://example.com/orders/1");
            Require(url is { Status: 200, Body: "fetch https://example.com/orders/1" },
                $"An origin-form path that holds '://' is matched as it is, here by a catch-all start. Observed {url.Status} '{url.Body}'.");
            Require(absolute is { Status: 200, Body: "cancelled %41 by %41" } && stepped is { Status: 200, Body: "cancelled A by A" },
                $"An absolute-form target is decoded once too, and a literal '..' step is removed as Kestrel removes it. Observed {absolute.Status} '{absolute.Body}', {stepped.Status} '{stepped.Body}'.");
            using (var computed = new HttpRequestMessage(HttpMethod.Post, "ship-computed") { Content = JsonContent.Create(new { id = "Zz" }) })
                Require((await client.SendAsync(computed, token)).StatusCode == HttpStatusCode.OK, "A workflow waits on a cancel path it computes.");
            using (var computedCancel = await PostAsync("orders/Zz/cancel"))
                Require(await computedCancel.Content.ReadAsStringAsync(token) == "computed Zz",
                    "A computed wait that shares the cancel template's shape is reached by the edge's literal retry.");
            using (var refused = await PostAsync("bad-headers"))
                Require(refused.StatusCode == HttpStatusCode.InternalServerError &&
                        await refused.Content.ReadAsStringAsync(token) == "the workflow faulted",
                    "A response that sets a header the edge owns faults its workflow, and the caller gets 500.");
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
                retryId, new(KeepFor: retention))
            : DispatchAsync(context, scope, routes, ingress, Now() + requestTime, token);
        Task<HttpResponseMessage> PostAsync(string path) => client.PostAsync(path, null, token);

        async Task<HttpResponseMessage> SendWithHeadersAsync(string path, params (string Name, string Value)[] headers)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
            return await client.SendAsync(request, token);
        }

        // HttpClient folds repeated headers into one line; a raw request sends each line as given.
        async Task<(int Status, string Body)> RawPostAsync(string path, params string[] headerLines)
        {
            var address = new Uri(app.Urls.Single());
            using var socket = new System.Net.Sockets.TcpClient();
            await socket.ConnectAsync(address.Host, address.Port, token);
            await using var stream = socket.GetStream();
            var target = path.StartsWith("http://", StringComparison.Ordinal) ? path : "/" + path;     // an absolute-form target as given
            var head = $"POST {target} HTTP/1.1\r\nHost: {address.Authority}\r\n{string.Concat(headerLines.Select(line => line + "\r\n"))}Content-Length: 0\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(head), token);
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync(token);
            var status = int.Parse(text.Split(' ', 3)[1]);
            var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var payload = text[(split + 4)..];
            if (text[..split].Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
            {
                var decoded = new System.Text.StringBuilder();
                for (var at = 0; at < payload.Length;)
                {
                    var line = payload.IndexOf("\r\n", at, StringComparison.Ordinal);
                    var size = Convert.ToInt32(payload[at..line].Split(';', 2)[0].Trim(), 16);     // without any chunk extension
                    if (size == 0) break;
                    decoded.Append(payload, line + 2, size);
                    at = line + 2 + size + 2;
                }
                payload = decoded.ToString();
            }
            return (status, payload);
        }

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
        IReadOnlyDictionary<string, string> query, headers;
        // Each pair goes in raw and in order, so the model's rules join the repeats and keep each name as sent
        // (Request.Query would have merged names that differ only in case).
        try { query = HttpFields.Query(QueryPairs(request.QueryString.Value)); }
        catch (HttpFieldsException refused) { return Results.Text(refused.Message, statusCode: refused.OverBound ? StatusCodes.Status414UriTooLong : 400); }
        try { headers = HttpFields.RequestHeaders(request.Headers.SelectMany(field => field.Value.Select(value => KeyValuePair.Create(field.Key, value ?? "")))); }
        catch (HttpFieldsException refused) { return Results.Text(refused.Message, statusCode: refused.OverBound ? StatusCodes.Status431RequestHeaderFieldsTooLarge : 400); }
        // The route templates are matched on the path as the client sent it, so that the matcher decodes it once: Request.Path
        // is already decoded but for %2F, and a value of '%41' sent as %2541 would be decoded twice.
        var sent = SentPath(context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? request.Path.ToUriComponent());
        var match = HttpRouteMatcher.Match(routes, request.Method, sent);
        Kernel.Native.HttpRequest message;
        try { message = new Kernel.Native.HttpRequest(request.Method, request.Path, body) { Query = query, Headers = headers, RouteValues = match.Values }; }
        catch (HttpFieldsException refused) { return Results.Text(refused.Message, statusCode: StatusCodes.Status414UriTooLong); }
        // The edge picks each request's event id; route values never go into it.
        var id = Guid.NewGuid().ToString("N");
        Contract.Reply reply;
        if (match.Route is { Start: true } route)
            reply = await ingress.RequestAsync(scope, new Contract.Target.Start(PublicWorkflow.Start(
                    NativeExecutionModel.Start(route.Workflow, triggerNode: route.Node, trigger: Values.From(message)))),
                id, Contract.Kinds.Start, null, null, new Contract.RequestOptions(null, deadline, route.RespondBeforeCommit), token);
        else
        {
            reply = await ingress.RequestAsync(scope, new Contract.Target.Event(match.Key), eventId ?? id, Contract.Kinds.Event, JsonSerializer.Serialize(message),
                eventOptions, new Contract.RequestOptions(null, deadline, match.Route?.RespondBeforeCommit ?? false), token);
            // A computed wait may share a wait template's shape: with no waiter on the template's key, the request goes to
            // its literal key, without route values (design card 1; a "no waiter" publishes nothing, FC-C2).
            var literal = HttpRouteMatcher.Literal(request.Method, sent);
            if (match.Route is not null && reply.Error == "no waiter" && literal.Key != match.Key)
                reply = await ingress.RequestAsync(scope, new Contract.Target.Event(literal.Key), eventId ?? id, Contract.Kinds.Event,
                    JsonSerializer.Serialize(message with { RouteValues = literal.Values }), eventOptions, new Contract.RequestOptions(null, deadline, false), token);
        }

        return reply switch
        {
            { Error: "no waiter" or "missing" } => Results.Text("no workflow waits on this route", statusCode: 404),
            { Error: "timeout" } => Results.Text("the workflow did not answer in time", statusCode: 504),
            { Error: "lost" } => Results.Text("the workflow's owner was lost", statusCode: 503),
            { Status: Contract.ActorStatus.Faulted } => Results.Text("the workflow faulted", statusCode: 500),
            { Error: null, Channel: "response", Item: JsonNode response } when Values.To<Kernel.Native.HttpResponse>(response) is { } answer =>
                Answer(context.Response, answer),
            { Error: null, Item: null } => Results.NoContent(),
            _ => Results.StatusCode(500)
        };
    }

    // The path of the request target as the client sent it: without the scheme and authority of an absolute-form target
    // and without the query, and with the literal '.' and '..' segments removed as Kestrel removes them (RFC 3986,
    // 5.2.4); an escaped %2E%2E stays a segment, which the matcher takes for no value.
    private static string SentPath(string target)
    {
        var path = target.Split('?', 2)[0];
        if (path is not ['/', ..] && path.IndexOf("://", StringComparison.Ordinal) is var scheme and >= 0)
            path = path.IndexOf('/', scheme + 3) is var start and >= 0 ? path[start..] : "/";
        var kept = new List<string>();
        foreach (var segment in path.Split('/'))
            if (segment == "..") { if (kept.Count > 1) kept.RemoveAt(kept.Count - 1); }
            else if (segment != ".") kept.Add(segment);
        return string.Join('/', kept);
    }

    private static List<KeyValuePair<string, string>> QueryPairs(string? queryString)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var pair in new Microsoft.AspNetCore.WebUtilities.QueryStringEnumerable(queryString))
            pairs.Add(KeyValuePair.Create(pair.DecodeName().ToString(), pair.DecodeValue().ToString()));
        return pairs;
    }

    // The response's own headers go out with it; the content type is the response's field (HttpFields.Reserved).
    private static IResult Answer(Microsoft.AspNetCore.Http.HttpResponse response, Kernel.Native.HttpResponse answer)
    {
        foreach (var (name, value) in answer.Headers) response.Headers[name] = value;
        return Results.Text(answer.Body is JsonValue scalar && answer.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
            scalar.TryGetValue<string>(out var text) ? text : answer.Body?.ToJsonString(), answer.ContentType, statusCode: answer.StatusCode);
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

    private sealed record EventAttempt(string Id, TimeSpan? KeepFor);

    private sealed class RecordingIngress(Contract.IIngress inner, string correlationKey) : Contract.IIngress
    {
        internal ConcurrentQueue<EventAttempt> Attempts { get; } = new();

        public ValueTask<Contract.Reply> RequestAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
            string? payload, Contract.AddOptions? add, Contract.RequestOptions request, CancellationToken cancellationToken = default)
        {
            if (target is Contract.Target.Event @event && @event.CorrelationKey == correlationKey)
                Attempts.Enqueue(new(id, add?.KeepFor));
            return inner.RequestAsync(scope, target, id, kind, payload, add, request, cancellationToken);
        }

        public ValueTask<Contract.Admission> SendAsync(Contract.BoundScope scope, Contract.Target target, string id, string kind,
            string? payload = null, Contract.AddOptions? options = null, CancellationToken cancellationToken = default) =>
            inner.SendAsync(scope, target, id, kind, payload, options, cancellationToken);

        public ValueTask SetStartAsync(Contract.BoundScope scope, string key, string wait, Contract.StartSpec? spec,
            CancellationToken cancellationToken = default) => inner.SetStartAsync(scope, key, wait, spec, cancellationToken);
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
