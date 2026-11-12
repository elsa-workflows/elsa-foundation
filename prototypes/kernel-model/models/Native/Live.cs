using System.Text.Json.Nodes;

namespace Kernel.Native;

// HTTP on the kernel's live output (merge): the model emits with IStep.Emit, the kernel releases after the commit of the
// step that wrote it and sends it to the reply addresses of the messages the instance consumed, and each node keeps its
// open requests in the kernel's Requests, which take the first response and ignore whatever comes after. The native
// branch's own channel (ILiveChannel, LiveNode) and the kernel hooks it needed (NATIVE N2: OnApplied, OnReset) are gone.

/// <summary>
/// The transport between nodes, in process, for the tests: not durable, and what goes to a node that is gone is dropped.
/// Between the workers of one node it is a call, as the kernel's transport is.
/// </summary>
public sealed class Network
{
    private readonly Dictionary<string, Requests> _nodes = new(StringComparer.Ordinal);

    /// <summary>Responses sent, whether or not anybody still waited for them.</summary>
    public int Sent { get; private set; }

    /// <summary>Responses that reached an open request: on the node itself, and from another node.</summary>
    public int Answers { get; private set; }
    public int Hops { get; private set; }

    /// <summary>For tests: called as a response reaches its open request, with the reply.</summary>
    public Action<Reply>? OnAnswer { get; set; }

    public Requests Node(string name) => _nodes.TryGetValue(name, out var requests) ? requests : _nodes[name] = new Requests();

    /// <summary>The kernel's transport as the workers of <paramref name="from"/> see it.</summary>
    public IReplyTransport From(string from) => new Link(this, from);

    private sealed class Link(Network network, string from) : IReplyTransport
    {
        public void Send(string node, Reply reply)
        {
            if (reply.Channel == Request.Response) network.Sent++;
            if (!network._nodes.TryGetValue(node, out var requests)) return;
            var open = requests.All.FirstOrDefault(r => r.Id == reply.Request) is { Done: false };
            requests.Receive(reply);
            if (!open || reply.Channel != Request.Response) return;
            network.Answers++;
            if (node != from) network.Hops++;
            network.OnAnswer?.Invoke(reply);
        }
    }
}

/// <summary>The in-memory route table, built from the published definitions: routing reads nothing.</summary>
public sealed class HttpRoutes
{
    private readonly Dictionary<string, (string Workflow, string Node)> _starts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _early = new(StringComparer.Ordinal);

    public static string Key(string method, string path) => $"http:{method.ToUpperInvariant()}:{path.Trim('/')}";

    public HttpRoutes(WorkflowRegistry registry)
    {
        foreach (var workflow in registry.All)
        {
            // Every route with a literal path: its opt-out of the release rule (the owner's default of 12 November is to
            // release a response after the commit of the step that wrote it).
            foreach (var node in workflow.Nodes.Values.Where(n => n.Type == typeof(HttpEndpoint)))
                if (node.Inputs.GetValueOrDefault("Path") is LiteralBinding { Value: { } p } && node.Inputs.GetValueOrDefault("RespondBeforeCommit") is LiteralBinding { Value: { } early } && early.GetValue<bool>())
                    _early.Add(Key(Method(node), p.GetValue<string>()));
            // A start trigger is an HttpEndpoint at the root or first in a root sequence, with a literal path.
            var first = workflow.Root.Type == typeof(Sequence) && workflow.Root.Children.Count > 0 ? workflow.Root.Children[0] : workflow.Root;
            if (first.Type != typeof(HttpEndpoint) || first.Inputs.GetValueOrDefault("Path") is not LiteralBinding { Value: { } path }) continue;
            _starts[Key(Method(first), path.GetValue<string>())] = (workflow.Id, first.Id);
        }
    }

    private static string Method(Node node) => node.Inputs.GetValueOrDefault("Method") is LiteralBinding { Value: { } m } ? m.GetValue<string>() : "POST";

    public bool TryStart(string method, string path, out (string Workflow, string Node) route) => _starts.TryGetValue(Key(method, path), out route);

    /// <summary>The route's caller is answered when the step ends, not after its commit.</summary>
    public bool BeforeCommit(string method, string path) => _early.Contains(Key(method, path));
}

/// <summary>
/// A node's HTTP front: a request that matches a start route starts a workflow in a partition this node owns, hands
/// it the request as the step's ambient value, and observes it; any other request resumes the instance that waits on
/// its route, wherever it is owned, by a delivery that carries this node's reply address. The host's <c>pump</c> runs
/// the workers; a request still open when the pump returns times out.
/// </summary>
public sealed class HttpHost(string node, Requests requests, HttpRoutes routes, IKernelStore store)
{
    private long _requests;

    public async Task<HttpResponse> SendAsync(HttpRequest request, Worker local, string mode, Func<Task> pump)
    {
        var id = "q" + ++_requests;
        var open = requests.Open(id, local.Now + 30_000);
        var replyTo = ReplyTo.Of(node, id, routes.BeforeCommit(request.Method, request.Path));
        if (routes.TryStart(request.Method, request.Path, out var route))
        {
            var instance = InstanceRef.In(local.Partition, $"http-{node}-{_requests}");
            var spec = NativeModel.Spec(route.Workflow, mode, null, route.Node);
            local.Post(new Message($"{instance}#start", instance, Kinds.Start, Start: spec, ReplyTo: replyTo), fresh: true, ambient: Values.From(request));
        }
        else
        {
            var key = HttpRoutes.Key(request.Method, request.Path);
            var waiting = await store.FindSubscriptionsAsync(key);
            if (waiting.Count == 0) return new HttpResponse(404, "text/plain", "no workflow waits on this route");
            var delivery = Events.Deliver(new EventRow(key, $"{node}.{id}", Json.Write(request), local.Now + 60_000), waiting[0]);
            await store.AddAsync(delivery with { ReplyTo = replyTo });
        }
        await pump();
        requests.Expire(open.Deadline);     // the pump gave up: past this request's deadline
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
}
