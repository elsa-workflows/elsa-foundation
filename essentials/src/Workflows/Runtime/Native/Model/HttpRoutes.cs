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
            // A start trigger is an HttpEndpoint with a literal path at the root, first in a root Sequence, or the start
            // node of a root Flowchart (the designer's shape).
            var first = StartOf(workflow.Root);
            if (first.Type != typeof(HttpEndpoint) || first.Inputs.GetValueOrDefault("Path") is not LiteralBinding { Value: { } path }) continue;
            _starts[Key(Method(first), path.GetValue<string>())] = (workflow.Id, first.Id);
        }
    }

    // The node a run of the root begins at. A Flowchart's is the graph engine's start node (the first child without an
    // inbound connection, else the first child); a graph the engine refuses has none, and its run faults on its own.
    private static Node StartOf(Node root)
    {
        if (root.Children.Count == 0) return root;
        if (root.Type == typeof(Sequence)) return root.Children[0];
        if (root.Type != typeof(Flowchart)) return root;
        try
        {
            return Flowchart.GraphOf(root).SelectStartNode() is { } start ? root.Children.First(child => child.Id == start.NodeId) : root;
        }
        catch (FlowchartExecutionException) { return root; }
    }

    private static string Method(Node node) => node.Inputs.GetValueOrDefault("Method") is LiteralBinding { Value: { } m } ? m.GetValue<string>() : "POST";

    public bool TryStart(string method, string path, out (string Workflow, string Node) route) => _starts.TryGetValue(Key(method, path), out route);

    /// <summary>The route's caller is answered when the turn ends, not after its commit.</summary>
    public bool BeforeCommit(string method, string path) => _early.Contains(Key(method, path));
}
