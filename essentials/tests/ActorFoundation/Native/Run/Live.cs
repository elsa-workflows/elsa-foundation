using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Kernel.Native;

namespace Kernel.Native.Run;

// HTTP on the kernel's live output (merge): the model emits with ITurn.Emit, the kernel releases after the commit of the
// turn that wrote it and sends it to the reply addresses of the messages the instance consumed, and each node keeps its
// open requests in the kernel's Requests, which take the first response and ignore whatever comes after. The native
// branch's own channel (ILiveChannel, LiveNode) and the kernel hooks it needed (NATIVE N2: OnApplied, OnReset) are gone.

/// <summary>
/// The transport between nodes, in process, for the tests: not durable, and what goes to a node that is gone is dropped.
/// Between the workers of one node it is a call, as the kernel's transport is.
/// </summary>
public sealed class Network
{
    private readonly ConcurrentDictionary<string, Requests> _nodes = new(StringComparer.Ordinal);

    /// <summary>Responses sent, whether or not anybody still waited for them.</summary>
    public int Sent => Volatile.Read(ref _sent);

    /// <summary>Responses that reached an open request: on the node itself, and from another node.</summary>
    public int Answers => Volatile.Read(ref _answers);
    public int Hops => Volatile.Read(ref _hops);
    private int _sent, _answers, _hops;

    /// <summary>For tests: called as a response reaches its open request, with the reply.</summary>
    public Action<Reply>? OnAnswer { get; set; }

    public Requests Node(string name) => _nodes.GetOrAdd(name, static _ => new Requests());

    /// <summary>The kernel's transport as the workers of <paramref name="from"/> see it.</summary>
    public IReplyTransport From(string from) => new Link(this, from);

    private sealed class Link(Network network, string from) : IReplyTransport
    {
        public void Send(string node, Reply reply)
        {
            if (reply.Channel == Request.Response) Interlocked.Increment(ref network._sent);
            if (!network._nodes.TryGetValue(node, out var requests)) return;
            if (!requests.TryReceive(reply) || reply.Channel != Request.Response) return;
            Interlocked.Increment(ref network._answers);
            if (node != from) Interlocked.Increment(ref network._hops);
            network.OnAnswer?.Invoke(reply);
        }
    }
}

/// <summary>
/// A node's HTTP front: a request is matched against the route templates (<see cref="HttpRouteMatcher"/>), and carries
/// the matched route values. One that matches a start route starts a workflow in a partition this node owns, hands it
/// the request as the turn's ambient value, and observes it; any other request resumes the instance that waits on its
/// resolved route, wherever it is owned, by a delivery that carries this node's reply address. The host's <c>pump</c> runs
/// the workers; a request still open when the pump returns times out.
/// </summary>
public sealed class HttpHost(string node, Requests requests, HttpRoutes routes, IKernelStore store)
{
    private long _requests;

    public async Task<HttpResponse> SendAsync(HttpRequest request, Worker local, string mode, Func<Task> pump)
    {
        var id = "q" + Interlocked.Increment(ref _requests);
        var match = HttpRouteMatcher.Match(routes, request.Method, request.Path);
        request = request with { RouteValues = match.Values };
        var open = requests.Open(id, local.Now + 30_000);
        var replyTo = open.ReplyAddress(node, match.Route?.RespondBeforeCommit ?? false);
        try
        {
            if (match.Route is { Start: true } route)
            {
                var instance = InstanceRef.In(local.Partition, $"http-{node}-{open.Registration}");
                // Decision 4: an Ephemeral run may wait in memory for its own short timers and jobs up to the route's timeout.
                var spec = NativeModel.Spec(route.Workflow, mode, null, route.Node) with { ShortWait = TimeSpan.FromMilliseconds(open.Deadline - local.Now) };
                local.Post(new Message($"{instance}#start", instance, Kinds.Start, Start: spec, ReplyTo: replyTo), fresh: true, ambient: Values.From(request));
            }
            else
            {
                var key = match.Key;
                var waiting = await store.FindSubscriptionsAsync(key);
                if (waiting.Count == 0) return new HttpResponse(404, "text/plain", "no workflow waits on this route");
                var delivery = new EventRoute(new EventRow(key, $"{node}.{open.Registration}", Json.Write(request), local.Now + 60_000), waiting[0]).Delivery(replyTo: replyTo);
                await store.AddEventAsync(delivery);
            }
            await pump();
            requests.Timeout(open);     // only this registration's pump has returned
            return open switch
            {
                { Answer: JsonNode answer } => Values.To<HttpResponse>(answer)!,
                { Error: "lost" } => new HttpResponse(503, "text/plain", "the workflow's owner was lost"),
                { Error: "timeout" } => new HttpResponse(504, "text/plain", "the workflow did not answer in time"),
                { Error: "missing" } => new HttpResponse(404, "text/plain", "the workflow does not exist"),
                { Status: InstanceStatus.Faulted } => new HttpResponse(500, "text/plain", "the workflow faulted"),
                _ => new HttpResponse(204, "text/plain", null),     // it ended without a response
            };
        }
        finally { requests.Disconnect(open); }
    }
}
