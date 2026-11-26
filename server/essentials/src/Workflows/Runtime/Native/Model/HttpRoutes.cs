using Kernel.Graph.Exceptions;

namespace Kernel.Native;

/// <summary>
/// The in-memory route table, built from the published definitions: routing reads nothing. A route is its method in
/// upper case and its path as a <see cref="HttpRouteTemplate"/>: trimmed of white space and slashes, literals and
/// parameter names in lower case, as the old core canonicalizes a template.
/// <list type="bullet">
/// <item>A start route is an HttpEndpoint with a literal path where a run begins (<see cref="StartsOf"/>) that may start one
/// (<see cref="NativeStartTriggers"/>). A wait route is
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
    /// The subscription key of a literal method and path: <c>http:{METHOD}:{path}</c>, with the path trimmed and in lower
    /// case, or <c>http:{METHOD}:#{sha-256 of the path}</c> past <see cref="MaxReadableKeyBytes"/>. A readable key whose
    /// path is '#' and a hash equals that hashed key; reaching it takes knowing the long path's hash.
    /// </summary>
    public static string Key(string method, string path) => Readable(method, Normalize(path));

    /// <summary>
    /// The key of a path a route template with parameters resolved (<see cref="HttpRouteTemplate.Resolve"/>):
    /// <c>http:{METHOD}={path}</c>. Its literals are already in lower case and its values keep their case, so that ids
    /// that differ only in case are two keys; and it has its own separator, which no method contains, so that no literal
    /// key (a computed wait's, or an edge's literal retry) can meet it.
    /// </summary>
    public static string ResolvedKey(string method, string path) => Readable(method, '=', path.Trim().Trim('/'));

    /// <summary>
    /// The key a wait route subscribes on for these route values, and a matched request is delivered to: the resolved key
    /// for a template with parameters, the literal key for one without.
    /// </summary>
    public static string WaitKey(string method, HttpRouteTemplate template, IReadOnlyDictionary<string, string>? values) =>
        template.Segments.Any(segment => segment.Kind != HttpRouteSegmentKind.Literal)
            ? ResolvedKey(method, template.Resolve(values))
            : Key(method, template.Text);

    private static string Readable(string method, string path) => Readable(method, ':', path);

    private static string Readable(string method, char separator, string path)
    {
        var verb = method.ToUpperInvariant();
        var key = $"http:{verb}{separator}{path}";
        return System.Text.Encoding.UTF8.GetByteCount(key) <= MaxReadableKeyBytes
            ? key
            : $"http:{verb}{separator}#{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))}";
    }

    private static string Normalize(string path) => path.Trim().Trim('/').ToLowerInvariant();

    /// <summary>The start and wait routes with a literal path, most specific first.</summary>
    public IReadOnlyList<HttpRoute> Routes => _routes;

    public HttpRoutes(WorkflowRegistry registry)
    {
        var shapes = new Dictionary<string, HttpRoute>(StringComparer.Ordinal);
        var definitions = registry.All.ToDictionary(workflow => workflow.Id, workflow => workflow.Definition, StringComparer.Ordinal);
        foreach (var workflow in registry.All)
        {
            // An endpoint that may not start a run is a wait wherever it is (NativeStartTriggers).
            var starts = StartsOf(workflow.Root).Where(NativeStartTriggers.CanStart).ToHashSet();
            foreach (var node in workflow.Nodes.Values.Where(n => n.Type == typeof(HttpEndpoint)))
            {
                if (node.Inputs.GetValueOrDefault("Path") is not LiteralBinding { Value: { } literal })
                {
                    // A path an expression computes is a literal route known only at run time: the edge cannot match a
                    // template it never sees, so it takes no route values.
                    if (BindsRouteValues(node))
                        throw new HttpRouteTemplateException($"Definition '{workflow.Definition}', endpoint '{node.Id}': RouteValues need a literal route template, and this path is computed.");
                    continue;
                }
                HttpRouteTemplate template;
                try { template = HttpRouteTemplate.Parse(literal.GetValue<string>()); }
                catch (HttpRouteTemplateException refused) { throw new HttpRouteTemplateException($"Definition '{workflow.Definition}', endpoint '{node.Id}': {refused.Message}"); }
                var start = starts.Contains(node);
                if (!start && !template.FullyBound)
                    throw new HttpRouteTemplateException($"Definition '{workflow.Definition}', endpoint '{node.Id}': the wait's route '{template.Text}' has an optional or catch-all parameter; a wait's route is fully bound.");
                if (!start && template.Segments.Any(segment => segment.Kind != HttpRouteSegmentKind.Literal) && !BindsRouteValues(node))
                    throw new HttpRouteTemplateException($"Definition '{workflow.Definition}', endpoint '{node.Id}': the wait's route '{template.Text}' has parameters and no RouteValues to resolve them.");
                // Its opt-out of the release rule (the owner's default of 12 November is to release a response after the
                // commit of the turn that wrote it).
                var early = node.Inputs.GetValueOrDefault("RespondBeforeCommit") is LiteralBinding { Value: { } flag } && flag.GetValue<bool>();
                var route = new HttpRoute(Method(node).ToUpperInvariant(), template, workflow.Id, node.Id, start, early);
                // Two waits on one shape are fine when they read alike, since each resolves to its own key: the edge matches
                // the shape once, so their parameter names and release rule must agree. Anything else on one shape is refused.
                if (shapes.TryGetValue($"{route.Method} {template.Shape}", out var claimed) &&
                    (claimed.Start || start || claimed.Template.Text != template.Text || claimed.RespondBeforeCommit != early))
                    throw new HttpRouteConflictException(route.Method, template.Text, (claimed.Workflow, claimed.Node), (workflow.Id, node.Id),
                        claimed.Start && start ? HttpRouteConflict.TwoStarts : claimed.Start || start ? HttpRouteConflict.StartAndWait : HttpRouteConflict.TwoWaits)
                    { Definitions = (definitions[claimed.Workflow], workflow.Definition) };
                // Waits of different shapes can still match one path (orders/special/cancel and orders/{id}/cancel): the edge
                // takes the more specific, so they must read alike too.
                if (!start && _routes.FirstOrDefault(other => !other.Start && other.Method == route.Method && Overlap(other.Template, template) &&
                        (other.Template.Text != template.Text || other.RespondBeforeCommit != early)) is { } overlapping)
                    throw new HttpRouteConflictException(route.Method, template.Text, (overlapping.Workflow, overlapping.Node), (workflow.Id, node.Id), HttpRouteConflict.TwoWaits)
                    { Definitions = (definitions[overlapping.Workflow], workflow.Definition) };
                if (start || !shapes.ContainsKey($"{route.Method} {template.Shape}")) shapes[$"{route.Method} {template.Shape}"] = route;
                _routes.Add(route);
                if (start) _starts[Key(route.Method, template.Text)] = (workflow.Id, node.Id);
                if (early) _early.Add(Key(route.Method, template.Text));
            }
        }
        _routes.Sort((a, b) => HttpRouteTemplate.Compare(a.Template, b.Template));
    }

    // A RouteValues input counts as bound whatever it holds, a literal null too, as HttpEndpoint counts it.
    private static bool BindsRouteValues(Node node) => node.Inputs.ContainsKey("RouteValues");

    // Two fully bound templates match one path when they have as many segments and no position holds two different literals.
    private static bool Overlap(HttpRouteTemplate a, HttpRouteTemplate b) =>
        a.Segments.Count == b.Segments.Count && a.Segments.Zip(b.Segments).All(pair =>
            pair.First.Kind != HttpRouteSegmentKind.Literal || pair.Second.Kind != HttpRouteSegmentKind.Literal || pair.First.Text == pair.Second.Text);

    // The nodes a run of the root can begin at. In a Flowchart, a run triggered at a child begins there (the graph
    // engine's SelectEntryNode), so every child without an inbound connection is an entry, as the old core's start-path
    // rule has it. A child a connection leads to is not one, even when the engine falls back to it as the start node: a
    // loop that comes back to it would wait there on the start route's own key, and no request could reach that wait.
    // A graph the engine refuses has no entry; its run faults on its own.
    /// <summary>Where a run begins: the root, the first child of a root Sequence, or an entry of a root Flowchart (<see cref="Flowchart.IsEntry"/>).</summary>
    internal static IEnumerable<Node> StartsOf(Node root)
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

