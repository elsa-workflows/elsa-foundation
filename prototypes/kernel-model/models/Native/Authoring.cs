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

/// <summary>A bound input. Leaf inputs are materialized once per activation; composites are activated per callback.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InputAttribute : Attribute;

/// <summary>Elsa 4's background placement: the leaf runs as off-step work (the kernel's <c>Run</c>), not inside the step.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class BackgroundAttribute : Attribute;

/// <summary>Elsa 4's side-effect profile External: an attempt is not safe to replay. In Durable mode it is committed before it runs.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ExternalAttribute : Attribute;

// ---- the graph -----------------------------------------------------------------------------------------------------

public enum JoinMode { Any, All }

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
    public Dictionary<string, JoinMode> Joins { get; } = new(StringComparer.Ordinal);
    /// <summary>Output captures: variable name to a path in the result ("" is the whole result).</summary>
    public Dictionary<string, string> Captures { get; } = new(StringComparer.Ordinal);
    /// <summary>The named child slot this node fills in its parent, where the parent has slots (If: Then, Else).</summary>
    public string? Slot { get; set; }
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
            if (parent is not null) Parents[node.Id] = parent;
            foreach (var child in node.Children) Index(child, node);
        }
        Index(root, null);
    }

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
    public IEnumerable<Workflow> All => _workflows.Values;
}

// ---- transitions, triggers, effects --------------------------------------------------------------------------------

public enum TransitionKind { Complete, Suspend, Fault, Cancel }

public sealed record ActivityFault(string Code, string Message);

/// <summary>What a suspended activity waits for. The model turns each into a kernel registration.</summary>
public abstract record Trigger;
public sealed record EventTrigger(string Name) : Trigger;                                 // a subscription
public sealed record TimerTrigger(long Milliseconds) : Trigger;                           // a message to self with a due time
public sealed record ChildTrigger(string Workflow, JsonNode? Input, string Mode) : Trigger;  // a child start and its child-finished
public sealed record HttpTrigger(string Method, string Path) : Trigger;                   // a subscription on the route

/// <summary>What an activity does to the world besides its result. The model turns each into a kernel call.</summary>
public abstract record Effect;
public sealed record PublishEffect(string Event, JsonNode? Payload) : Effect;             // the kernel's publish
public sealed record DispatchEffect(string Workflow, JsonNode? Input, string Mode) : Effect; // a detached start
public sealed record EmitEffect(string Channel, JsonNode? Item) : Effect;                 // live, not durable: the kernel's Emit

/// <summary>The closed set of what an activation can end in. Not generic: the typed result is serialized by the helper.</summary>
public sealed record Transition(
    TransitionKind Kind, JsonNode? Result = null, string Outcome = "Done", ActivityFault? Fault = null, JsonNode? State = null,
    IReadOnlyList<Trigger>? Triggers = null, IReadOnlyList<Effect>? Effects = null)
{
    public Transition With(params Effect[] effects) => this with { Effects = [.. Effects ?? [], .. effects] };
}

public readonly record struct Unit;

// ---- contexts ------------------------------------------------------------------------------------------------------

public class ActivityContext
{
    internal ActivityContext(InstanceRef instance, string frame, string node, int attempt, CancellationToken cancellationToken)
        => (Instance, FrameId, NodeId, Attempt, CancellationToken) = (instance, frame, node, attempt, cancellationToken);

    public InstanceRef Instance { get; }
    public string FrameId { get; }
    public string NodeId { get; }
    /// <summary>The committed attempt of an External activity in Durable mode; 0 otherwise.</summary>
    public int Attempt { get; }
    public CancellationToken CancellationToken { get; }
}

public sealed class StartContext<TTrigger> : ActivityContext
{
    private readonly JsonNode? _trigger;
    internal StartContext(ActivityContext c, bool delivery, JsonNode? trigger) : base(c.Instance, c.FrameId, c.NodeId, c.Attempt, c.CancellationToken)
        => (IsTriggerDelivery, _trigger) = (delivery, trigger);

    /// <summary>This activation is the workflow's start trigger being delivered (an HTTP request, an event).</summary>
    public bool IsTriggerDelivery { get; }

    public bool TryGetTrigger(out TTrigger trigger)
    {
        trigger = IsTriggerDelivery ? Values.To<TTrigger>(_trigger)! : default!;
        return IsTriggerDelivery;
    }
}

public sealed class ResumeContext<TState, TTrigger> : ActivityContext
{
    internal ResumeContext(ActivityContext c, TState state, TTrigger trigger) : base(c.Instance, c.FrameId, c.NodeId, c.Attempt, c.CancellationToken)
        => (State, Trigger) = (state, trigger);

    public TState State { get; }
    public TTrigger Trigger { get; }
}

// ---- base types ----------------------------------------------------------------------------------------------------

public abstract class ActivityBase;

internal interface ILeaf
{
    ValueTask<Transition> ExecuteAsync(ActivityContext context);
}

internal interface IStateful
{
    ValueTask<Transition> StartAsync(ActivityContext context, bool delivery, JsonNode? trigger);
    ValueTask<Transition> ResumeAsync(ActivityContext context, JsonNode? state, JsonNode? trigger);
}

