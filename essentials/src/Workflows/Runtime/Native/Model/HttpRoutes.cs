using Kernel.Graph.Exceptions;

namespace Kernel.Native;

/// <summary>
/// The in-memory route table, built from the published definitions: routing reads nothing. A route is its method in
/// upper case and its path as a <see cref="HttpRouteTemplate"/>: trimmed of white space and slashes, literals and
/// parameter names in lower case, as the old core canonicalizes a template.
/// <list type="bullet">
/// <item>A start route is an HttpEndpoint with a literal path where a run begins (<see cref="StartsOf"/>). A wait route is
/// any other HttpEndpoint with a literal path; it subscribes on its template resolved with its route values, so a
/// request on <c>orders/42/cancel</c> resumes only the instance that waits for order 42.</item>
/// <item>One route shape starts one workflow at one endpoint: a second start with the same method and shape
/// (<c>orders/{id}</c> and <c>orders/{key}</c>) is refused with <see cref="HttpRouteConflictException"/>, as the old core
/// refuses it at publish. So is a shape that is a start in one place and a wait in another, so that a request needs one
/// lookup. A definition published again replaces its own routes (the registry keeps one workflow per id), and one path
/// may start different workflows on different methods.</item>
/// <item>A template the model does not accept, or a wait's template with an optional or catch-all parameter, is refused
/// with <see cref="HttpRouteTemplateException"/>.</item>
/// </list>
/// Matching a request against the templates, most specific first (<see cref="HttpRouteTemplate.Compare"/>), is the HTTP
/// edge's work; it starts on a start route and resolves a wait route to its key with <see cref="WaitKey"/>.
/// </summary>
public sealed class HttpRoutes
{
    /// <summary>A key longer than this many UTF-8 bytes is kept as the SHA-256 of its path.</summary>
    public const int MaxReadableKeyBytes = 512;

    private readonly Dictionary<string, (string Workflow, string Node)> _starts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _early = new(StringComparer.Ordinal);
    private readonly List<HttpRoute> _routes = [];

    /// <summary>
    /// The subscription key of a method and path: <c>http:{METHOD}:{path}</c>, with the path trimmed and in lower case,
    /// or <c>http:{METHOD}:h:{sha-256 of the path}</c> past <see cref="MaxReadableKeyBytes"/>.
    /// </summary>
    public static string Key(string method, string path)
    {
        var (verb, normalized) = (method.ToUpperInvariant(), Normalize(path));
        var key = $"http:{verb}:{normalized}";
        return System.Text.Encoding.UTF8.GetByteCount(key) <= MaxReadableKeyBytes
            ? key
            : $"http:{verb}:h:{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)))}";
    }

    /// <summary>The key a wait route subscribes on for these route values, and a matched request is delivered to.</summary>
    public static string WaitKey(string method, HttpRouteTemplate template, IReadOnlyDictionary<string, string>? values) =>
        Key(method, template.Resolve(values));

    private static string Normalize(string path) => path.Trim().Trim('/').ToLowerInvariant();

    /// <summary>The start and wait routes with a literal path, most specific first.</summary>
    public IReadOnlyList<HttpRoute> Routes => _routes;

    public HttpRoutes(WorkflowRegistry registry)
    {
        var shapes = new Dictionary<string, HttpRoute>(StringComparer.Ordinal);
        foreach (var workflow in registry.All)
        {
            var starts = StartsOf(workflow.Root).ToHashSet();
            foreach (var node in workflow.Nodes.Values.Where(n => n.Type == typeof(HttpEndpoint)))
            {
                if (node.Inputs.GetValueOrDefault("Path") is not LiteralBinding { Value: { } literal }) continue;
                HttpRouteTemplate template;
                try { template = HttpRouteTemplate.Parse(literal.GetValue<string>()); }
                catch (HttpRouteTemplateException refused) { throw new HttpRouteTemplateException($"Workflow '{workflow.Id}', endpoint '{node.Id}': {refused.Message}"); }
                var start = starts.Contains(node);
                if (!start && !template.FullyBound)
                    throw new HttpRouteTemplateException($"Workflow '{workflow.Id}', endpoint '{node.Id}': the wait's route '{template.Text}' has an optional or catch-all parameter; a wait's route is fully bound.");
                // Its opt-out of the release rule (the owner's default of 12 November is to release a response after the
                // commit of the turn that wrote it).
                var early = node.Inputs.GetValueOrDefault("RespondBeforeCommit") is LiteralBinding { Value: { } flag } && flag.GetValue<bool>();
                var route = new HttpRoute(Method(node).ToUpperInvariant(), template, workflow.Id, node.Id, start, early);
                // Two waits on one shape are fine: each resolves to its own key. Anything else on one shape is refused.
                if (shapes.TryGetValue($"{route.Method} {template.Shape}", out var claimed) && (claimed.Start || start))
                    throw new HttpRouteConflictException(route.Method, template.Text, (claimed.Workflow, claimed.Node), (workflow.Id, node.Id),
                        startAndWait: claimed.Start != start);
                if (start || !shapes.ContainsKey($"{route.Method} {template.Shape}")) shapes[$"{route.Method} {template.Shape}"] = route;
                _routes.Add(route);
                if (start) _starts[Key(route.Method, template.Text)] = (workflow.Id, node.Id);
                if (early) _early.Add(Key(route.Method, template.Text));
            }
        }
        _routes.Sort((a, b) => HttpRouteTemplate.Compare(a.Template, b.Template));
    }

