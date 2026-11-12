using System.Text.Json.Nodes;

namespace Kernel.Native;

// The live channel, in its smallest form, behind one interface so that the kernel's Emit, Ambient and start-and-observe
// (built on another branch) can replace it. Nothing here is durable.

/// <summary>The one seam: what a step emitted, handed over at the step's end.</summary>
public interface ILiveChannel
{
    /// <summary>
    /// Unless <paramref name="beforeCommit"/>, the item is held until the step's result is applied: its commit landed,
    /// or it had nothing to write. <paramref name="replyTo"/> is "node/request", or null when nobody waits.
    /// </summary>
    void Emit(string instance, string? replyTo, string channel, JsonNode? item, bool beforeCommit);
}

/// <summary>The transport between nodes. Not durable: an item for a node or request that is gone is dropped.</summary>
public sealed class LiveNetwork
{
    private readonly Dictionary<string, LiveNode> _nodes = new(StringComparer.Ordinal);
    public LiveNode Node(string name) => _nodes.TryGetValue(name, out var node) ? node : _nodes[name] = new LiveNode(name, this);
    public int Hops { get; private set; }

    internal void Send(string replyTo, string channel, JsonNode? item, string from)
    {
        var slash = replyTo.IndexOf('/');
        if (slash < 0 || !_nodes.TryGetValue(replyTo[..slash], out var node)) return;
        if (node.Name != from) Hops++;
        node.Receive(replyTo[(slash + 1)..], channel, item);
    }
}

/// <summary>One node's side: items held until their step is applied, and the requests that are open here.</summary>
public sealed class LiveNode(string name, LiveNetwork network) : ILiveChannel
{
    private readonly Dictionary<string, List<(string ReplyTo, string Channel, JsonNode? Item)>> _held = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (TaskCompletionSource<HttpResponse> Response, string? Instance)> _open = new(StringComparer.Ordinal);
    private long _requests;

    public string Name => name;
    public int Released { get; private set; }
    public int Dropped { get; private set; }
    /// <summary>For tests: called as a response is released, with its instance.</summary>
    public Action<string>? OnRelease { get; set; }

    /// <summary>Open a request on this node: its reply address and the response it will get.</summary>
    public (string ReplyTo, Task<HttpResponse> Response) Open()
    {
        var id = "q" + ++_requests;
        var response = new TaskCompletionSource<HttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _open[id] = (response, null);
        return ($"{name}/{id}", response.Task);
    }

    /// <summary>The instance the request waits on, so that losing its owner ends the request instead of hanging it.</summary>
    public void Watch(string replyTo, string instance)
    {
        var id = replyTo[(replyTo.IndexOf('/') + 1)..];
        if (_open.TryGetValue(id, out var open)) _open[id] = (open.Response, instance);
    }

    public void Close(string replyTo, HttpResponse response)
    {
        if (_open.Remove(replyTo[(replyTo.IndexOf('/') + 1)..], out var open)) open.Response.TrySetResult(response);
    }

    public void Emit(string instance, string? replyTo, string channel, JsonNode? item, bool beforeCommit)
    {
        if (replyTo is null) return;
        if (beforeCommit)
        {
            Release(instance, replyTo, channel, item);
            return;
        }
        (_held.TryGetValue(instance, out var list) ? list : _held[instance] = []).Add((replyTo, channel, item));
    }

    /// <summary>The kernel's hook (NATIVE N2): the step's result is in memory. Released if it landed, dropped if it was parked.</summary>
    public void Applied(string instance, bool landed)
    {
        if (!_held.Remove(instance, out var items)) return;
        foreach (var (replyTo, channel, item) in items)
            if (landed) Release(instance, replyTo, channel, item);
            else Dropped++;
    }

