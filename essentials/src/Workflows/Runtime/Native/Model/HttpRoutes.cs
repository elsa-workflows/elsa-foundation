using Kernel.Graph.Exceptions;

namespace Kernel.Native;

/// <summary>
/// The in-memory route table, built from the published definitions: routing reads nothing. One method and path starts
/// one workflow: a second claim on it is refused with <see cref="HttpRouteConflictException"/> when the table is built,
/// as the old core refuses it at publish. A definition published again replaces its own routes (the registry keeps one
/// workflow per id), and one path may start different workflows on different methods.
/// </summary>
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
            // release a response after the commit of the turn that wrote it).
            foreach (var node in workflow.Nodes.Values.Where(n => n.Type == typeof(HttpEndpoint)))
                if (node.Inputs.GetValueOrDefault("Path") is LiteralBinding { Value: { } p } && node.Inputs.GetValueOrDefault("RespondBeforeCommit") is LiteralBinding { Value: { } early } && early.GetValue<bool>())
                    _early.Add(Key(Method(node), p.GetValue<string>()));
            // A start trigger is an HttpEndpoint with a literal path at the root, first in a root Sequence, or a child of a
            // root Flowchart (the designer's shape) that no connection leads to.
            foreach (var start in StartsOf(workflow.Root))
                if (start.Type == typeof(HttpEndpoint) && start.Inputs.GetValueOrDefault("Path") is LiteralBinding { Value: { } path })
                {
                    var (method, route) = (Method(start).ToUpperInvariant(), path.GetValue<string>().Trim('/'));
                    if (!_starts.TryAdd(Key(method, route), (workflow.Id, start.Id)))
                        throw new HttpRouteConflictException(method, route, _starts[Key(method, route)], (workflow.Id, start.Id));
                }
        }
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

    public bool TryStart(string method, string path, out (string Workflow, string Node) route) => _starts.TryGetValue(Key(method, path), out route);

    /// <summary>The route's caller is answered when the turn ends, not after its commit.</summary>
    public bool BeforeCommit(string method, string path) => _early.Contains(Key(method, path));
}

/// <summary>Two start endpoints claim one method and path: a route starts one workflow, so the route table refuses the second.</summary>
public sealed class HttpRouteConflictException(string method, string path, (string Workflow, string Node) first, (string Workflow, string Node) second)
    : InvalidOperationException(Describe(method, path, first, second))
{
    /// <summary>The method both claim, in upper case.</summary>
    public string Method { get; } = method;

    /// <summary>The path both claim, without its leading and trailing slashes.</summary>
    public string Path { get; } = path;

    /// <summary>The workflow and endpoint that claimed the route first.</summary>
    public (string Workflow, string Node) First { get; } = first;

    /// <summary>The workflow and endpoint whose claim was refused.</summary>
    public (string Workflow, string Node) Second { get; } = second;

    private static string Describe(string method, string path, (string Workflow, string Node) first, (string Workflow, string Node) second) =>
        first.Workflow == second.Workflow
            ? $"Workflow '{first.Workflow}' starts on {method} /{path} at two endpoints, '{first.Node}' and '{second.Node}'; a route starts one workflow at one endpoint."
            : $"Workflows '{first.Workflow}' and '{second.Workflow}' both start on {method} /{path}; a route starts one workflow. Change the method or path of one of them.";
}