/// <summary>What two claims on one method and route shape are.</summary>
public enum HttpRouteConflict
{
    /// <summary>Two starts: a route starts one workflow at one endpoint.</summary>
    TwoStarts,
    /// <summary>A start and a wait: a request reaches one of them.</summary>
    StartAndWait,
    /// <summary>Two waits that name their parameters differently or differ in their release rule.</summary>
    TwoWaits,
}

/// <summary>
/// Two endpoints claim one method and route shape in a way a request cannot tell apart (<see cref="HttpRouteConflict"/>),
/// so the route table refuses the second claim.
/// </summary>
public sealed class HttpRouteConflictException(string method, string path, (string Workflow, string Node) first, (string Workflow, string Node) second,
    HttpRouteConflict kind = HttpRouteConflict.TwoStarts)
    : InvalidOperationException
{
    /// <summary>
    /// The definitions of <see cref="First"/> and <see cref="Second"/>, which the message names: a published version's
    /// definition, or the workflow itself for one built in code. Unset, the message names the workflows.
    /// </summary>
    public (string First, string Second)? Definitions { get; init; }

    public override string Message => Describe(Method, Path, Definitions is { } named ? (named.First, First.Node) : First,
        Definitions is { } again ? (again.Second, Second.Node) : Second, Kind);

    /// <summary>The method both claim, in upper case.</summary>
    public string Method { get; } = method;

    /// <summary>The second claim's normalized template, for example <c>orders/{id}</c>.</summary>
    public string Path { get; } = path;

    /// <summary>The claim met first in the registry's order, which is not a publication order.</summary>
    public (string Workflow, string Node) First { get; } = first;

    /// <summary>The claim met second, at which the table was refused.</summary>
    public (string Workflow, string Node) Second { get; } = second;

    /// <summary>What the two claims are.</summary>
    public HttpRouteConflict Kind { get; } = kind;

    private static string Describe(string method, string path, (string Workflow, string Node) first, (string Workflow, string Node) second, HttpRouteConflict kind) =>
        kind == HttpRouteConflict.TwoWaits
            ? $"Endpoints '{first.Node}' of definition '{first.Workflow}' and '{second.Node}' of definition '{second.Workflow}' wait on {method} /{path}'s shape with other parameter names or another release rule; a request matches the shape once. Name the parameters alike or change one route."
            : kind == HttpRouteConflict.StartAndWait
            ? $"Endpoint '{first.Node}' of definition '{first.Workflow}' and endpoint '{second.Node}' of definition '{second.Workflow}' are a start and a wait on {method} /{path}'s shape; a request reaches one of them. Change the method or path of one of them."
            : first.Workflow == second.Workflow
                ? $"Definition '{first.Workflow}' starts on {method} /{path} at two endpoints, '{first.Node}' and '{second.Node}'; a route starts one workflow at one endpoint. Change the method or path of one of them."
                : $"Definitions '{first.Workflow}' and '{second.Workflow}' both start on {method} /{path}; a route starts one workflow. Change the method or path of one of them.";
}
