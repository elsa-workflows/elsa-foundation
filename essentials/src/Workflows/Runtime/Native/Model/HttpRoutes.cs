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
            // A start trigger is an HttpEndpoint at the root or first in a root sequence, with a literal path.
            var first = workflow.Root.Type == typeof(Sequence) && workflow.Root.Children.Count > 0 ? workflow.Root.Children[0] : workflow.Root;
            if (first.Type != typeof(HttpEndpoint) || first.Inputs.GetValueOrDefault("Path") is not LiteralBinding { Value: { } path }) continue;
            _starts[Key(Method(first), path.GetValue<string>())] = (workflow.Id, first.Id);
        }
    }

    private static string Method(Node node) => node.Inputs.GetValueOrDefault("Method") is LiteralBinding { Value: { } m } ? m.GetValue<string>() : "POST";

    public bool TryStart(string method, string path, out (string Workflow, string Node) route) => _starts.TryGetValue(Key(method, path), out route);

    /// <summary>The route's caller is answered when the turn ends, not after its commit.</summary>
    public bool BeforeCommit(string method, string path) => _early.Contains(Key(method, path));
}
