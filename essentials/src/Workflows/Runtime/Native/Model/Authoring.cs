using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kernel.Native;

// The authoring surface, ported from Elsa 4 (see NATIVE.md, section 1). A leaf returns a closed transition; a stateful
// activity suspends with state and triggers and resumes on a fresh object; a composite schedules children and gets
// their callbacks. Inputs are properties bound by the definition; the output is one typed result. Activities never see
// the kernel, a store or another instance: what they want from the world is a trigger or an effect in their transition.

// ---- inputs --------------------------------------------------------------------------------------------------------

/// <summary>Where an input's value comes from.</summary>
public abstract record Binding
{
    public static Binding Literal(object? value) => new LiteralBinding(value as JsonNode ?? JsonSerializer.SerializeToNode(value));
    public static Binding Var(string name) => new VariableBinding(name);
    public static Binding Result(string node, string? path = null) => new ResultBinding(node, path);
    public static Binding Expr(string text) => new ExpressionBinding(text);
    public static implicit operator Binding(string literal) => Literal(literal);
    public static implicit operator Binding(int literal) => Literal(literal);
    public static implicit operator Binding(long literal) => Literal(literal);
    public static implicit operator Binding(bool literal) => Literal(literal);
}

public sealed record LiteralBinding(JsonNode? Value) : Binding;
public sealed record VariableBinding(string Name) : Binding;
public sealed record ResultBinding(string Node, string? Path) : Binding;
public sealed record ExpressionBinding(string Text) : Binding;

// ---- the graph -----------------------------------------------------------------------------------------------------

/// <summary>
/// What a flowchart node is besides its activity: main's gateway kinds (the graph engine's policy kinds). A node with
/// none routes on its outcome, and a node with several inbound connections is an implicit join, decided by the
/// dead-path join (main's default). The join modes are explicit: ParallelJoin and InclusiveJoin wait for every inbound
/// connection, live or dead; Merge never waits; FirstWins races its outbound branches and cancels the losers.
/// </summary>
public enum Gateway { Decision, ParallelFork, ParallelJoin, InclusiveFork, InclusiveJoin, FirstWins, Merge }

/// <summary>
/// One case of a Switch: its label, which is also the outcome the Switch completes with, and its condition. A case
/// without a condition matches when the Switch's <c>Value</c> equals its label (ordinal), as the old core's retained cases do.
/// </summary>
public sealed record SwitchCase(string Label, Binding? Condition = null);

/// <summary>A flowchart connection from a source's outcome to a target.</summary>
public sealed record Connection(string Source, string Target, string Outcome = "Done");

/// <summary>One node of a definition: an activity type, its input bindings, its children and (for a flowchart) its connections.</summary>
public sealed class Node(string id, Type type)
{
    public string Id { get; } = id;
    public Type Type { get; } = type;
    public Dictionary<string, Binding> Inputs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Node> Children { get; } = [];
    public List<Connection> Connections { get; } = [];
    /// <summary>For a flowchart: the gateway kind of a child, by its id.</summary>
    public Dictionary<string, Gateway> Gateways { get; } = new(StringComparer.Ordinal);
    /// <summary>For a Switch: its cases, in the order they are tried. A case's branch is the child in its slot (<see cref="Native.Switch.CaseSlot"/>).</summary>
    public List<SwitchCase> Cases { get; } = [];
    /// <summary>Output captures: variable name to a path in the result ("" is the whole result).</summary>
    public Dictionary<string, string> Captures { get; } = new(StringComparer.Ordinal);
    /// <summary>The named child slot this node fills in its parent, where the parent has slots (If: Then, Else).</summary>
    public string? Slot { get; set; }
    /// <summary>The time allowed for a background activity's job.</summary>
    public TimeSpan? JobTimeout { get; init; }
    internal bool External, Background;
}

