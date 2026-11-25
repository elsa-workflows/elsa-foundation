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
    public static Binding Var(string name, string? path = null) => new VariableBinding(name, path);
    /// <summary>An expression the host evaluates (<see cref="HostExpressionBinding"/>).</summary>
    public static Binding Host(string language, string text, params (string Name, Binding Value)[] parameters) =>
        new HostExpressionBinding(language, text, [.. parameters.Select(p => new HostParameter(p.Name, p.Value))]);
    /// <summary>The instance's id or correlation id (<see cref="InstanceBinding"/>).</summary>
    public static Binding Instance(InstanceField field) => new InstanceBinding(field);
    public static Binding Result(string node, string? path = null) => new ResultBinding(node, path);
    public static Binding Expr(string text) => new ExpressionBinding(text);
    public static implicit operator Binding(string literal) => Literal(literal);
    public static implicit operator Binding(int literal) => Literal(literal);
    public static implicit operator Binding(long literal) => Literal(literal);
    public static implicit operator Binding(bool literal) => Literal(literal);
}

public sealed record LiteralBinding(JsonNode? Value) : Binding;
/// <summary>A variable, or a member of it by a dotted path (a workflow input's member is the variable <c>input</c>'s).</summary>
public sealed record VariableBinding(string Name, string? Path = null) : Binding;

/// <summary>A fact of the running instance.</summary>
public sealed record InstanceBinding(InstanceField Field) : Binding;

/// <summary>
/// The instance's id, or the correlation id Set Correlation Id last set (null until then). A correlation id given when the
/// instance starts is K10's start path's to carry.
/// </summary>
public enum InstanceField { Id, CorrelationId }

/// <summary>
/// An expression the host evaluates (owner decision of 25 November: JavaScript and Liquid, through the host's portable
/// evaluator): its language, text, result type and options, and its parameters, each a binding resolved where the node
/// runs. The host sees the parameters and the variables visible there by name.
/// </summary>
public sealed record HostExpressionBinding(string Language, string Text, IReadOnlyList<HostParameter> Parameters, string? ResultType = null, JsonObject? Options = null) : Binding;

public sealed record HostParameter(string Name, Binding Value);
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

/// <summary>
/// What a GraphActivity declares, as the old core's pinned descriptor does for a placed reusable activity: its inputs,
/// its local variables, its outputs and the outcomes it completes with. The body is the boundary's one child.
/// </summary>
public sealed record GraphBoundary
{
    public IReadOnlyList<GraphInput> Inputs { get; init; } = [];
    public IReadOnlyList<GraphVariable> Variables { get; init; } = [];
    public IReadOnlyList<GraphOutput> Outputs { get; init; } = [];

    /// <summary>The body's outcome to the boundary's, when the body ends by itself. Empty: it ends on Done.</summary>
    public IReadOnlyDictionary<string, string> OutcomeMappings { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>A Complete node's reference key to the boundary's outcome.</summary>
    public IReadOnlyDictionary<string, string> CompleteOutcomes { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>An input of a GraphActivity, bound on its node under its name; unbound, it takes its default, else it is null or, when required, refused.</summary>
public sealed record GraphInput(string Name, bool Required = false, Binding? Default = null);

/// <summary>A local variable of a GraphActivity's body, initialized where its inputs are visible.</summary>
public sealed record GraphVariable(string Name, Binding? Value = null);

/// <summary>
/// An output of a GraphActivity: its mapping, read where its inputs and variables are visible when it completes, or, with
/// none, what a Set Output in its body assigns it. A required one with neither faults the boundary, as on the old core.
/// </summary>
public sealed record GraphOutput(string Name, Binding? Value = null, bool Required = false);

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
    /// <summary>For a GraphActivity: what it declares.</summary>
    public GraphBoundary? Graph { get; set; }
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
    /// <param name="definition">The definition this workflow is a version of (<see cref="Definition"/>); the workflow itself when null.</param>
    public Workflow(string id, Node root, IReadOnlyDictionary<string, JsonNode?>? variables = null, string? definition = null)
    {
        if (definition is not null && string.IsNullOrWhiteSpace(definition)) throw new ArgumentException("A definition cannot be blank.", nameof(definition));
        (Id, Root, Variables, Definition) = (id, root, variables ?? new Dictionary<string, JsonNode?>(), definition ?? id);
        void Index(Node node, Node? parent)
        {
            if (!Nodes.TryAdd(node.Id, node)) throw new InvalidOperationException($"Node id '{node.Id}' is used twice in '{id}'.");
            node.External = node.Type.IsDefined(typeof(ExternalAttribute));
            node.Background = node.Type.IsDefined(typeof(BackgroundAttribute));
            if (node.JobTimeout is { } timeout && (!node.Background || timeout < TimeSpan.Zero))
                throw new ArgumentException($"Node '{node.Id}' requires a nonnegative timeout on a background activity.");
            if (node.Type == typeof(Switch)) Switch.Validate(node, id);
            else if (node.Cases.Count != 0) throw new InvalidOperationException($"Node '{node.Id}' in '{id}' has cases and is no Switch.");
            if (node.Type == typeof(GraphActivity)) GraphActivity.Validate(node, id);
            else if (node.Graph is not null) throw new InvalidOperationException($"Node '{node.Id}' in '{id}' declares a boundary and is no GraphActivity.");
            if (node.Type == typeof(Complete)) GraphActivity.ValidateExit(node, Boundary(parent), id);
            if (node.Type == typeof(SetOutput)) GraphActivity.ValidateOutput(node, Boundary(parent), id);
            if (node.Type == typeof(Finish) && !(node.Inputs.GetValueOrDefault("Outcome") is LiteralBinding { Value: JsonValue outcome } && outcome.TryGetValue<string>(out var named) && !string.IsNullOrWhiteSpace(named)))
                throw new InvalidOperationException($"Finish '{node.Id}' in '{id}' needs a literal outcome.");
            if (parent is not null) Parents[node.Id] = parent;
            foreach (var child in node.Children) Index(child, node);
        }
        Index(root, null);

        Node? Boundary(Node? node)
        {
            for (; node is not null; node = Parents.GetValueOrDefault(node.Id))
                if (node.Type == typeof(GraphActivity)) return node;
            return null;
        }
    }

    // Private M0 experiment; this request is not a product incident-strategy setting.
    internal bool InterventionRequested { get; set; }

    /// <summary>The workflow's id: on a publishing host, the published artifact's id, which an instance runs.</summary>
    public string Id { get; }

    /// <summary>
    /// The definition this workflow is a version of, which instance reads and schedules key on: the workflow itself unless
    /// set. Two artifacts of one definition share it.
    /// </summary>
    public string Definition { get; }

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
            {
                JsonNode? value;
                try { value = evaluate(binding); }
                catch (NativeExpressionException failed) when (failed.Input is null) { throw failed.For(property.Name); }
                property.SetValue(activity, Values.To(value, property.PropertyType));
            }
        return activity;
    }

    /// <summary>The materialized inputs of a leaf, for a background job that cannot read variables.</summary>
    public static JsonObject Snapshot(Node node, Func<Binding, JsonNode?> evaluate)
    {
        var snapshot = new JsonObject();
        foreach (var property in Of(node.Type))
            if (node.Inputs.TryGetValue(property.Name, out var binding))
            {
                try { snapshot[property.Name] = evaluate(binding)?.DeepClone(); }
                catch (NativeExpressionException failed) when (failed.Input is null) { throw failed.For(property.Name); }
            }
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
