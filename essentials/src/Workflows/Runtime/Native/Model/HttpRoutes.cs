using Kernel.Graph.Exceptions;

namespace Kernel.Native;

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
            // release a response after the commit of the turn that wrote it).
            foreach (var node in workflow.Nodes.Values.Where(n => n.Type == typeof(HttpEndpoint)))
                if (node.Inputs.GetValueOrDefault("Path") is LiteralBinding { Value: { } p } && node.Inputs.GetValueOrDefault("RespondBeforeCommit") is LiteralBinding { Value: { } early } && early.GetValue<bool>())
                    _early.Add(Key(Method(node), p.GetValue<string>()));
            // A start trigger is an HttpEndpoint with a literal path at the root, first in a root Sequence, or a child of a
            // root Flowchart (the designer's shape) that no connection leads to.
            foreach (var start in StartsOf(workflow.Root))
                if (start.Type == typeof(HttpEndpoint) && start.Inputs.GetValueOrDefault("Path") is LiteralBinding { Value: { } path })
                    _starts[Key(Method(start), path.GetValue<string>())] = (workflow.Id, start.Id);
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
        var targets = root.Connections.Select(connection => connection.Target).ToHashSet(StringComparer.Ordinal);
        return root.Children.Where(child => !targets.Contains(child.Id));
    }

    private static string Method(Node node) => node.Inputs.GetValueOrDefault("Method") is LiteralBinding { Value: { } m } ? m.GetValue<string>() : "POST";

    public bool TryStart(string method, string path, out (string Workflow, string Node) route) => _starts.TryGetValue(Key(method, path), out route);

    /// <summary>The route's caller is answered when the turn ends, not after its commit.</summary>
    public bool BeforeCommit(string method, string path) => _early.Contains(Key(method, path));
}