public sealed class Workflow
{
    public Workflow(string id, Node root, IReadOnlyDictionary<string, JsonNode?>? variables = null)
    {
        (Id, Root, Variables) = (id, root, variables ?? new Dictionary<string, JsonNode?>());
        void Index(Node node, Node? parent)
        {
            if (!Nodes.TryAdd(node.Id, node)) throw new InvalidOperationException($"Node id '{node.Id}' is used twice in '{id}'.");
            node.External = node.Type.IsDefined(typeof(ExternalAttribute));
            node.Background = node.Type.IsDefined(typeof(BackgroundAttribute));
            if (node.JobTimeout is { } timeout && (!node.Background || timeout < TimeSpan.Zero))
                throw new ArgumentException($"Node '{node.Id}' requires a nonnegative timeout on a background activity.");
            if (node.Type == typeof(Switch)) Switch.Validate(node, id);
            else if (node.Cases.Count != 0) throw new InvalidOperationException($"Node '{node.Id}' in '{id}' has cases and is no Switch.");
            if (parent is not null) Parents[node.Id] = parent;
            foreach (var child in node.Children) Index(child, node);
        }
        Index(root, null);
    }

    // Private M0 experiment; this request is not a product incident-strategy setting.
    internal bool InterventionRequested { get; set; }

    public string Id { get; }
    public Node Root { get; }
    public IReadOnlyDictionary<string, JsonNode?> Variables { get; }
    public Dictionary<string, Node> Nodes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Node> Parents { get; } = new(StringComparer.Ordinal);
}

public sealed class WorkflowRegistry
{
    private readonly Dictionary<string, Workflow> _workflows = new(StringComparer.Ordinal);
    public WorkflowRegistry Add(Workflow workflow) { _workflows[workflow.Id] = workflow; return this; }
    public Workflow Get(string id) => _workflows.TryGetValue(id, out var w) ? w : throw new InvalidOperationException($"No workflow '{id}'.");
    internal bool TryGet(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Workflow? workflow) =>
        _workflows.TryGetValue(id, out workflow);
    public bool Remove(string id) => _workflows.Remove(id);
    public IEnumerable<Workflow> All => _workflows.Values;
}

public sealed record ChildCompleted(string Frame, string Node, string Outcome, JsonNode? Result, string? Tag);
public sealed record ChildFaulted(string Frame, string Node, ActivityFault Fault, string? Tag);

/// <summary>A structural activity: it schedules children and decides on their callbacks. A fresh object per callback.</summary>
public abstract class CompositeActivity : ActivityBase
{
    protected internal abstract void Execute(CompositeContext context);
    protected internal abstract void OnChildCompleted(CompositeContext context, ChildCompleted child);
    protected internal virtual void OnChildFaulted(CompositeContext context, ChildFaulted child) => context.Fault(child.Fault);

    /// <summary>
    /// Which activation of a child this is, for the effect key's iteration path (round 5 C): null when this composite
    /// runs each child once, else a number or name from this composite's committed state (a ForEach index, a While
    /// iteration, a Flowchart round and token, a StateMachine visit) that two activations of one child never share and
    /// that a replay cannot reorder. Called on a bare instance (no inputs) with the parent frame's state and the child's
    /// scheduling tag, iteration scope and lexical scope.
    /// </summary>
    protected internal virtual string? IterationOf(JsonNode? state, string? childTag, IReadOnlyDictionary<string, JsonNode?>? childScope, string? childLexicalScope) => null;
}

internal static class Hydrator
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Inputs = new();

    public static PropertyInfo[] Of(Type type) => Inputs.GetOrAdd(type, t => t.GetProperties().Where(p => p.IsDefined(typeof(InputAttribute))).ToArray());

    public static ActivityBase Create(Node node, Func<Binding, JsonNode?> evaluate)
    {
        var activity = (ActivityBase)Activator.CreateInstance(node.Type)!;
        foreach (var property in Of(node.Type))
            if (node.Inputs.TryGetValue(property.Name, out var binding))
                property.SetValue(activity, Values.To(evaluate(binding), property.PropertyType));
        return activity;
    }

    /// <summary>The materialized inputs of a leaf, for a background job that cannot read variables.</summary>
    public static JsonObject Snapshot(Node node, Func<Binding, JsonNode?> evaluate)
    {
        var snapshot = new JsonObject();
        foreach (var property in Of(node.Type))
            if (node.Inputs.TryGetValue(property.Name, out var binding)) snapshot[property.Name] = evaluate(binding)?.DeepClone();
        return snapshot;
    }

    public static ActivityBase Create(Node node, JsonObject snapshot)
    {
        var activity = (ActivityBase)Activator.CreateInstance(node.Type)!;
        foreach (var property in Of(node.Type))
            if (snapshot.TryGetPropertyValue(property.Name, out var value)) property.SetValue(activity, Values.To(value, property.PropertyType));
        return activity;
    }
}