    /// <summary>The kernel's hook (NATIVE N2): a worker dropped its memory. Held items of its partition are lost; their requests get 503.</summary>
    public void Reset(int partition)
    {
        foreach (var instance in _held.Keys.Where(i => new InstanceRef(i).Partition == partition).ToList())
        {
            Dropped += _held[instance].Count;
            _held.Remove(instance);
        }
        foreach (var (id, open) in _open.Where(o => o.Value.Instance is { } i && new InstanceRef(i).Partition == partition).ToList())
        {
            _open.Remove(id);
            open.Response.TrySetResult(new HttpResponse(503, "text/plain", "the workflow's owner was lost"));
        }
    }

    private void Release(string instance, string replyTo, string channel, JsonNode? item)
    {
        Released++;
        OnRelease?.Invoke(instance);
        network.Send(replyTo, channel, item, name);
    }

    internal void Receive(string request, string channel, JsonNode? item)
    {
        if (channel != HttpRoutes.ResponseChannel || !_open.Remove(request, out var open)) return;   // gone, or answered already: never two responses
        open.Response.TrySetResult(Values.To<HttpResponse>(item)!);
    }
}

/// <summary>The in-memory route table, built from the published definitions: routing reads nothing.</summary>
public sealed class HttpRoutes
{
    public const string ResponseChannel = "http.response";
    private readonly Dictionary<string, (string Workflow, string Node)> _starts = new(StringComparer.Ordinal);

    public static string Key(string method, string path) => $"http:{method.ToUpperInvariant()}:{path.Trim('/')}";

    public HttpRoutes(WorkflowRegistry registry)
    {
        foreach (var workflow in registry.All)
        {
            // A start trigger is an HttpEndpoint at the root or first in a root sequence, with a literal path.
            var first = workflow.Root.Type == typeof(Sequence) && workflow.Root.Children.Count > 0 ? workflow.Root.Children[0] : workflow.Root;
            if (first.Type != typeof(HttpEndpoint) || first.Inputs.GetValueOrDefault("Path") is not LiteralBinding { Value: { } path }) continue;
            var method = first.Inputs.GetValueOrDefault("Method") is LiteralBinding { Value: { } m } ? m.GetValue<string>() : "POST";
            _starts[Key(method, path.GetValue<string>())] = (workflow.Id, first.Id);
        }
    }

    public bool TryStart(string method, string path, out (string Workflow, string Node) route) => _starts.TryGetValue(Key(method, path), out route);
}

/// <summary>
/// A node's HTTP front: a request that matches a start route starts a workflow in a partition this node owns and
/// observes it; any other request resumes the instance that waits on its route, wherever it is owned, with a reply
/// address. The host's <c>pump</c> runs the node's workers; a request still open when the pump returns times out.
/// </summary>
public sealed class HttpHost(LiveNode live, HttpRoutes routes, IKernelStore store)
{
    private long _starts;

    public async Task<HttpResponse> SendAsync(HttpRequest request, Worker local, string mode, Func<Task> pump)
    {
        var (replyTo, response) = live.Open();
        if (routes.TryStart(request.Method, request.Path, out var route))
        {
            var id = InstanceRef.In(local.Partition, $"http-{live.Name}-{++_starts}");
            live.Watch(replyTo, id.Id);
            var spec = NativeModel.Spec(route.Workflow, mode, null, route.Node, Values.From(request with { ReplyTo = replyTo }), replyTo);
            local.Post(new Message($"{id}#start", id, Kinds.Start, Start: spec), fresh: true);
        }
        else
        {
            var key = HttpRoutes.Key(request.Method, request.Path);
            var waiting = await store.FindSubscriptionsAsync(key);
            if (waiting.Count == 0)
            {
                live.Close(replyTo, new HttpResponse(404, "text/plain", "no workflow waits on this route"));
                return await response;
            }
            var wait = waiting[0];
            live.Watch(replyTo, wait.Instance);
            var keep = local.Now + 60_000;
            await store.AddAsync(Events.Deliver(new EventRow(key, replyTo.Replace('/', '.'), Json.Write(request with { ReplyTo = replyTo }), keep), wait));
        }
        await pump();
        if (!response.IsCompleted) live.Close(replyTo, new HttpResponse(504, "text/plain", "the workflow did not answer in time"));
        return await response;
    }
}