    // The nodes a run of the root can begin at. In a Flowchart, a run triggered at a child begins there (the graph
    // engine's SelectEntryNode), so every child without an inbound connection is an entry, as the old core's start-path
    // rule has it. A child a connection leads to is not one, even when the engine falls back to it as the start node: a
    // loop that comes back to it would wait there on the start route's own key, and no request could reach that wait.
    // A graph the engine refuses has no entry; its run faults on its own.
    private static IEnumerable<Node> StartsOf(Node root)
    {
        if (root.Children.Count == 0) return [root];
        if (root.Type == typeof(Sequence)) return [root.Children[0]];
        if (root.Type != typeof(Flowchart)) return [root];
        try { _ = Flowchart.GraphOf(root); }
        catch (Exception refused) when (refused is FlowchartExecutionException or ArgumentException) { return []; }
        return root.Children.Where(child => Flowchart.IsEntry(root, child));
    }

    private static string Method(Node node) => node.Inputs.GetValueOrDefault("Method") is LiteralBinding { Value: { } m } ? m.GetValue<string>() : "POST";

    /// <summary>The start route with this literal path, for an edge that does not match templates.</summary>
    public bool TryStart(string method, string path, out (string Workflow, string Node) route) => _starts.TryGetValue(Key(method, path), out route);

    /// <summary>The literal route's caller is answered when the turn ends, not after its commit (<see cref="HttpRoute.RespondBeforeCommit"/>).</summary>
    public bool BeforeCommit(string method, string path) => _early.Contains(Key(method, path));
}

/// <summary>A start or wait route the HTTP edge matches requests against.</summary>
public sealed record HttpRoute(string Method, HttpRouteTemplate Template, string Workflow, string Node, bool Start, bool RespondBeforeCommit);

/// <summary>
/// Two endpoints claim one method and route shape: two starts, or a start and a wait. A request must reach one of them, so
/// the route table refuses the second claim.
/// </summary>
public sealed class HttpRouteConflictException(string method, string path, (string Workflow, string Node) first, (string Workflow, string Node) second, bool startAndWait = false)
    : InvalidOperationException(Describe(method, path, first, second, startAndWait))
{
    /// <summary>The method both claim, in upper case.</summary>
    public string Method { get; } = method;

    /// <summary>The second claim's normalized template, for example <c>orders/{id}</c>.</summary>
    public string Path { get; } = path;

    /// <summary>The claim met first in the registry's order, which is not a publication order.</summary>
    public (string Workflow, string Node) First { get; } = first;

    /// <summary>The claim met second, at which the table was refused.</summary>
    public (string Workflow, string Node) Second { get; } = second;

    /// <summary>True when one claim is a start and the other a wait; false when both are starts.</summary>
    public bool StartAndWait { get; } = startAndWait;

    private static string Describe(string method, string path, (string Workflow, string Node) first, (string Workflow, string Node) second, bool startAndWait) =>
        startAndWait
            ? $"Endpoint '{first.Node}' of '{first.Workflow}' and endpoint '{second.Node}' of '{second.Workflow}' are a start and a wait on {method} /{path}'s shape; a request reaches one of them. Change the method or path of one of them."
            : first.Workflow == second.Workflow
                ? $"Workflow '{first.Workflow}' starts on {method} /{path} at two endpoints, '{first.Node}' and '{second.Node}'; a route starts one workflow at one endpoint. Change the method or path of one of them."
                : $"Workflows '{first.Workflow}' and '{second.Workflow}' both start on {method} /{path}; a route starts one workflow. Change the method or path of one of them.";
}