/// <summary>A variable write: an engine intrinsic, as in Elsa 4. The interpreter applies it; nothing executes.</summary>
internal interface IIntrinsic
{
    string Name { get; }
    JsonNode? Value { get; }
}

/// <summary>A leaf activity: one attempt, a closed transition.</summary>
public abstract class Activity<TResult> : ActivityBase, ILeaf
{
    protected abstract ValueTask<Transition> ExecuteAsync(ActivityContext context);
    ValueTask<Transition> ILeaf.ExecuteAsync(ActivityContext context) => ExecuteAsync(context);

    protected static Transition Complete(TResult result, string outcome = "Done") => new(TransitionKind.Complete, Values.From(result), outcome);
    protected static Transition Fault(string code, string message) => new(TransitionKind.Fault, Fault: new ActivityFault(code, message));
    protected static Transition Cancel(string reason) => new(TransitionKind.Cancel, Outcome: "Cancelled", Fault: new ActivityFault("cancelled", reason));
}

public abstract class Activity : Activity<Unit>
{
    protected static Transition Complete(string outcome = "Done") => new(TransitionKind.Complete, null, outcome);
}

/// <summary>An activity that suspends with state and triggers and resumes on a fresh activation.</summary>
public abstract class StatefulActivity<TResult, TState, TTrigger> : ActivityBase, IStateful
{
    protected abstract ValueTask<Transition> ExecuteAsync(StartContext<TTrigger> context);
    protected abstract ValueTask<Transition> ResumeAsync(ResumeContext<TState, TTrigger> context);

    protected static Transition Complete(TResult result, string outcome = "Done") => new(TransitionKind.Complete, Values.From(result), outcome);
    protected static Transition Suspend(TState state, params Trigger[] triggers) => new(TransitionKind.Suspend, State: Values.From(state), Triggers: triggers);
    protected static Transition Fault(string code, string message) => new(TransitionKind.Fault, Fault: new ActivityFault(code, message));

    ValueTask<Transition> IStateful.StartAsync(ActivityContext context, bool delivery, JsonNode? trigger) =>
        ExecuteAsync(new StartContext<TTrigger>(context, delivery, trigger));

    ValueTask<Transition> IStateful.ResumeAsync(ActivityContext context, JsonNode? state, JsonNode? trigger) =>
        ResumeAsync(new ResumeContext<TState, TTrigger>(context, Values.To<TState>(state)!, Values.To<TTrigger>(trigger)!));
}

public sealed record ChildCompleted(string Frame, string Node, string Outcome, JsonNode? Result, string? Tag);
public sealed record ChildFaulted(string Frame, string Node, ActivityFault Fault, string? Tag);

/// <summary>A structural activity: it schedules children and decides on their callbacks. A fresh object per callback.</summary>
public abstract class CompositeActivity : ActivityBase
{
    protected internal abstract void Execute(CompositeContext context);
    protected internal abstract void OnChildCompleted(CompositeContext context, ChildCompleted child);
    protected internal virtual void OnChildFaulted(CompositeContext context, ChildFaulted child) => context.Fault(child.Fault);
}

// ---- values --------------------------------------------------------------------------------------------------------

public static class Values
{
    public static JsonNode? From<T>(T value) => value switch
    {
        null => null,
        Unit => null,
        JsonNode node => node.DeepClone(),
        _ => JsonSerializer.SerializeToNode(value),
    };

    public static T? To<T>(JsonNode? node) => node switch
    {
        null => default,
        T same => same,
        _ => node.Deserialize<T>(),
    };

    public static object? To(JsonNode? node, Type type) => node is null ? null : type.IsInstanceOfType(node) ? node : node.Deserialize(type);

    public static JsonNode? Path(JsonNode? node, string? path)
    {
        if (string.IsNullOrEmpty(path)) return node;
        foreach (var part in path.Split('.'))
            node = node is JsonObject o && o.TryGetPropertyValue(part, out var next) ? next : null;
        return node;
    }

    public static bool Truthy(JsonNode? node) => node switch
    {
        null => false,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when TryNumber(v, out var d) => d != 0,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length > 0,
        _ => true,
    };

    /// <summary>A message payload as a value: JSON when it parses, else the text itself (a kernel payload is any string).</summary>
    public static JsonNode? Parse(string? payload)
    {
        if (payload is null) return null;
        try { return JsonNode.Parse(payload); }
        catch (JsonException) { return JsonValue.Create(payload); }
    }

    /// <summary>A JSON number whatever backs it: a parsed element, or a CLR long, int or double.</summary>
    public static bool TryNumber(JsonValue v, out double d)
    {
        if (v.TryGetValue(out d)) return true;
        if (v.TryGetValue<long>(out var l)) { d = l; return true; }
        if (v.TryGetValue<int>(out var i)) { d = i; return true; }
        if (v.TryGetValue<decimal>(out var m)) { d = (double)m; return true; }
        return false;
    }
}

/// <summary>Sets the bound inputs on a fresh activity object. Property lookups are cached per type.</summary>
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

    /// <summary>The materialized inputs of a leaf, for off-step work that cannot read variables.</summary>
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
