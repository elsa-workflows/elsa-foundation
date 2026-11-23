using System.Collections.Immutable;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa3.Mapping.Services;

/// <summary>
/// Mutation-only interpretation of a reusable script. It never produces source edits: loop back edges and local
/// helper calls can therefore be followed without rewriting the same source range twice. Values record ownership,
/// local container identity and local functions; merely reading or returning a workflow value is not a write.
/// </summary>
internal sealed class Elsa3ReusableVariableWriteAnalyzer(
    Func<Node, HashSet<string>?> scopeOf,
    Func<Elsa3AmbientReferenceKind, string, bool> isSessionVariable,
    Func<string, string?> generatedAccessor)
{
    private readonly record struct Binding(HashSet<string> Scope, string Name);
    private sealed record Value(bool Session, ImmutableHashSet<Node> Objects, ImmutableHashSet<Node> Functions)
    {
        public bool UnknownSpread { get; init; }
        public bool MayBeString { get; init; }
        public bool MayBePrimitive { get; init; }
        public static readonly Value Local = new(false, [], []);
        public static readonly Value Primitive = Local with { MayBePrimitive = true };
        public static readonly Value Owned = new(true, [], []) { MayBePrimitive = true };
        public static readonly Value Text = Local with { MayBeString = true };
        public static Value Object(Node node) => new(false, [node], []);
        public static Value Function(Node node) => new(false, [node], [node]);
        public Value Union(Value other) => new(Session || other.Session, Objects.Union(other.Objects), Functions.Union(other.Functions)) { UnknownSpread = UnknownSpread || other.UnknownSpread, MayBeString = MayBeString || other.MayBeString, MayBePrimitive = MayBePrimitive || other.MayBePrimitive };
        public bool Same(Value other) => Session == other.Session && UnknownSpread == other.UnknownSpread && MayBeString == other.MayBeString && MayBePrimitive == other.MayBePrimitive && Objects.SetEquals(other.Objects) && Functions.SetEquals(other.Functions);
    }

    private readonly record struct Attributes(bool Configurable, bool Enumerable, bool Writable)
    {
        public static readonly Attributes Ordinary = new(true, true, true);
        public Attributes Union(Attributes other) => new(Configurable && other.Configurable, Enumerable || other.Enumerable, Writable || other.Writable);
    }

    private sealed class State
    {
        public Dictionary<Binding, Value> Bindings { get; } = [];
        public Dictionary<(Node Object, string Member), Value> Members { get; } = [];
        public Dictionary<Node, ImmutableList<string>> PropertyOrder { get; } = new(ReferenceEqualityComparer.Instance);
        public HashSet<Node> Unordered { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<(Node Object, string Member), Value> Getters { get; } = [];
        public Dictionary<(Node Object, string Member), Value> Setters { get; } = [];
        public Dictionary<(Node Object, string Member), Attributes> Properties { get; } = [];
        public Dictionary<Node, Value> Prototypes { get; } = new(ReferenceEqualityComparer.Instance);
        public HashSet<(Node Object, string Member)> OptionalProperties { get; } = [];
        public State Copy()
        {
            var copy = new State();
            foreach (var item in Bindings) copy.Bindings[item.Key] = item.Value;
            foreach (var item in Members) copy.Members[item.Key] = item.Value;
            foreach (var item in PropertyOrder) copy.PropertyOrder[item.Key] = item.Value;
            copy.Unordered.UnionWith(Unordered);
            foreach (var item in Getters) copy.Getters[item.Key] = item.Value;
            foreach (var item in Setters) copy.Setters[item.Key] = item.Value;
            foreach (var item in Properties) copy.Properties[item.Key] = item.Value;
            foreach (var item in Prototypes) copy.Prototypes[item.Key] = item.Value;
            copy.OptionalProperties.UnionWith(OptionalProperties);
            return copy;
        }
        public void Join(State other)
        {
            foreach (var key in Properties.Keys.Except(other.Properties.Keys)) OptionalProperties.Add(key);
            foreach (var key in other.Properties.Keys.Except(Properties.Keys)) OptionalProperties.Add(key);
            OptionalProperties.UnionWith(other.OptionalProperties);
            foreach (var (key, value) in other.Bindings) Bindings[key] = Bindings.GetValueOrDefault(key, Value.Local).Union(value);
            foreach (var (key, value) in other.Members) Members[key] = Members.GetValueOrDefault(key, Value.Local).Union(value);
            foreach (var (key, order) in other.PropertyOrder)
            {
                if (PropertyOrder.TryGetValue(key, out var current))
                {
                    if (!current.SequenceEqual(order)) Unordered.Add(key);
                    PropertyOrder[key] = current.AddRange(order.Where(name => !current.Contains(name)));
                }
                else PropertyOrder[key] = order;
            }
            Unordered.UnionWith(other.Unordered);
            foreach (var (key, value) in other.Getters) Getters[key] = Getters.GetValueOrDefault(key, Value.Local).Union(value);
            foreach (var (key, value) in other.Setters) Setters[key] = Setters.GetValueOrDefault(key, Value.Local).Union(value);
            foreach (var (key, value) in other.Properties) Properties[key] = Properties.TryGetValue(key, out var current) ? current.Union(value) : value;
            foreach (var (key, value) in other.Prototypes) Prototypes[key] = Prototypes.GetValueOrDefault(key, Value.Local).Union(value);
        }
        public bool Same(State other) => Bindings.Count == other.Bindings.Count && Members.Count == other.Members.Count &&
            Bindings.All(item => other.Bindings.TryGetValue(item.Key, out var value) && item.Value.Same(value)) &&
            Members.All(item => other.Members.TryGetValue(item.Key, out var value) && item.Value.Same(value)) &&
            PropertyOrder.Count == other.PropertyOrder.Count && PropertyOrder.All(item => other.PropertyOrder.TryGetValue(item.Key, out var order) && item.Value.SequenceEqual(order)) &&
            Unordered.SetEquals(other.Unordered) && Getters.Count == other.Getters.Count && Setters.Count == other.Setters.Count && Properties.Count == other.Properties.Count && Prototypes.Count == other.Prototypes.Count &&
            Getters.All(item => other.Getters.TryGetValue(item.Key, out var value) && item.Value.Same(value)) &&
            Setters.All(item => other.Setters.TryGetValue(item.Key, out var value) && item.Value.Same(value)) &&
            Properties.All(item => other.Properties.TryGetValue(item.Key, out var value) && item.Value == value) &&
            Prototypes.All(item => other.Prototypes.TryGetValue(item.Key, out var value) && item.Value.Same(value)) && OptionalProperties.SetEquals(other.OptionalProperties);
    }

    private static State? Join(State? first, State? second)
    {
        if (first is null) return second?.Copy();
        var joined = first.Copy();
        if (second is not null) joined.Join(second);
        return joined;
    }
    private static ImmutableDictionary<string, State>? JoinLabels(ImmutableDictionary<string, State>? first, ImmutableDictionary<string, State>? second)
    {
        if (second is null) return first;
        var joined = first ?? ImmutableDictionary<string, State>.Empty;
        foreach (var (label, state) in second) joined = joined.SetItem(label, Join(joined.GetValueOrDefault(label), state)!);
        return joined;
    }
    private readonly record struct Flow(bool Normal, Value Returned, State? Returns = null, State? Breaks = null, State? Continues = null, State? Throws = null,
        ImmutableDictionary<string, State>? LabeledBreaks = null, ImmutableDictionary<string, State>? LabeledContinues = null)
    {
        public static readonly Flow Next = new(true, Value.Local);
        public Flow Union(Flow other) => new(Normal || other.Normal, Returned.Union(other.Returned),
            Join(Returns, other.Returns), Join(Breaks, other.Breaks), Join(Continues, other.Continues), Join(Throws, other.Throws),
            JoinLabels(LabeledBreaks, other.LabeledBreaks), JoinLabels(LabeledContinues, other.LabeledContinues));
    }
    private sealed record Closure(HashSet<string>[] Scopes, Dictionary<Binding, Value> Captured, Value This, Value Arguments, Value Super);
    private sealed record ClassInfo(ClassBody Body, Value Super, bool Derived);
    private sealed class ConstructorFrame(ClassInfo type, Value receiver)
    {
        public ClassInfo Type { get; } = type;
        public Value Receiver { get; set; } = receiver;
        public Value Result { get; set; } = Value.Local;
    }
    private readonly List<HashSet<string>> _scopes = [];
    private readonly Dictionary<Node, Closure> _closures = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> _calls = new(ReferenceEqualityComparer.Instance);
    private readonly List<State> _exceptionStates = [];
    private readonly List<Value> _receivers = [];
    private readonly List<Value> _arguments = [];
    private readonly HashSet<Node> _allocated = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> _summaryObjects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Value> _intrinsics = new(StringComparer.Ordinal);
    private readonly List<(string Name, Node Target)> _labels = [];
    private readonly Dictionary<Node, ClassInfo> _classes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> _stringifyingArrays = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> _arrays = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> _ordinaryObjects = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> _unprovenConstructions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Node, string> _primitiveBoxes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Node, Value> _homes = new(ReferenceEqualityComparer.Instance);
    private readonly List<Value> _supers = [];
    private readonly List<ConstructorFrame> _constructors = [];
    private readonly HashSet<Node> _constructingObjects = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> _writers = new(ReferenceEqualityComparer.Instance);
    private Node? _dynamicConstructor;
    private readonly Dictionary<Node, Node> _unknownMembers = new(ReferenceEqualityComparer.Instance);
    private State _state = new();
    private bool _writes;
    private int _opaqueDepth;
    private readonly List<State?> _optionalChains = [];

    public bool MayWrite(Script script)
    {
        // An unresolved constructor can compile arbitrary source through Function.prototype.constructor.
        // Its callable identity is conservative; acquiring it alone does not execute the source.
        _dynamicConstructor = script;
        _writers.Add(script);
        Execute(script);
        return _writes;
    }

    private Binding? Resolve(string name)
    {
        for (var index = _scopes.Count - 1; index >= 0; index--)
            if (_scopes[index].Contains(name)) return new Binding(_scopes[index], name);
        return null;
    }
    private Value Read(string name) => Resolve(name) is { } binding ? _state.Bindings.GetValueOrDefault(binding, Value.Local) : Value.Local;
    private bool Global(string name) => Resolve(name) is null;
    private static bool NativeNamespace(string name) => name is "JSON" or "Math" or "Object" or "Reflect" or "Array" or "Number" or "String" or "Boolean" or "Date" or "Intl" or "Temporal" or "RegExp";
    private Value Allocate(Node node)
    {
        // One allocation site can represent several live instances after another call or loop iteration.
        // Its fields then need weak updates, so a later instance cannot erase an earlier instance's ownership.
        if (!_allocated.Add(node)) { _summaryObjects.Add(node); _state.Unordered.Add(node); }
        return Value.Object(node);
    }
    private Value Intrinsic(Identifier name)
    {
        if (!_intrinsics.TryGetValue(name.Name, out var value)) _intrinsics[name.Name] = value = Value.Object(name);
        return value;
    }

    private Flow Execute(Node node) => Execute(node, null);

    private Flow Execute(Node node, IReadOnlyDictionary<string, Value>? initialValues)
    {
        if (_writes) return Flow.Next;
        // A definition is a value; its body runs only at a call site with that call's actual parameters.
        if (node is IFunction) return Flow.Next;
        var scope = scopeOf(node);
        if (scope is not null) EnterScope(node, scope, initialValues);
        try { return ExecuteCore(node); }
        finally
        {
            foreach (var exceptional in _exceptionStates) exceptional.Join(_state);
            if (scope is not null) _scopes.RemoveAt(_scopes.Count - 1);
        }
    }

    private void EnterScope(Node node, HashSet<string> scope, IReadOnlyDictionary<string, Value>? initialValues = null)
    {
        _scopes.Add(scope);
        foreach (var name in scope)
            _state.Bindings[new Binding(scope, name)] = initialValues is not null && initialValues.TryGetValue(name, out var value)
                ? value : Value.Primitive;
        IEnumerable<Node> statements = node switch
        {
            Script script => script.Body,
            BlockStatement block => block.Body,
            SwitchStatement choice => choice.Cases.SelectMany(item => item.Consequent),
            _ => []
        };
        foreach (var function in statements.OfType<FunctionDeclaration>())
            if (function.Id is { } name) Bind(name, FunctionValue(function));
    }

    private Flow Sequence(IEnumerable<Node> statements)
    {
        var result = Flow.Next;
        foreach (var statement in statements)
        {
            if (!result.Normal || _writes) break;
            var next = Execute(statement);
            result = result.Union(next) with { Normal = next.Normal };
        }
        return result;
    }

    private Flow ExecuteCore(Node node)
    {
        switch (node)
        {
            case Script script: return Sequence(script.Body);
            case BlockStatement block: return Sequence(block.Body);
            case ExpressionStatement expression: Evaluate(expression.Expression); return Flow.Next;
            case VariableDeclaration declaration:
                foreach (var item in declaration.Declarations)
                    if (item.Init is not null || declaration.Kind != VariableDeclarationKind.Var)
                        Bind(item.Id, Evaluate(item.Init));
                return Flow.Next;
            case ClassDeclaration type:
                ClassValue(type, type.Id, type.Body, type.SuperClass);
                return Flow.Next;
            case ReturnStatement returned:
                var returnedValue = Evaluate(returned.Argument);
                return new Flow(false, returnedValue, Returns: _state.Copy());
            case ThrowStatement thrown: Evaluate(thrown.Argument); return new Flow(false, Value.Local, Throws: _state.Copy());
            case BreakStatement broken:
                return broken.Label is { } breakLabel ? new Flow(false, Value.Local, LabeledBreaks: ImmutableDictionary<string, State>.Empty.Add(breakLabel.Name, _state.Copy()))
                    : new Flow(false, Value.Local, Breaks: _state.Copy());
            case ContinueStatement continued:
                return continued.Label is { } continueLabel ? new Flow(false, Value.Local, LabeledContinues: ImmutableDictionary<string, State>.Empty.Add(continueLabel.Name, _state.Copy()))
                    : new Flow(false, Value.Local, Continues: _state.Copy());
            case LabeledStatement labeled:
                var target = labeled.Body;
                while (target is LabeledStatement innerLabel) target = innerLabel.Body;
                _labels.Add((labeled.Label.Name, target));
                var labeledFlow = Execute(labeled.Body);
                _labels.RemoveAt(_labels.Count - 1);
                var labeledExit = labeledFlow.LabeledBreaks?.GetValueOrDefault(labeled.Label.Name);
                var labeledExits = Join(labeledFlow.Normal ? _state : null, labeledExit);
                if (labeledExits is not null) _state = labeledExits;
                return labeledFlow with { Normal = labeledExits is not null, LabeledBreaks = labeledFlow.LabeledBreaks?.Remove(labeled.Label.Name) };
            case IfStatement branch:
                Evaluate(branch.Test);
                return Branch(() => Execute(branch.Consequent), () => branch.Alternate is null ? Flow.Next : Execute(branch.Alternate));
            case ForStatement loop:
                if (loop.Init is VariableDeclaration init) Execute(init); else Evaluate(loop.Init);
                return Loop(loop, () => Evaluate(loop.Test), () => Execute(loop.Body), () => Evaluate(loop.Update));
            case WhileStatement loop: return Loop(loop, () => Evaluate(loop.Test), () => Execute(loop.Body), () => Value.Local);
            case DoWhileStatement loop: return Loop(loop, () => Value.Local, () => Execute(loop.Body), () => Evaluate(loop.Test), atLeastOnce: true);
            case ForOfStatement loop:
                var values = Iterated(Evaluate(loop.Right), out var iterator);
                return Loop(loop, () => Value.Local, () => { BindLoop(loop.Left, values); return CloseLoop(Execute(loop.Body), iterator, loop); }, () => Value.Local);
            case ForInStatement loop:
                Evaluate(loop.Right);
                return Loop(loop, () => Value.Local, () => { BindLoop(loop.Left, Value.Local); return Execute(loop.Body); }, () => Value.Local);
            case SwitchStatement choice:
                Evaluate(choice.Discriminant);
                var caseScope = choice.Cases.Count > 0 ? scopeOf(choice.Cases[0]) : null;
                if (caseScope is not null) EnterScope(choice, caseScope);
                try
                {
                    var entry = _state.Copy();
                    State? exits = choice.Cases.Any(item => item.Test is null) ? null : entry.Copy();
                    var flow = new Flow(false, Value.Local);
                    for (var index = 0; index < choice.Cases.Count; index++)
                    {
                        _state = entry.Copy();
                        Evaluate(choice.Cases[index].Test);
                        var path = Sequence(choice.Cases.Skip(index).SelectMany(item => item.Consequent));
                        exits = Join(exits, path.Normal ? _state : null);
                        exits = Join(exits, path.Breaks);
                        flow = flow.Union(path with { Normal = path.Normal || path.Breaks is not null, Breaks = null });
                    }
                    _state = exits ?? entry;
                    return flow with { Normal = exits is not null };
                }
                finally
                {
                    if (caseScope is not null) _scopes.RemoveAt(_scopes.Count - 1);
                }
            case TryStatement attempt:
                var exceptions = _state.Copy();
                _exceptionStates.Add(exceptions);
                var tried = Execute(attempt.Block);
                _exceptionStates.RemoveAt(_exceptionStates.Count - 1);
                var completed = tried.Normal ? _state.Copy() : null;
                if (attempt.Handler is { } handler)
                {
                    _state = exceptions;
                    var caught = Execute(handler);
                    completed = Join(completed, caught.Normal ? _state : null);
                    tried = (tried with { Throws = null }).Union(caught);
                }
                _state = completed ?? exceptions;
                if (attempt.Finalizer is { } finalizer)
                    return Finally(tried, completed, finalizer);
                return tried;
            case CatchClause caught:
                if (caught.Param is { } parameter) Bind(parameter, Value.Local);
                return Execute(caught.Body);
            default:
                Evaluate(node);
                return Flow.Next;
        }
    }

    private Flow Finally(Flow incoming, State? normal, Node finalizer)
    {
        var result = new Flow(false, Value.Local);
        State? normalExit = null;
        Run(normal, 0); Run(incoming.Returns, 1); Run(incoming.Breaks, 2); Run(incoming.Continues, 3); Run(incoming.Throws, 4);
        foreach (var (label, state) in incoming.LabeledBreaks ?? ImmutableDictionary<string, State>.Empty) Run(state, 5, label);
        foreach (var (label, state) in incoming.LabeledContinues ?? ImmutableDictionary<string, State>.Empty) Run(state, 6, label);
        _state = normalExit ?? normal ?? _state;
        return result;

        void Run(State? state, int kind, string? label = null)
        {
            if (state is null || _writes) return;
            _state = state.Copy();
            var completion = Execute(finalizer);
            result = result.Union(completion with { Normal = false });
            if (!completion.Normal) return;
            var resumed = kind switch
            {
                1 => new Flow(false, incoming.Returned, Returns: _state.Copy()),
                2 => new Flow(false, Value.Local, Breaks: _state.Copy()),
                3 => new Flow(false, Value.Local, Continues: _state.Copy()),
                4 => new Flow(false, Value.Local, Throws: _state.Copy()),
                5 => new Flow(false, Value.Local, LabeledBreaks: ImmutableDictionary<string, State>.Empty.Add(label!, _state.Copy())),
                6 => new Flow(false, Value.Local, LabeledContinues: ImmutableDictionary<string, State>.Empty.Add(label!, _state.Copy())),
                _ => Flow.Next
            };
            if (kind == 0) normalExit = Join(normalExit, _state);
            result = result.Union(resumed);
        }
    }

    private Flow Branch(Func<Flow> left, Func<Flow> right)
    {
        var before = _state.Copy();
        var first = left();
        var firstState = _state.Copy();
        _state = before;
        var second = right();
        // A returning arm cannot change the aliases used by the subsequent, continuing arm.
        if (first.Normal && second.Normal) _state.Join(firstState);
        else if (first.Normal) _state = firstState;
        return first.Union(second);
    }

    private Flow Loop(Node identity, Func<Value> test, Func<Flow> body, Func<Value> update, bool atLeastOnce = false)
    {
        var entry = _state.Copy();
        var head = entry.Copy();
        var returns = Value.Local;
        State? returnStates = null;
        State? thrownStates = null;
        ImmutableDictionary<string, State>? labeledBreaks = null;
        ImmutableDictionary<string, State>? labeledContinues = null;
        var loopLabels = _labels.Where(item => ReferenceEquals(item.Target, identity)).Select(item => item.Name).ToArray();
        State? exits = null;
        while (!_writes)
        {
            _state = head.Copy();
            test();
            if (!atLeastOnce) { exits ??= _state.Copy(); exits.Join(_state); }
            var flow = body();
            returns = returns.Union(flow.Returned);
            returnStates = Join(returnStates, flow.Returns);
            thrownStates = Join(thrownStates, flow.Throws);
            labeledBreaks = JoinLabels(labeledBreaks, flow.LabeledBreaks);
            labeledContinues = JoinLabels(labeledContinues, flow.LabeledContinues?.RemoveRange(loopLabels));
            exits = Join(exits, flow.Breaks);
            var back = Join(flow.Normal ? _state : null, flow.Continues);
            foreach (var label in loopLabels) back = Join(back, flow.LabeledContinues?.GetValueOrDefault(label));
            if (back is not null)
            {
                _state = back;
                update();
                if (atLeastOnce) exits = Join(exits, _state);
            }
            var next = entry.Copy();
            if (back is not null) next.Join(_state);
            if (next.Same(head)) break;
            head = next;
        }
        _state = exits ?? entry;
        return new Flow(exits is not null, returns, Returns: returnStates, Throws: thrownStates, LabeledBreaks: labeledBreaks, LabeledContinues: labeledContinues);
    }

    private void BindLoop(Node target, Value value)
    {
        if (target is VariableDeclaration declaration)
            foreach (var item in declaration.Declarations) Bind(item.Id, value);
        else Bind(target, value);
    }

    private Value Evaluate(Node? node)
    {
        if (node is null) return Value.Primitive;
        if (_writes) return Value.Local;
        Value result;
        switch (node)
        {
            case Identifier name:
                if (Global(name.Name) && name.Name is "setVariable" or "eval" or "Function") { _writers.Add(node); result = Value.Function(node); break; }
                if (Global(name.Name) && name.Name == "globalThis") { _writes = true; return Value.Local; }
                result = Global(name.Name) ? name.Name == "variables" ? Value.Owned : name.Name == "arguments" ? _arguments.LastOrDefault() ?? Value.Local : NativeNamespace(name.Name) ? Intrinsic(name) : Value.Local : Read(name.Name);
                break;
            case ThisExpression: result = _receivers.Count > 0 ? _receivers[^1] : Value.Local; break;
            case Super: result = _supers.LastOrDefault() ?? Value.Local; break;
            case Literal literal: result = literal.Value is string ? Value.Text : Value.Local; break;
            case IFunction: result = FunctionValue(node); break;
            case ClassExpression type: result = ClassValue(type, type.Id, type.Body, type.SuperClass); break;
            case MemberExpression member:
                var memberReceiver = Evaluate(member.Object);
                OptionalAccess(member.Optional, memberReceiver);
                if (member.Computed) EvaluateKey(member.Property);
                result = Member(memberReceiver, Name(member), member.Object is Super ? _receivers.LastOrDefault() : null);
                break;
            case ChainExpression chain:
                _optionalChains.Add(null);
                result = Evaluate(chain.Expression);
                var skipped = _optionalChains[^1];
                _optionalChains.RemoveAt(_optionalChains.Count - 1);
                if (skipped is not null)
                {
                    _state.Join(skipped);
                    result = result.Union(Value.Primitive);
                }
                break;
            case YieldExpression yielded:
                // Iterator suspension/resumption is not interpreted here. A called generator that can hand a
                // workflow-owned value to that protocol is refused conservatively, rather than losing its ownership
                // at next().value. Uncalled generators and independent local yielded values do not incur a refusal.
                _writes |= OpaqueOwnership(Evaluate(yielded.Argument));
                result = Value.Local; break;
            case SequenceExpression sequence:
                result = Value.Local;
                foreach (var expression in sequence.Expressions) result = Evaluate(expression);
                break;
            case AssignmentExpression assignment:
                if (assignment.Operator is Operator.LogicalOrAssignment or Operator.LogicalAndAssignment or Operator.NullishCoalescingAssignment)
                {
                    // The short-circuit operator retains the already evaluated reference, even if its RHS changes
                    // the local binding used to obtain that reference. The skipped branch retains the old state.
                    Value? logicalReceiver = null;
                    if (assignment.Left is MemberExpression logicalTarget)
                    {
                        logicalReceiver = Evaluate(logicalTarget.Object);
                        if (logicalTarget.Computed) EvaluateKey(logicalTarget.Property);
                    }
                    var logicalThis = assignment.Left is MemberExpression { Object: Super } ? _receivers.LastOrDefault() : null;
                    var existing = logicalReceiver is null ? Evaluate(assignment.Left) : Member(logicalReceiver, Name((MemberExpression)assignment.Left), logicalThis);
                    var beforeAssignment = _state.Copy();
                    result = Evaluate(assignment.Right);
                    if (logicalReceiver is null) Bind(assignment.Left, result);
                    else AssignMember(logicalReceiver, Name((MemberExpression)assignment.Left), result, logicalThis, assignment.Right);
                    _state.Join(beforeAssignment);
                    result = existing.Union(result);
                    break;
                }
                if (assignment.Left is MemberExpression target)
                {
                    // JavaScript evaluates the reference before the RHS can reassign the receiver's local binding.
                    var targetValue = Evaluate(target.Object);
                    var targetThis = target.Object is Super ? _receivers.LastOrDefault() : null;
                    if (target.Computed) EvaluateKey(target.Property);
                    if (assignment.Operator != Operator.Assignment) Coerce([Member(targetValue, Name(target), targetThis)]);
                    result = Evaluate(assignment.Right);
                    if (assignment.Operator != Operator.Assignment) Coerce([result]);
                    AssignMember(targetValue, Name(target), assignment.Operator == Operator.Assignment ? result : Value.Local, targetThis,
                        assignment.Operator == Operator.Assignment ? assignment.Right : null);
                    break;
                }
                if (assignment.Operator != Operator.Assignment) Coerce([Evaluate(assignment.Left)]);
                result = Evaluate(assignment.Right);
                if (assignment.Operator != Operator.Assignment) { Coerce([result]); result = Value.Local; }
                Bind(assignment.Left, result);
                break;
            case UpdateExpression update:
                if (update.Argument is MemberExpression updated)
                {
                    var receiver = Evaluate(updated.Object);
                    var updatedThis = updated.Object is Super ? _receivers.LastOrDefault() : null;
                    if (updated.Computed) EvaluateKey(updated.Property);
                    Coerce([Member(receiver, Name(updated), updatedThis)]);
                    AssignMember(receiver, Name(updated), Value.Local, updatedThis);
                }
                else { Coerce([Evaluate(update.Argument)]); Bind(update.Argument, Value.Local); }
                result = Value.Local; break;
            case UnaryExpression unary:
                if (unary.Operator == Operator.Delete && unary.Argument is MemberExpression deleted)
                {
                    var receiver = Evaluate(deleted.Object);
                    if (deleted.Computed) EvaluateKey(deleted.Property);
                    DeleteMember(receiver, Name(deleted));
                }
                else
                {
                    var operand = Evaluate(unary.Argument);
                    if (unary.Operator is Operator.UnaryPlus or Operator.UnaryNegation or Operator.BitwiseNot) Coerce([operand]);
                }
                result = Value.Local; break;
            case BinaryExpression binary when binary is not LogicalExpression:
                var lhs = Evaluate(binary.Left); var rhs = Evaluate(binary.Right);
                if (binary.Operator == Operator.In) Coerce([lhs], stringHint: true);
                else if (binary.Operator is not (Operator.StrictEquality or Operator.StrictInequality or Operator.InstanceOf) &&
                         !(binary.Operator is Operator.Equality or Operator.Inequality && IsReference(lhs) && IsReference(rhs))) Coerce([lhs, rhs]);
                result = Value.Local; break;
            case ConditionalExpression conditional:
                Evaluate(conditional.Test);
                var left = Value.Local; var right = Value.Local;
                Branch(() => { left = Evaluate(conditional.Consequent); return Flow.Next; }, () => { right = Evaluate(conditional.Alternate); return Flow.Next; });
                result = left.Union(right); break;
            case LogicalExpression logical:
                var first = Evaluate(logical.Left);
                var beforeRight = _state.Copy();
                var last = Evaluate(logical.Right);
                _state.Join(beforeRight);
                result = logical.Operator == Operator.LogicalAnd
                    ? first.MayBePrimitive ? last.Union(Value.Primitive) : last
                    : first.Union(last); break;
            case ObjectExpression value:
                result = Allocate(node);
                foreach (var property in value.Properties)
                {
                    if (property is Property item)
                    {
                        if (item.Computed) EvaluateKey(item.Key);
                        var propertyValue = Evaluate(item.Value);
                        if (!item.Computed && !item.Shorthand && !item.Method && item.Kind == PropertyKind.Init && PropertyName(item) == "__proto__")
                        { _state.Prototypes[node] = propertyValue; continue; }
                        if (item.Method || item.Kind is PropertyKind.Get or PropertyKind.Set)
                            foreach (var method in propertyValue.Functions) _homes[method] = result;
                        if (item.Kind is PropertyKind.Get or PropertyKind.Set)
                        {
                            RegisterProperty(node, PropertyName(item));
                            _state.Members.Remove((node, PropertyName(item)));
                            _state.Properties[(node, PropertyName(item))] = new(true, true, false);
                            (item.Kind == PropertyKind.Get ? _state.Getters : _state.Setters)[(node, PropertyName(item))] = propertyValue;
                        }
                        else Put(result, PropertyName(item), propertyValue);
                    }
                    else if (property is SpreadElement spread) CopyMembers(result, Evaluate(spread.Argument));
                }
                break;
            case ArrayExpression array:
                result = Allocate(node);
                _arrays.Add(node);
                for (var index = 0; index < array.Elements.Count; index++)
                    if (array.Elements[index] is { } item)
                        Put(result, item is SpreadElement ? "*" : index.ToString(System.Globalization.CultureInfo.InvariantCulture), item is SpreadElement spread ? Iterated(Evaluate(spread.Argument)) : Evaluate(item));
                break;
            case CallExpression call: result = Call(call); break;
            case TemplateLiteral template:
                // Untagged substitutions use ToString; tagged template arguments do not.
                foreach (var expression in template.Expressions) Coerce([Evaluate(expression)], stringHint: true);
                result = Value.Text; break;
            case TaggedTemplateExpression tagged:
                result = Apply(Evaluate(tagged.Tag), [Value.Object(tagged.Quasi), .. tagged.Quasi.Expressions.Select(Evaluate)], Value.Local);
                break;
            case NewExpression construction:
                var constructor = Evaluate(construction.Callee);
                var constructorArguments = Arguments(construction.Arguments, constructor);
                result = Allocate(node);
                if (constructor.Functions.Count > 0)
                {
                    if (!constructor.Session && constructor.Functions.IsSupersetOf(constructor.Objects) &&
                        constructor.Functions.All(target => OrdinaryConstructor(target))) _ordinaryObjects.Add(node);
                    else _unprovenConstructions.Add(node);
                    var explicitResult = Apply(constructor, constructorArguments, result);
                    result = result.Union(explicitResult);
                }
                else if (construction.Callee is Identifier native && Global(native.Name) && native.Name is "Date" or "RegExp" or "String" or "Number" or "Boolean")
                {
                    _ordinaryObjects.Add(node);
                    if (native.Name == "String") result = result with { MayBeString = true };
                    if (native.Name is "String" or "Number" or "Boolean" or "Date") _primitiveBoxes[node] = native.Name;
                    if (native.Name is "String" or "RegExp") Coerce(native.Name == "String" ? constructorArguments.Take(1) : constructorArguments.Take(2), stringHint: true);
                    else if (native.Name == "Number") Coerce(constructorArguments.Take(1));
                    else if (native.Name == "Date") Coerce(constructorArguments);
                    // Boolean conversion tests truthiness and never calls object conversion hooks.
                }
                else if (construction.Callee is Identifier { Name: "Object" } && Global("Object"))
                {
                    _ordinaryObjects.Add(node);
                    if (constructorArguments.FirstOrDefault() is { } wrapped &&
                        (wrapped.Session || wrapped.Objects.Count > 0 || wrapped.Functions.Count > 0))
                        result = wrapped.MayBePrimitive ? result.Union(wrapped) : wrapped;
                }
                else if (construction.Callee is Identifier { Name: "Array" } && Global("Array"))
                {
                    _arrays.Add(node);
                    foreach (var argument in constructorArguments) Put(result, "*", Member(result, "*").Union(argument));
                }
                else if (construction.Callee is Identifier collection && Global(collection.Name) && collection.Name is "Set" or "Map" or "WeakSet" or "WeakMap")
                {
                    _ordinaryObjects.Add(node);
                    if (constructorArguments.Length > 0) Put(result, "*", Iterated(constructorArguments[0]));
                }
                else
                {
                    _unprovenConstructions.Add(node);
                    _writes |= constructorArguments.Any(argument => OpaqueOwnership(argument));
                }
                break;
            default:
                foreach (var child in node.ChildNodes)
                    if (child is Statement) Execute(child); else Evaluate(child);
                result = Value.Local; break;
        }
        if (!result.Session && result.Objects.Count == 0 && result.Functions.Count == 0)
            result = result with { MayBePrimitive = true };
        if (_opaqueDepth > 0 && result.Session) _writes = true;
        foreach (var exceptional in _exceptionStates) exceptional.Join(_state);
        return result;
    }

    private void CaptureParameterValues(Node pattern, Dictionary<string, Value> values)
    {
        switch (pattern)
        {
            case Identifier name: values[name.Name] = Read(name.Name); break;
            case AssignmentPattern assignment: CaptureParameterValues(assignment.Left, values); break;
            case RestElement rest: CaptureParameterValues(rest.Argument, values); break;
            case ArrayPattern array:
                foreach (var item in array.Elements.OfType<Node>()) CaptureParameterValues(item, values);
                break;
            case ObjectPattern obj:
                foreach (var property in obj.Properties)
                    CaptureParameterValues(property is Property item ? item.Value : property, values);
                break;
        }
    }

    private void Bind(Node pattern, Value value)
    {
        switch (pattern)
        {
            case Identifier name when Resolve(name.Name) is { } binding: _state.Bindings[binding] = value; break;
            case MemberExpression member: WriteMember(member, value); break;
            case AssignmentPattern assignment: Bind(assignment.Left, value.Union(Evaluate(assignment.Right))); break;
            case ObjectPattern patternObject:
                foreach (var property in patternObject.Properties)
                    if (property is Property item)
                    {
                        if (item.Computed) EvaluateKey(item.Key);
                        Bind(item.Value, Member(value, PropertyName(item)));
                    }
                    else if (property is RestElement rest) Bind(rest.Argument, LocalCopy(pattern, value));
                break;
            case ArrayPattern array:
                var elements = Iterated(value, out var patternIterator, includeStored: false);
                for (var index = 0; index < array.Elements.Count; index++)
                    if (array.Elements[index] is { } item)
                    {
                        if (item is RestElement rest)
                        {
                            var remainder = LocalCopy(pattern, value, array: true);
                            Put(remainder, "*", Member(remainder, "*").Union(elements));
                            Bind(rest.Argument, remainder);
                        }
                        else Bind(item, Member(value, index.ToString(System.Globalization.CultureInfo.InvariantCulture)).Union(elements));
                    }
                // A finite binding pattern leaves the remaining iterator suspended; a rest binding consumes it.
                if (array.Elements.All(item => item is not RestElement)) CloseIterator(patternIterator);
                break;
        }
    }

    private void WriteMember(MemberExpression member, Value value)
    {
        var receiver = Evaluate(member.Object);
        if (member.Computed) EvaluateKey(member.Property);
        AssignMember(receiver, Name(member), value, member.Object is Super ? _receivers.LastOrDefault() : null);
    }
    private void AssignMember(Value receiver, string name, Value value, Value? actualReceiver = null, Node? syntax = null)
    {
        if (receiver.Session || actualReceiver?.Session == true) _writes = true;
        if (receiver.Objects.Any(item => _intrinsics.Values.Any(intrinsic => intrinsic.Objects.Contains(item)))) _writes = true;
        foreach (var item in receiver.Objects)
        {
            var target = Value.Object(item);
            var arrayLength = name == "length" && KnownArray(actualReceiver ?? target);
            if (arrayLength && !_state.Properties.ContainsKey((item, name)))
            { RegisterProperty(item, name); _state.Properties[(item, name)] = new(false, false, true); }
            var setter = Lookup(target, name, (owner, key) => _state.Setters.GetValueOrDefault((owner, key), Value.Local));
            if (setter.Functions.Count > 0) Apply(setter, [value], actualReceiver ?? target);
            var accessor = Lookup(target, name, (owner, key) => _state.Getters.ContainsKey((owner, key)) || _state.Setters.ContainsKey((owner, key)) ? Value.Owned : Value.Local).Session;
            var ownData = _state.Members.ContainsKey((item, name));
            if (accessor && !ownData && name != "*") continue;
            var readOnly = Lookup(target, name, (owner, key) => _state.Properties.TryGetValue((owner, key), out var attributes) && !attributes.Writable ? Value.Owned : Value.Local).Session;
            if (readOnly && !accessor && name != "*") continue;
            if (arrayLength)
            {
                // ArraySetLength performs ToUint32 and ToNumber separately; user conversion hooks may run twice.
                Coerce([value]); Coerce([value]);
                ShrinkArray(actualReceiver ?? target, syntax);
            }
            Put(actualReceiver ?? target, name, arrayLength ? Value.Local : value, weak: receiver.Objects.Count != 1 || accessor, define: false);
        }
    }
    private Value Member(Value receiver, string name, Value? getterReceiver = null)
    {
        var result = StoredMember(receiver, name);
        var getter = Lookup(receiver, name, (owner, key) => _state.Getters.GetValueOrDefault((owner, key), Value.Local));
        if (getter.Functions.Count > 0) result = result.Union(Apply(getter, [], getterReceiver ?? receiver));
        if (name == "constructor" && result.Functions.Count == 0 && !DefinitelyHasProperty(receiver, name))
            result = result.Union(Value.Function(_dynamicConstructor!));
        if (!DefinitelyHasProperty(receiver, name)) result = result.Union(Value.Primitive);
        return result;
    }

    private bool DefinitelyHasProperty(Value receiver, string name, HashSet<Node>? seen = null)
    {
        if (receiver.Session || receiver.MayBePrimitive || receiver.Objects.Count == 0) return false;
        seen ??= new(ReferenceEqualityComparer.Instance);
        foreach (var item in receiver.Objects)
        {
            if (!seen.Add(item)) return false;
            if (_state.Properties.ContainsKey((item, name)) && !_state.OptionalProperties.Contains((item, name))) continue;
            if (!DefinitelyHasProperty(_state.Prototypes.GetValueOrDefault(item, Value.Local), name, seen)) return false;
        }
        return true;
    }
    private Value StoredMember(Value receiver, string name)
    {
        var result = receiver.Session ? Value.Owned : Value.Local;
        if (receiver.Objects.Any(item => _intrinsics.Values.Any(intrinsic => intrinsic.Objects.Contains(item)))) result = result.Union(receiver);
        return result.Union(Lookup(receiver, name, (owner, key) => _state.Members.GetValueOrDefault((owner, key), Value.Local), sessionOwnership: true));
    }

    private IEnumerable<string> PropertyNames(Value receiver, HashSet<Node>? seen = null)
    {
        seen ??= new(ReferenceEqualityComparer.Instance);
        foreach (var item in receiver.Objects)
        {
            if (!seen.Add(item)) continue;
            foreach (var name in _state.PropertyOrder.GetValueOrDefault(item, [])) yield return name;
            foreach (var name in PropertyNames(_state.Prototypes.GetValueOrDefault(item, Value.Local), seen)) yield return name;
        }
    }

    private Value Lookup(Value receiver, string name, Func<Node, string, Value> own, HashSet<Node>? seen = null, bool sessionOwnership = false)
    {
        if (name == "*")
        {
            var all = Value.Local;
            foreach (var property in PropertyNames(receiver).Where(property => property != "*").Distinct()) all = all.Union(Lookup(receiver, property, own, sessionOwnership: sessionOwnership));
            return all.Union(LookupWildcard(receiver, own, new(ReferenceEqualityComparer.Instance), sessionOwnership));
        }
        seen ??= new(ReferenceEqualityComparer.Instance);
        var result = receiver.Session && sessionOwnership ? Value.Owned : Value.Local;
        foreach (var item in receiver.Objects)
        {
            if (!seen.Add(item)) continue;
            result = result.Union(own(item, name)).Union(own(item, "*"));
            if (sessionOwnership && !_state.Properties.ContainsKey((item, name)) && _unknownMembers.TryGetValue(item, out var nested))
                result = result.Union(Value.Object(nested));
            // An own data property shadows an inherited getter/setter, including a class prototype accessor.
            if (!_state.Properties.ContainsKey((item, name)) || _state.OptionalProperties.Contains((item, name)))
                result = result.Union(Lookup(_state.Prototypes.GetValueOrDefault(item, Value.Local), name, own, seen, sessionOwnership));
        }
        return result;
    }

    private Value LookupWildcard(Value receiver, Func<Node, string, Value> own, HashSet<Node> seen, bool sessionOwnership)
    {
        var result = receiver.Session && sessionOwnership ? Value.Owned : Value.Local;
        foreach (var item in receiver.Objects)
            if (seen.Add(item))
            {
                result = result.Union(own(item, "*")).Union(LookupWildcard(_state.Prototypes.GetValueOrDefault(item, Value.Local), own, seen, sessionOwnership));
                if (sessionOwnership && _unknownMembers.TryGetValue(item, out var nested)) result = result.Union(Value.Object(nested));
            }
        return result;
    }

    private void DeleteMember(Value receiver, string name)
    {
        _writes |= receiver.Session;
        // Deletion does not invoke a getter or setter. Only a unique, configurable own property can disappear
        // definitely; unknown keys, allocation summaries and nonconfigurable descriptors retain their aliases.
        if (receiver.Objects.Count != 1 || name == "*") return;
        var item = receiver.Objects.Single();
        if (name == "length" && KnownArray(receiver)) return;
        if (_summaryObjects.Contains(item) || !_state.Properties.TryGetValue((item, name), out var attributes) || !attributes.Configurable) return;
        _state.Members.Remove((item, name)); _state.Getters.Remove((item, name)); _state.Setters.Remove((item, name)); _state.Properties.Remove((item, name));
        _state.OptionalProperties.Remove((item, name));
        if (_state.PropertyOrder.TryGetValue(item, out var order)) _state.PropertyOrder[item] = order.Remove(name);
    }

    private void ShrinkArray(Value receiver, Node? syntax)
    {
        if (receiver.Objects.Count != 1 || syntax is not Literal { Value: double or float or int or long or decimal } literal) return;
        var length = Convert.ToDouble(literal.Value, System.Globalization.CultureInfo.InvariantCulture);
        if (length < 0 || length > uint.MaxValue || length != Math.Truncate(length)) return;
        var item = receiver.Objects.Single();
        if (_summaryObjects.Contains(item)) return;
        foreach (var name in _state.PropertyOrder.GetValueOrDefault(item, []).Where(name => ArrayIndex(name) is { } index && index >= length)
                     .OrderByDescending(name => ArrayIndex(name)).ToArray())
        {
            // A failed deletion restores length above that index and stops the shrink. Lower indices remain live.
            if (!_state.Properties.GetValueOrDefault((item, name), Attributes.Ordinary).Configurable) break;
            DeleteMember(receiver, name);
        }
    }

    private void Put(Value receiver, string name, Value value, bool weak = false, bool define = true)
    {
        foreach (var item in receiver.Objects)
        {
            RegisterProperty(item, name);
            var mayAlias = weak || receiver.Objects.Count != 1 || name == "*" || _summaryObjects.Contains(item);
            if (!mayAlias) _state.OptionalProperties.Remove((item, name));
            if (!mayAlias && define) { _state.Getters.Remove((item, name)); _state.Setters.Remove((item, name)); _state.Properties[(item, name)] = Attributes.Ordinary; }
            _state.Members[(item, name)] = mayAlias
                ? _state.Members.GetValueOrDefault((item, name), Value.Local).Union(value) : value;
        }
    }

    private void RegisterProperty(Node item, string name)
    {
        var order = _state.PropertyOrder.GetValueOrDefault(item, []);
        if (!order.Contains(name)) _state.PropertyOrder[item] = order.Add(name);
        _state.Properties.TryAdd((item, name), Attributes.Ordinary);
        if (name == "*") _state.Unordered.Add(item);
    }

    private static uint? ArrayIndex(string name) => uint.TryParse(name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) &&
        index != uint.MaxValue && name == index.ToString(System.Globalization.CultureInfo.InvariantCulture) ? index : null;

    private void OwnProperties(Value source, Action<Value, string, Value> visit, bool symbols = false, bool elementsOnly = false)
    {
        // OrdinaryOwnPropertyKeys orders array indices numerically, then other strings by creation time. A single
        // registry keeps data and accessor definitions interleaved. Unknown keys, joined creation orders and summary
        // allocations do not have one proven order: join possible callback effects to a finite ownership fixed point.
        var entry = _state.Copy(); State? exits = null;
        foreach (var item in source.Objects)
        {
            _state = entry.Copy();
            var receiver = Value.Object(item);
            var names = _state.PropertyOrder.GetValueOrDefault(item, [])
                .Where(name => (elementsOnly || _state.Properties.GetValueOrDefault((item, name), Attributes.Ordinary).Enumerable) &&
                    (symbols || !name.StartsWith("@@", StringComparison.Ordinal)) && (!elementsOnly || name == "*" || ArrayIndex(name) is not null))
                .OrderBy(name => ArrayIndex(name) is not null ? 0 : name.StartsWith("@@", StringComparison.Ordinal) ? 2 : 1)
                .ThenBy(name => ArrayIndex(name) ?? 0).ToArray();
            if (_state.Unordered.Contains(item))
            {
                while (!_writes)
                {
                    var before = _state.Copy(); var after = before.Copy();
                    foreach (var name in names)
                    {
                        _state = before.Copy(); visit(receiver, name, Member(receiver, name)); after.Join(_state);
                    }
                    _state = after;
                    if (_state.Same(before)) break;
                }
            }
            else foreach (var name in names) visit(receiver, name, Member(receiver, name));
            exits ??= _state.Copy(); exits.Join(_state);
        }
        _state = exits ?? entry;
    }
    private Value LocalCopy(Node identity, Value source, bool array = false)
    {
        var copy = Allocate(identity);
        if (array) _arrays.Add(identity);
        CopyMembers(copy, source);
        return copy;
    }
    private void CopyMembers(Value target, Value source, bool assign = false)
    {
        if (source.Session) Put(target, "*", Value.Owned);
        OwnProperties(source, (_, name, value) => { if (assign) AssignMember(target, name, value); else Put(target, name, value); }, symbols: true);
    }

    private void DefineProperty(Value target, string name, Value descriptor, Node syntax)
    {
        _writes |= target.Session;
        // ToPropertyDescriptor reads these fields in this order, potentially executing descriptor getters.
        var fields = new Dictionary<string, Value>(StringComparer.Ordinal);
        foreach (var field in new[] { "enumerable", "configurable", "value", "writable", "get", "set" }) fields[field] = Member(descriptor, field);
        var names = PropertyNames(descriptor).ToHashSet(StringComparer.Ordinal);
        var plain = syntax is ObjectExpression literal && literal.Properties.All(item => item is Property { Computed: false, Kind: PropertyKind.Init });
        bool? Flag(string field)
        {
            if (!plain) return null;
            var property = ((ObjectExpression)syntax).Properties.OfType<Property>().LastOrDefault(item => PropertyName(item) == field);
            return property is null ? false : property.Value is Literal { Value: bool flag } ? flag : null;
        }
        foreach (var item in target.Objects)
        {
            var key = (item, name);
            var existed = _state.Properties.TryGetValue(key, out var previous);
            var attributes = new Attributes(names.Contains("configurable") ? Flag("configurable") == true : existed && previous.Configurable,
                names.Contains("enumerable") ? Flag("enumerable") != false : existed && previous.Enumerable,
                names.Contains("writable") ? Flag("writable") != false : existed && previous.Writable);
            var weak = target.Objects.Count != 1 || name == "*" || _summaryObjects.Contains(item) ||
                existed && !previous.Configurable;
            RegisterProperty(item, name);
            var accessor = names.Contains("get") || names.Contains("set");
            var data = names.Contains("value") || names.Contains("writable");
            var wasAccessor = _state.Getters.ContainsKey(key) || _state.Setters.ContainsKey(key);
            if (!weak)
            {
                _state.OptionalProperties.Remove(key);
                if (accessor && !wasAccessor) _state.Members.Remove(key);
                if (data && wasAccessor) { _state.Getters.Remove(key); _state.Setters.Remove(key); }
            }
            if (names.Contains("get")) _state.Getters[key] = weak ? _state.Getters.GetValueOrDefault(key, Value.Local).Union(fields["get"]) : fields["get"];
            if (names.Contains("set")) _state.Setters[key] = weak ? _state.Setters.GetValueOrDefault(key, Value.Local).Union(fields["set"]) : fields["set"];
            if (names.Contains("value") || !accessor && !existed || data && wasAccessor && !weak)
                _state.Members[key] = weak ? _state.Members.GetValueOrDefault(key, Value.Local).Union(fields["value"]) : fields["value"];
            _state.Properties[key] = weak ? _state.Properties[key].Union(attributes) : attributes;
        }
    }
    private bool ContainsSession(Value value, HashSet<(Node Node, bool Callable)>? seen = null)
    {
        if (value.Session) return true;
        seen ??= [];
        return value.Objects.Any(item => seen.Add((item, false)) && (_state.Members.Where(entry => ReferenceEquals(entry.Key.Object, item)).Any(entry => ContainsSession(entry.Value, seen)) ||
                   _state.Getters.Concat(_state.Setters).Where(entry => ReferenceEquals(entry.Key.Object, item)).Any(entry => ContainsSession(entry.Value, seen)) ||
                   ContainsSession(_state.Prototypes.GetValueOrDefault(item, Value.Local), seen))) ||
               value.Functions.Any(item => seen.Add((item, true)) && _closures.TryGetValue(item, out var closure) && closure.Captured.Values.Any(captured => ContainsSession(captured, seen)));
    }

    private Value FunctionValue(Node function)
    {
        if (!_closures.ContainsKey(function))
            _closures[function] = new Closure(_scopes.ToArray(), new Dictionary<Binding, Value>(_state.Bindings), _receivers.LastOrDefault() ?? Value.Local, _arguments.LastOrDefault() ?? Value.Local, _supers.LastOrDefault() ?? Value.Local);
        if (function is IFunction callable)
        {
            Allocate(function);
            if (function is not ArrowFunctionExpression && !_state.Properties.ContainsKey((function, "prototype")))
            {
                var prototype = Allocate(callable.Body);
                Put(Value.Object(function), "prototype", prototype);
                _state.Properties[(function, "prototype")] = new Attributes(false, false, true);
                Put(prototype, "constructor", Value.Function(function));
                _state.Properties[(callable.Body, "constructor")] = new Attributes(true, false, true);
            }
        }
        return Value.Function(function);
    }

    private Value ClassValue(Node node, Identifier? name, ClassBody body, Node? superClass)
    {
        var local = node is ClassExpression ? scopeOf(node) : null;
        if (local is not null) EnterScope(node, local);
        try
        {
            var value = FunctionValue(node).Union(Allocate(node));
            if (name is not null) Bind(name, value);
            _classes[node] = new ClassInfo(body, Evaluate(superClass), superClass is not null);
            _state.Prototypes[node] = _classes[node].Super;
            var prototype = Allocate(body);
            Put(value, "prototype", prototype);
            _state.Properties[(node, "prototype")] = new(false, false, false);
            var basePrototype = StoredMember(_classes[node].Super, "prototype");
            _state.Prototypes[body] = basePrototype;
            _receivers.Add(value);
            _supers.Add(_classes[node].Super);
            try
            {
                foreach (var element in body.Body)
                {
                    if (element is MethodDefinition method)
                    {
                        if (method.Computed) EvaluateKey(method.Key);
                        var function = FunctionValue(method.Value);
                        if (method.Kind == PropertyKind.Constructor) _homes[method.Value] = value;
                        if (method.Static) DefineMethod(node, method, function);
                        else if (method.Kind != PropertyKind.Constructor) DefineMethod(body, method, function);
                    }
                    else if (element is PropertyDefinition field)
                    {
                        if (field.Computed) EvaluateKey(field.Key);
                        if (field.Static) Put(value, ClassMemberName(field.Key, field.Computed), Evaluate(field.Value));
                    }
                    else if (element is StaticBlock block) Execute(block);
                }
            }
            finally { _receivers.RemoveAt(_receivers.Count - 1); _supers.RemoveAt(_supers.Count - 1); }
            return value;
        }
        finally { if (local is not null) _scopes.RemoveAt(_scopes.Count - 1); }
    }

    private void DefineMethod(Node target, MethodDefinition method, Value function)
    {
        _homes[method.Value] = Value.Object(target);
        var name = ClassMemberName(method.Key, method.Computed);
        if (method.Kind is PropertyKind.Get or PropertyKind.Set)
        {
            RegisterProperty(target, name);
            _state.Members.Remove((target, name));
            (method.Kind == PropertyKind.Get ? _state.Getters : _state.Setters)[(target, name)] = function;
        }
        else Put(Value.Object(target), name, function);
        _state.Properties[(target, name)] = new(true, false, method.Kind is not (PropertyKind.Get or PropertyKind.Set));
    }

    private Value ConstructClass(Node node, ClassInfo type, IReadOnlyList<Value> arguments, Value receiver)
    {
        if (!_closures.TryGetValue(node, out var closure)) return Value.Local;
        if (!_calls.Add(node)) { _writes |= arguments.Any(argument => OpaqueOwnership(argument)); return Value.Local; }
        var callerScopes = _scopes.ToArray();
        _scopes.Clear(); _scopes.AddRange(closure.Scopes);
        foreach (var (binding, value) in closure.Captured) _state.Bindings.TryAdd(binding, value);
        _receivers.Add(receiver);
        var frame = new ConstructorFrame(type, receiver);
        _constructors.Add(frame);
        var roots = receiver.Objects.Where(_constructingObjects.Add).ToArray();
        foreach (var target in roots)
            _state.Prototypes[target] = _summaryObjects.Contains(target) ? _state.Prototypes.GetValueOrDefault(target, Value.Local).Union(Value.Object(type.Body)) : Value.Object(type.Body);
        try
        {
            var constructor = type.Body.Body.OfType<MethodDefinition>().FirstOrDefault(method => method.Kind == PropertyKind.Constructor);
            if (type.Derived && constructor is null)
            {
                if (type.Super.Functions.Count > 0) frame.Result = Apply(type.Super, arguments, receiver);
                else _writes |= OpaqueOwnership(type.Super) || arguments.Any(argument => OpaqueOwnership(argument));
                frame.Receiver = receiver.Union(frame.Result);
            }
            // Base fields initialize before its constructor. A derived explicit constructor initializes fields at
            // its actual super(...) call, rather than eagerly invoking the base twice with different arguments.
            if (!type.Derived || constructor is null) InitializeFields(frame);
            return frame.Result.Union(constructor is null ? Value.Local : Invoke(constructor.Value, arguments, frame.Receiver));
        }
        finally
        {
            foreach (var target in roots) _constructingObjects.Remove(target);
            _constructors.RemoveAt(_constructors.Count - 1);
            _receivers.RemoveAt(_receivers.Count - 1);
            _scopes.Clear(); _scopes.AddRange(callerScopes);
            _calls.Remove(node);
        }
    }

    private void InitializeFields(ConstructorFrame frame)
    {
        _receivers.Add(frame.Receiver);
        _supers.Add(_state.Prototypes.GetValueOrDefault(frame.Type.Body, Value.Local));
        try
        {
            foreach (var field in frame.Type.Body.Body.OfType<PropertyDefinition>().Where(field => !field.Static))
            {
                _writes |= frame.Receiver.Session;
                Put(frame.Receiver, ClassMemberName(field.Key, field.Computed), Evaluate(field.Value));
            }
        }
        finally { _receivers.RemoveAt(_receivers.Count - 1); _supers.RemoveAt(_supers.Count - 1); }
    }

    private string ClassMemberName(Node key, bool computed) => !computed && key is Identifier name ? name.Name : Key(key);

    private Value Invoke(Node node, IReadOnlyList<Value> arguments, Value? receiver = null)
    {
        // The body setter remains an effectful callable through aliases, arguments, containers and callbacks.
        // Merely carrying the function as a value is not a write; only invocation activates the capability.
        if (_writers.Contains(node)) { _writes = true; return Value.Local; }
        if (_classes.TryGetValue(node, out var type)) return ConstructClass(node, type, arguments, receiver ?? Value.Local);
        if (node is not IFunction function || !_closures.TryGetValue(node, out var closure)) return Value.Local;
        // A recursive/opaque cycle is not unrolled indefinitely. Refuse when it carries workflow ownership;
        // otherwise its unknown result cannot introduce workflow ownership into a later local mutation.
        if (!_calls.Add(node))
        {
            _writes |= arguments.Any(argument => ContainsSession(argument)) || closure.Captured.Values.Any(value => ContainsSession(value));
            return Value.Local;
        }
        var callerScopes = _scopes.ToArray();
        var previous = new Dictionary<Binding, Value>(_state.Bindings);
        _scopes.Clear(); _scopes.AddRange(closure.Scopes);
        foreach (var (binding, value) in closure.Captured) _state.Bindings.TryAdd(binding, value);
        var local = scopeOf(node);
        if (local is not null) EnterScope(node, local);
        _receivers.Add(node is ArrowFunctionExpression ? closure.This : receiver ?? Value.Local);
        _arguments.Add(node is ArrowFunctionExpression ? closure.Arguments : ArgumentObject(node, arguments));
        var home = _homes.GetValueOrDefault(node, Value.Local);
        _supers.Add(node is ArrowFunctionExpression ? closure.Super : home.Objects.Aggregate(Value.Local, (result, item) => result.Union(_state.Prototypes.GetValueOrDefault(item, Value.Local))));
        try
        {
            for (var index = 0; index < function.Params.Count; index++)
            {
                if (function.Params[index] is RestElement rest)
                {
                    var restArray = Allocate(rest);
                    _arrays.Add(rest);
                    for (var position = index; position < arguments.Count; position++)
                        Put(restArray, (position - index).ToString(System.Globalization.CultureInfo.InvariantCulture), arguments[position]);
                    foreach (var value in arguments.Skip(index).Where(value => value.UnknownSpread)) Put(restArray, "*", value);
                    Bind(rest.Argument, restArray);
                    break;
                }
                Bind(function.Params[index], index < arguments.Count ? arguments[index] : Value.Local);
            }
            // Body var bindings start with the matching parameter's value, including owned objects
            // and effectful callables. Keep separate cells for parameter-initializer closures.
            var parameterValues = new Dictionary<string, Value>(StringComparer.Ordinal);
            foreach (var parameter in function.Params) CaptureParameterValues(parameter, parameterValues);
            var flow = function.Body is BlockStatement ? Execute(function.Body, parameterValues) : new Flow(false, Evaluate(function.Body), Returns: _state.Copy());
            var exits = Join(flow.Normal ? _state : null, flow.Returns);
            if (exits is not null) _state = exits;
            if (flow.Throws is not null) foreach (var exceptional in _exceptionStates) exceptional.Join(flow.Throws);
            // Hoisted nested functions were created before this invocation's parameters were bound. Retain the
            // actual lexical cells before restoring the caller, including a returned closure's parameter aliases.
            if (local is not null)
                foreach (var nested in _closures.Values.Where(item => item.Scopes.Contains(local)))
                    foreach (var (binding, value) in _state.Bindings.Where(item => nested.Scopes.Contains(item.Key.Scope)))
                        nested.Captured[binding] = nested.Captured.GetValueOrDefault(binding, Value.Local).Union(value);
            return flow.Normal ? flow.Returned.Union(Value.Primitive) : flow.Returned;
        }
        finally
        {
            foreach (var binding in _state.Bindings.Keys.Where(binding => !closure.Scopes.Contains(binding.Scope)).ToArray())
                if (previous.TryGetValue(binding, out var value)) _state.Bindings[binding] = value; else _state.Bindings.Remove(binding);
            _scopes.Clear(); _scopes.AddRange(callerScopes);
            _receivers.RemoveAt(_receivers.Count - 1);
            _arguments.RemoveAt(_arguments.Count - 1);
            _supers.RemoveAt(_supers.Count - 1);
            _calls.Remove(node);
        }
    }

    private bool OrdinaryConstructor(Node target, HashSet<Node>? seen = null)
    {
        if (target is IFunction) return true;
        seen ??= new(ReferenceEqualityComparer.Instance);
        if (!seen.Add(target) || !_classes.TryGetValue(target, out var type)) return false;
        return !type.Derived || !type.Super.Session && type.Super.Functions.Count > 0 &&
            type.Super.Functions.IsSupersetOf(type.Super.Objects) &&
            type.Super.Functions.All(parent => OrdinaryConstructor(parent, new HashSet<Node>(seen, ReferenceEqualityComparer.Instance)));
    }

    private Value ArgumentObject(Node identity, IReadOnlyList<Value> arguments)
    {
        var value = Allocate(identity);
        for (var index = 0; index < arguments.Count; index++) Put(value, index.ToString(System.Globalization.CultureInfo.InvariantCulture), arguments[index]);
        foreach (var argument in arguments.Where(argument => argument.UnknownSpread)) Put(value, "*", argument);
        return value;
    }

    private Value Apply(Value function, IReadOnlyList<Value> arguments, Value receiver)
    {
        if (function.Functions.Count == 0)
        {
            _writes |= OpaqueOwnership(receiver) || arguments.Any(argument => OpaqueOwnership(argument));
            return Value.Local;
        }
        var entry = _state.Copy(); State? exits = null; var result = Value.Local;
        foreach (var target in function.Functions)
        {
            _state = entry.Copy(); result = result.Union(Invoke(target, arguments, receiver));
            exits ??= _state.Copy(); exits.Join(_state);
        }
        _state = exits ?? entry;
        return result;
    }

    private bool OpaqueOwnership(Value value)
    {
        // Unknown native code can invoke callbacks nested in a wrapper/prototype and can repeat them in an
        // unknown order. Inspect that finite callable graph to an ownership fixed point, including callbacks
        // which acquire a workflow value themselves rather than receiving one as an argument.
        _opaqueDepth++;
        try
        {
            while (!_writes)
            {
                var before = _state.Copy();
                if (Inspect(value, null, [])) return true;
                if (_state.Same(before)) return false;
            }
            return true;
        }
        finally { _opaqueDepth--; }

        bool Inspect(Value candidate, Value? receiver, HashSet<(Node Node, bool Callable)> seen)
        {
            if (_writes || ContainsSession(candidate)) return true;
            foreach (var function in candidate.Functions)
                if (seen.Add((function, true)) && Inspect(Invoke(function, [], receiver), null, seen)) return true;
            foreach (var item in candidate.Objects)
            {
                if (!seen.Add((item, false))) continue;
                var actualReceiver = receiver ?? Value.Object(item);
                foreach (var field in _state.Members.Where(entry => ReferenceEquals(entry.Key.Object, item)).Select(entry => entry.Value).ToArray())
                    if (Inspect(field, actualReceiver, seen)) return true;
                foreach (var getter in _state.Getters.Where(entry => ReferenceEquals(entry.Key.Object, item)).Select(entry => entry.Value).ToArray())
                    if (Inspect(Apply(getter, [], actualReceiver), null, seen)) return true;
                if (Inspect(_state.Prototypes.GetValueOrDefault(item, Value.Local), actualReceiver, seen)) return true;
            }
            return _writes || ContainsSession(candidate);
        }
    }

    private Value[] Arguments(IEnumerable<Node> nodes, Value function)
    {
        var arguments = new List<Value>();
        var width = Math.Max(1, function.Functions.OfType<IFunction>().Select(target => target.Params.Count).DefaultIfEmpty(1).Max());
        int? uncertain = null;
        var uncertainValues = Value.Local;
        foreach (var node in nodes)
        {
            if (node is SpreadElement spread)
            {
                var source = Evaluate(spread.Argument);
                var yielded = Iterated(source, includeStored: false);
                var expanded = ArrayArguments(source, width, out var exact);
                if (!exact) for (var index = 0; index < expanded.Length; index++) expanded[index] = expanded[index].Union(yielded);
                if (!exact) uncertain ??= arguments.Count;
                arguments.AddRange(expanded);
                foreach (var value in expanded) uncertainValues = uncertainValues.Union(value);
            }
            else
            {
                var value = Evaluate(node);
                arguments.Add(value);
                if (uncertain is not null) uncertainValues = uncertainValues.Union(value);
            }
        }
        if (uncertain is { } from)
            for (var index = from; index < arguments.Count; index++) arguments[index] = arguments[index].Union(uncertainValues);
        return arguments.ToArray();
    }

    private Value[] ArrayArguments(Value source, int width, out bool exact)
    {
        var arrays = source.Objects.OfType<ArrayExpression>().ToArray();
        exact = !source.Session && arrays.Length == source.Objects.Count && arrays.Length > 0 &&
                arrays.All(array => array.Elements.All(item => item is not SpreadElement)) &&
                source.Objects.All(item => !_state.Members.ContainsKey((item, "*")) && !_summaryObjects.Contains(item));
        var count = exact ? arrays.Max(array => array.Elements.Count) : Math.Max(1, width);
        var unknown = exact ? Value.Local : Member(source, "*");
        var inexact = !exact;
        return Enumerable.Range(0, count).Select(index => Member(source, index.ToString(System.Globalization.CultureInfo.InvariantCulture)).Union(unknown) with { UnknownSpread = inexact }).ToArray();
    }

    private Value Iterated(Value source, bool includeStored = true) => Iterated(source, out _, includeStored);

    private Value Iterated(Value source, out Value iterator, bool includeStored = true)
    {
        // GetIterator calls @@iterator and IteratorStep calls next. Follow local implementations to a finite
        // ownership fixed point at the consuming syntax, not when the iterator is merely defined. Generator
        // suspension itself is intentionally not simulated (see YieldExpression's conservative boundary).
        // Workflow JSON arrays have no executable custom protocol; their elements retain session ownership.
        iterator = Value.Local;
        var method = Member(source, "@@iterator");
        if (method.Functions.Count == 0)
        {
            if (!source.Session) _writes |= OpaqueOwnership(method);
            return includeStored ? Member(source, "*") : Value.Local;
        }
        iterator = Apply(method, [], source);
        var next = Member(iterator, "next");
        if (next.Functions.Count == 0)
        {
            _writes |= OpaqueOwnership(iterator);
            return source.Session ? Value.Owned : Value.Local;
        }
        var result = source.Session ? Value.Owned : Value.Local;
        while (!_writes)
        {
            var before = _state.Copy();
            var step = Apply(next, [], iterator);
            Member(step, "done");
            result = result.Union(Member(step, "value"));
            _state.Join(before);
            if (_state.Same(before)) break;
        }
        return result;
    }

    private void CloseIterator(Value iterator)
    {
        // IteratorClose gets and calls return only at an early/abrupt consumer boundary. Its receiver can own
        // workflow references even though next's yielded values do not. Ordinary exhausted spreads do not close.
        var close = Member(iterator, "return");
        if (close.Functions.Count > 0) Apply(close, [], iterator);
        else _writes |= OpaqueOwnership(close);
    }

    private Flow CloseLoop(Flow flow, Value iterator, Node loop)
    {
        var normal = _state;
        State? Close(State? state)
        {
            if (state is null) return null;
            _state = state.Copy(); CloseIterator(iterator);
            return _state.Copy();
        }
        var breaks = Close(flow.Breaks); var returns = Close(flow.Returns); var throws = Close(flow.Throws);
        var labeledBreaks = flow.LabeledBreaks;
        foreach (var (label, state) in flow.LabeledBreaks ?? ImmutableDictionary<string, State>.Empty)
            labeledBreaks = labeledBreaks!.SetItem(label, Close(state)!);
        var labeledContinues = flow.LabeledContinues;
        foreach (var (label, state) in flow.LabeledContinues ?? ImmutableDictionary<string, State>.Empty)
            if (!_labels.Any(item => item.Name == label && ReferenceEquals(item.Target, loop)))
                labeledContinues = labeledContinues!.SetItem(label, Close(state)!);
        _state = normal;
        return flow with { Breaks = breaks, Returns = returns, Throws = throws, LabeledBreaks = labeledBreaks, LabeledContinues = labeledContinues };
    }

    private void Serialize(Value value, Value? replacer = null, Value? holder = null, HashSet<Node>? seen = null)
    {
        // SerializeJSONProperty calls toJSON first, then calls a replacer with the property holder as this.
        // Preserve that receiver: a local holder may contain workflow-owned fields even when value is primitive.
        seen ??= new(ReferenceEqualityComparer.Instance);
        if (value.Objects.Count > 1)
        {
            var entry = _state.Copy(); State? exits = null;
            foreach (var item in value.Objects)
            {
                _state = entry.Copy();
                Serialize(value with { Objects = [item] }, replacer, holder, seen);
                exits ??= _state.Copy(); exits.Join(_state);
            }
            _state = exits ?? entry;
            return;
        }
        var replacement = value with { Objects = [] };
        foreach (var item in value.Objects)
        {
            var receiver = Value.Object(item);
            var toJson = Member(receiver, "toJSON");
            replacement = replacement.Union(toJson.Functions.Count > 0 ? Apply(toJson, [Value.Local], receiver) : receiver);
        }
        if (replacer?.Functions.Count > 0) replacement = Apply(replacer, [Value.Local, replacement], holder ?? Value.Local);
        var serializationEntry = _state.Copy(); State? serialized = null;
        foreach (var item in replacement.Objects)
        {
            _state = serializationEntry.Copy();
            // A callable may have ordinary fields, but JSON omits them after toJSON/replacer processing.
            if (replacement.Functions.Contains(item)) continue;
            if (!seen.Add(item)) continue;
            try { OwnProperties(Value.Object(item), (receiver, _, member) => Serialize(member, replacer, receiver, seen), elementsOnly: KnownArray(Value.Object(item))); }
            // JSON's cycle stack is ancestry, not a cache: a shared reference is visited again in its later position
            // after intervening getters/toJSON calls may have changed the aliases captured by its accessors.
            finally { seen.Remove(item); }
            serialized ??= _state.Copy(); serialized.Join(_state);
        }
        _state = serialized ?? serializationEntry;
    }

    private static bool IsReference(Value value) => !value.Session && (value.Objects.Count > 0 || value.Functions.Count > 0);
    // Record array-producing executions. Method-name syntax is insufficient: slice/concat may return a string,
    // and re-evaluating the source AST to classify a value would repeat user-visible effects.
    private bool KnownArray(Value value) => !value.Session && !value.MayBeString && value.Objects.Count > 0 && value.Objects.All(_arrays.Contains);

    private Value ArrayString(Value value, bool locale = false, IReadOnlyList<Value>? arguments = null)
    {
        var active = value.Objects.Where(item => _stringifyingArrays.Add(item)).ToImmutableHashSet();
        if (active.Count == 0) return Value.Local;
        var receiver = value with { Objects = active };
        try
        {
            if (!locale)
            {
                // Array.prototype.toString delegates to an own callable join override, otherwise native join.
                // A non-callable own join selects Object.prototype.toString and does not read the array elements.
                var join = Member(receiver, "join");
                if (join.Functions.Count > 0) return Apply(join, [], receiver);
                if (receiver.Objects.All(item => _state.Members.ContainsKey((item, "join")) || _state.Getters.ContainsKey((item, "join"))))
                { Member(receiver, "@@toStringTag"); return Value.Local; }
            }
            OwnProperties(receiver, (_, _, element) =>
            {
                if (locale)
                {
                    var method = Member(element, "toLocaleString");
                    if (method.Functions.Count > 0) { Coerce([Apply(method, arguments ?? [], element)], stringHint: true); return; }
                    if (KnownArray(element)) { ArrayString(element, locale: true, arguments); return; }
                }
                Coerce([element], stringHint: true);
            }, elementsOnly: true);
            return Value.Local;
        }
        finally { foreach (var item in active) _stringifyingArrays.Remove(item); }
    }

    private void Coerce(IEnumerable<Value> arguments, bool stringHint = false)
    {
        foreach (var value in arguments)
        {
            var exotic = Member(value, "@@toPrimitive");
            if (exotic.Functions.Count > 0) { Apply(exotic, [Value.Local], value); continue; }
            foreach (var name in stringHint ? new[] { "toString", "valueOf" } : new[] { "valueOf", "toString" })
            {
                var method = Member(value, name);
                var nativeArray = method.Functions.Count == 0 && name == "toString" && KnownArray(value);
                if (method.Functions.Count == 0 && !nativeArray) continue;
                var converted = nativeArray ? ArrayString(value) : Apply(method, [], value);
                if (!converted.Session && converted.Objects.Count == 0 && converted.Functions.Count == 0) break;
            }
        }
    }

    private void EvaluateKey(Node key) => Coerce([Evaluate(key)], stringHint: true);

    private Value FreshContainer(CallExpression call)
    {
        var result = Allocate(call);
        // Unknown native results can be mutable before their first owned-field store. Keep the root allocation
        // unique, while unmodeled nested containers share a finite summary rather than disappearing as scalars.
        _unknownMembers[call] = call.Callee;
        _unknownMembers[call.Callee] = call.Callee;
        _summaryObjects.Add(call.Callee);
        _state.Unordered.Add(call.Callee);
        return result with { MayBePrimitive = true };
    }

    private Value ParsedJson(CallExpression call)
    {
        if (call.Arguments.FirstOrDefault() is Literal { Value: string text })
        {
            try
            {
                using var json = JsonDocument.Parse(text);
                return Data(json.RootElement, call, root: true);
            }
            catch (JsonException) { /* Unknown/deep input retains a possible container; no script is evaluated. */ }
        }
        var unknown = FreshContainer(call);
        Put(unknown, "*", Value.Object(call.Callee));
        return unknown;

        Value Data(JsonElement element, Node identity, bool root = false)
        {
            if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                return element.ValueKind == JsonValueKind.String ? Value.Text : Value.Local;
            var value = Allocate(identity);
            if (!root) { _summaryObjects.Add(identity); _state.Unordered.Add(identity); }
            if (root && element.ValueKind == JsonValueKind.Object) _ordinaryObjects.Add(identity);
            if (root && element.ValueKind == JsonValueKind.Array) _arrays.Add(identity);
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject()) Put(value, property.Name, Data(property.Value, call.Callee));
            else
            {
                var index = 0;
                foreach (var item in element.EnumerateArray()) Put(value, (index++).ToString(System.Globalization.CultureInfo.InvariantCulture), Data(item, call.Callee));
            }
            return value;
        }
    }

    private Value Revive(Value value, Value function, Value holder, HashSet<Node>? seen = null)
    {
        seen ??= new(ReferenceEqualityComparer.Instance);
        foreach (var item in value.Objects)
        {
            if (!seen.Add(item)) continue;
            try
            {
                OwnProperties(Value.Object(item), (owner, name, child) => Put(owner, name, Revive(child, function, owner, seen)), elementsOnly: KnownArray(Value.Object(item)));
            }
            finally { seen.Remove(item); }
        }
        return Apply(function, [Value.Local, value], holder);
    }

    private Value Call(CallExpression call)
    {
        if (call.Callee is Identifier reader && Global(reader.Name))
        {
            if (reader.Name == "getVariable" && call.Arguments.Count == 1)
            {
                Evaluate(call.Arguments[0]);
                return call.Arguments[0] is Literal { Value: string name } ?
                    (isSessionVariable(Elsa3AmbientReferenceKind.Variable, name) ? Value.Owned : Value.Local) : Value.Owned;
            }
            if (call.Arguments.Count == 0 && generatedAccessor(reader.Name) is { } accessor)
                return isSessionVariable(Elsa3AmbientReferenceKind.GeneratedAccessor, accessor) ? Value.Owned : Value.Local;
        }
        Value receiver = Value.Local;
        MemberExpression? member = call.Callee as MemberExpression;
        Value function;
        if (member is null) function = Evaluate(call.Callee);
        else
        {
            receiver = Evaluate(member.Object);
            OptionalAccess(member.Optional, receiver);
            if (member.Computed) EvaluateKey(member.Property);
            function = Member(receiver, Name(member), member.Object is Super ? _receivers.LastOrDefault() : null);
            if (member.Object is Super) receiver = _receivers.LastOrDefault() ?? Value.Local;
        }
        OptionalAccess(call.Optional, function);
        var arguments = Arguments(call.Arguments, function);
        if (call.Callee is Super)
        {
            var actualThis = _receivers.LastOrDefault() ?? Value.Local;
            var result = Apply(function, arguments, actualThis);
            if (_constructors.LastOrDefault() is { Type.Derived: true } frame)
            {
                frame.Result = frame.Result.Union(result);
                frame.Receiver = frame.Receiver.Union(result);
                if (_receivers.Count > 0) _receivers[^1] = frame.Receiver;
                InitializeFields(frame);
                return frame.Receiver;
            }
            return actualThis.Union(result);
        }
        if (function.Functions.Count > 0)
            return Apply(function, arguments, receiver);
        if (call.Callee is Identifier host && Global(host.Name))
        {
            if (host.Name == "toJson")
            {
                if (arguments.Length > 0) Serialize(arguments[0]);
                return Value.Local;
            }
            if (JavaScriptBuiltInFunctions.Names.Contains(host.Name))
            {
                // Faithful host helpers materialize all arguments as JSON (JavaScriptHostFunctions.Argument),
                // then parse string/number data in .NET. They do not invoke a JS toString/valueOf hook, but argument
                // materialization can execute getters/toJSON and must retain those executable boundaries.
                foreach (var argument in arguments) Serialize(argument);
                return Value.Local;
            }
        }
        var method = member is null ? null : Name(member);
        if (member?.Object is Identifier global && Global(global.Name))
        {
            if (global.Name == "Reflect" && method == "apply" && arguments.Length >= 3)
            {
                var width = arguments[0].Functions.OfType<IFunction>().Select(target => target.Params.Count).DefaultIfEmpty(1).Max();
                return Apply(arguments[0], ArrayArguments(arguments[2], width, out _), arguments[1]);
            }
            if (global.Name == "Reflect" && method == "get" && arguments.Length >= 2)
            {
                Coerce(arguments.Skip(1).Take(1), stringHint: true);
                return Member(arguments[0], call.Arguments.Count >= 2 ? Key(call.Arguments[1]) : "*", arguments.Length > 2 ? arguments[2] : null);
            }
            if (global.Name == "Object" && method == "getOwnPropertyDescriptor" && arguments.Length >= 2)
            {
                var descriptor = Allocate(call);
                _ordinaryObjects.Add(call);
                Coerce(arguments.Skip(1).Take(1), stringHint: true);
                var key = call.Arguments.Count >= 2 ? Key(call.Arguments[1]) : "*";
                Put(descriptor, "value", StoredMember(arguments[0], key));
                foreach (var item in arguments[0].Objects)
                {
                    if (_state.Getters.TryGetValue((item, key), out var getter)) Put(descriptor, "get", getter);
                    if (_state.Setters.TryGetValue((item, key), out var setter)) Put(descriptor, "set", setter);
                }
                return descriptor;
            }
            if (global.Name == "Object" && method == "defineProperty" && arguments.Length >= 3)
            {
                Coerce(arguments.Skip(1).Take(1), stringHint: true);
                DefineProperty(arguments[0], Key(call.Arguments[1]), arguments[2], call.Arguments[2]);
                return arguments[0];
            }
            if (global.Name == "Object" && method == "create")
            {
                var created = Allocate(call);
                _ordinaryObjects.Add(call);
                _state.Prototypes[call] = arguments.FirstOrDefault() ?? Value.Local;
                if (arguments.Length > 1)
                    OwnProperties(arguments[1], (_, name, descriptor) =>
                    {
                        var syntax = call.Arguments[1] is ObjectExpression map ? map.Properties.OfType<Property>().FirstOrDefault(item => PropertyName(item) == name)?.Value : null;
                        DefineProperty(created, name, descriptor, syntax ?? call);
                    });
                return created;
            }
            if (global.Name is "Object" or "Reflect" && method == "getPrototypeOf" && arguments.Length > 0)
            {
                var prototype = arguments[0].Objects.Aggregate(Value.Local, (result, item) => result.Union(_state.Prototypes.GetValueOrDefault(item, Value.Local)));
                return prototype.Objects.Count > 0 || prototype.Session ? prototype : FreshContainer(call);
            }
            if (global.Name == "Object" && method == "assign" && arguments.Length > 0)
            {
                if (arguments[0].Session && call.Arguments.Skip(1).Any(source => !EmptySource(source))) _writes = true;
                foreach (var source in arguments.Skip(1)) CopyMembers(arguments[0], source, assign: true);
                return arguments[0];
            }
            if (arguments.Length > 0 && arguments[0].Session &&
                (global.Name == "Object" && method is "defineProperty" or "defineProperties" or "setPrototypeOf" ||
                 global.Name == "Reflect" && method is "set" or "deleteProperty" or "defineProperty" or "setPrototypeOf")) _writes = true;
            if (global.Name == "JSON" && method == "stringify")
            {
                if (arguments.Length > 0)
                {
                    var holder = Allocate(call);
                    Put(holder, "", arguments[0]);
                    Serialize(arguments[0], arguments.Length > 1 ? arguments[1] : null, holder);
                }
                return Value.Local;
            }
            if (global.Name == "JSON" && method == "parse")
            {
                Coerce(arguments.Take(1), stringHint: true);
                var parsed = ParsedJson(call);
                if (arguments.Length <= 1 || arguments[1].Functions.Count == 0) return parsed;
                var holder = Allocate(call.Arguments.FirstOrDefault() ?? call.Callee);
                Put(holder, "", parsed);
                return Revive(parsed, arguments[1], holder);
            }
            if (global.Name == "Object" && method is "values" or "entries" && arguments.Length > 0)
            {
                var listed = Allocate(call);
                _arrays.Add(call);
                var value = arguments[0].Session ? Value.Owned : Value.Local;
                OwnProperties(arguments[0], (_, _, memberValue) => value = value.Union(memberValue));
                if (method == "entries")
                {
                    // Entry rows are new arrays; only their value column shares the source value.
                    var row = Allocate(call.Callee);
                    _arrays.Add(call.Callee);
                    Put(row, "0", Value.Local); Put(row, "1", value);
                    value = row;
                }
                Put(listed, "*", value);
                return listed;
            }
            if (global.Name == "Array" && method == "from" && arguments.Length > 0)
            {
                var elements = Iterated(arguments[0]);
                var mapped = Allocate(call);
                _arrays.Add(call);
                Put(mapped, "*", arguments.Length <= 1 || arguments[1].Functions.Count == 0 ? elements :
                    Repeated(_ => Apply(arguments[1], [elements, Value.Local], arguments.Length > 2 ? arguments[2] : Value.Local),
                        maximumCalls: LiteralArrayBound(call.Arguments[0]),
                        minimumCalls: LiteralArrayBound(call.Arguments[0]) ?? 0));
                return mapped;
            }
            // Only known read-only intrinsics bypass the opaque-call guard. Reflection and callbacks can return or
            // execute workflow-owned references; an unfamiliar native entry point is not assumed harmless.
            if (global.Name == "Math" && NumericMathArity(method) is not null || global.Name == "Object" && method is "keys" or "hasOwn" or "is" or "isExtensible" or "isFrozen" or "isSealed" or "getPrototypeOf" ||
                global.Name == "Reflect" && method is "has" or "ownKeys" or "isExtensible" or "getPrototypeOf" ||
                global.Name == "Number" && method is "isFinite" or "isInteger" or "isNaN" or "isSafeInteger" or "parseFloat" or "parseInt" ||
                global.Name == "String" && method is "fromCharCode" or "fromCodePoint" || global.Name == "Date" && method is "now" or "parse" or "UTC")
            {
                if (global.Name == "Math") Coerce(arguments.Take(NumericMathArity(method)!.Value));
                else if (global.Name == "String" || global.Name == "Date" && method == "UTC") Coerce(arguments);
                else if (global.Name == "Date" && method == "parse" || global.Name == "Number" && method is "parseFloat" or "parseInt")
                {
                    Coerce(arguments.Take(1), stringHint: true);
                    if (method == "parseInt") Coerce(arguments.Skip(1).Take(1));
                }
                else if (global.Name == "Object" && method == "hasOwn" || global.Name == "Reflect" && method == "has")
                    Coerce(arguments.Skip(1).Take(1), stringHint: true);
                return Value.Local;
            }
        }
        CoerceNativeParameters(method, receiver, arguments);
        if (KnownArray(receiver) && method is "toString" or "toLocaleString") return ArrayString(receiver, method == "toLocaleString", arguments);
        if (method is "push" or "unshift" or "splice" or "copyWithin") _writes |= receiver.Session && arguments.Length > 0;
        else if (method is "pop" or "shift" or "sort" or "reverse" or "fill") _writes |= receiver.Session;
        if (method is "push" or "unshift" or "splice" or "fill")
        {
            var inserted = method == "splice" ? arguments.Skip(2) : method == "fill" ? arguments.Take(1) : arguments;
            var members = Member(receiver, "*");
            foreach (var value in inserted) members = members.Union(value);
            Put(receiver, "*", members);
        }
        var callbackResult = Value.Local;
        // A direct literal cannot acquire an external alias before this call. Array iteration snapshots its length;
        // mutations from the callback cannot increase that maximum. Other receivers keep conservative repetition.
        var callbackBound = call.Callee is MemberExpression arrayCall ? LiteralArrayBound(arrayCall.Object) : null;
        if (method is "sort" or "toSorted")
        {
            Repeated(_ =>
            {
                if (arguments.FirstOrDefault()?.Functions.Count > 0)
                    Coerce([Apply(arguments[0], [Member(receiver, "*"), Member(receiver, "*")], Value.Local)]);
                else Coerce([Member(receiver, "*")], stringHint: true);
                return Value.Local;
            }, maximumCalls: callbackBound is <= 1 ? 0 : null);
        }
        if (method is "map" or "filter" or "find" or "findIndex" or "findLast" or "findLastIndex" or "some" or "every" or "forEach" or "flatMap" or "reduce" or "reduceRight")
            if (arguments.FirstOrDefault()?.Functions.Count > 0)
            {
                var maximum = method is "reduce" or "reduceRight" && arguments.Length <= 1 && callbackBound is { } count
                    ? Math.Max(0, count - 1) : callbackBound;
                var minimum = maximum is > 0 && call.Callee is MemberExpression { Object: ArrayExpression literalArray } &&
                    literalArray.Elements.All(item => item is not null) ? 1 : 0;
                callbackResult = Repeated(accumulator =>
                    {
                        var returned = Apply(arguments[0], method is "reduce" or "reduceRight"
                            ? [accumulator, Member(receiver, "*"), Value.Local, receiver]
                            : [Member(receiver, "*"), Value.Local, receiver],
                            method is not ("reduce" or "reduceRight") && arguments.Length > 1 ? arguments[1] : Value.Local);
                        return method == "flatMap" ? Flattened(returned) : returned;
                    },
                    method is "reduce" or "reduceRight" ? arguments.Length > 1 ? arguments[1] : Member(receiver, "*") : null,
                    maximum, minimum);
            }
        if (method is "replace" or "replaceAll")
        {
            Coerce([receiver], stringHint: true);
            Coerce(arguments.Take(1), stringHint: true);
            if (arguments.Length > 1 && arguments[1].Functions.Count > 0)
                Repeated(_ =>
                {
                    Coerce([Apply(arguments[1], [Value.Local, Value.Local, Value.Local], Value.Local)], stringHint: true);
                    return Value.Local;
                });
            else if (arguments.Length > 1) Coerce([arguments[1]], stringHint: true);
            return Value.Local;
        }
        if (method is "reduce" or "reduceRight") return arguments.FirstOrDefault()?.Functions.Count > 0
            ? callbackResult : arguments.Length > 1 ? arguments[1] : Member(receiver, "*");
        if (method is "map" or "flatMap")
        {
            var mapped = Value.Object(call);
            _arrays.Add(call);
            Put(mapped, "*", callbackResult);
            return mapped;
        }
        if (method == "concat")
        {
            var array = KnownArray(receiver);
            if (!array) { Coerce([receiver], stringHint: true); Coerce(arguments, stringHint: true); }
            if (receiver.MayBeString && receiver.Objects.Count == 0 && !receiver.Session) return Value.Text;
            var concatenated = LocalCopy(call, receiver, array);
            foreach (var argument in arguments) Put(concatenated, "*", Member(concatenated, "*").Union(argument).Union(Member(argument, "*")));
            return concatenated with { MayBeString = !array };
        }
        if (method is "pop" or "shift") return Member(receiver, "*").Union(Value.Primitive);
        if (method is "sort" or "reverse" or "fill" or "copyWithin") return receiver;
        if (method is "slice" or "splice" or "filter" or "toReversed" or "toSorted" or "toSpliced" or "with")
        {
            if (method == "slice" && receiver.MayBeString && receiver.Objects.Count == 0 && !receiver.Session) return Value.Text;
            var array = method != "slice" || KnownArray(receiver);
            return LocalCopy(call, receiver, array) with { MayBeString = !array };
        }
        if (method is "at" or "find" or "findLast") return Member(receiver, "*").Union(Value.Primitive);
        if (method == "valueOf")
        {
            // Generic Object/Array valueOf is identity. Known boxed primitive implementations return a scalar.
            var result = receiver with { Objects = receiver.Objects.Except(_primitiveBoxes.Keys).ToImmutableHashSet() };
            if (receiver.Objects.Any(item => _primitiveBoxes.GetValueOrDefault(item) == "String")) result = result.Union(Value.Text);
            return result;
        }
        if (method is "push" or "unshift" or "splice" or "copyWithin" or "pop" or "shift" or "sort" or "reverse" or "fill" or
            "join" or "includes" or "indexOf" or "lastIndexOf" or "findIndex" or "findLastIndex" or "some" or "every" or "forEach" or "reduce" or "reduceRight" or
            "toString" or "toLocaleString" or "valueOf" or "hasOwnProperty" or "isPrototypeOf" or "propertyIsEnumerable" or
            "charAt" or "charCodeAt" or "codePointAt" or "substring" or "substr" or "split" or "replace" or "replaceAll" or "trim" or "toUpperCase" or "toLowerCase" or "startsWith" or "endsWith") return Value.Local;
        if (call.Callee is Identifier pure && Global(pure.Name) && pure.Name is "String" or "Number" or "Boolean" or "parseInt" or "parseFloat" or "isNaN" or "isFinite")
        {
            if (pure.Name != "Boolean") Coerce(arguments.Take(1), stringHint: pure.Name is "String" or "parseInt" or "parseFloat");
            if (pure.Name == "parseInt") Coerce(arguments.Skip(1).Take(1));
            return pure.Name == "String" ? Value.Text : Value.Local;
        }
        if (call.Callee is Identifier { Name: "Object" } && Global("Object"))
        {
            _ordinaryObjects.Add(call);
            if (arguments.FirstOrDefault() is { } value && (value.Session || value.Objects.Count > 0 || value.Functions.Count > 0))
                return value.MayBePrimitive ? Allocate(call).Union(value) : value;
            return Allocate(call);
        }
        // An opaque helper receiving a workflow-owned object (including inside a local wrapper or closure) could
        // change it. Unlike a known local helper, its read-only behavior cannot be established here.
        _writes |= OpaqueOwnership(receiver) || arguments.Any(argument => OpaqueOwnership(argument));
        return FreshContainer(call);
    }

    private static int? LiteralArrayBound(Node expression) => expression is ArrayExpression array &&
        array.Elements.All(item => item is not SpreadElement) ? array.Elements.Count : null;

    private void OptionalAccess(bool optional, Value receiver)
    {
        // The receiver/callee has already executed; only subsequent keys/arguments may be skipped. Preserve
        // that exact checkpoint, including its mandatory effects, rather than the state before the whole chain.
        if (optional && receiver.MayBePrimitive && _optionalChains.Count > 0)
            _optionalChains[^1] = Join(_optionalChains[^1], _state);
    }

    private Value Flattened(Value source)
    {
        // flatMap reads returned array indices immediately after each callback, including non-enumerable indices.
        // Ordinary object/function returns are not flattened. Unknown native containers may be arrays or objects.
        var possibleArrays = source.Objects.Where(item => _arrays.Contains(item) ||
            _unprovenConstructions.Contains(item) || item is not (ObjectExpression or IFunction) &&
            !_ordinaryObjects.Contains(item) && !_primitiveBoxes.ContainsKey(item)).ToImmutableHashSet();
        var result = source with { Objects = source.Objects.Where(item => !_arrays.Contains(item) ||
            _ordinaryObjects.Contains(item) || _unprovenConstructions.Contains(item)).ToImmutableHashSet() };
        OwnProperties(source with { Session = false, Objects = possibleArrays, Functions = [] },
            (_, _, element) => result = result.Union(element), elementsOnly: true);
        foreach (var item in possibleArrays)
        {
            // Custom prototypes are an opaque indexed-access boundary: retain their possible effects and aliases.
            var before = _state.Copy();
            result = result.Union(Member(_state.Prototypes.GetValueOrDefault(item, Value.Local), "*"));
            _state.Join(before);
        }
        return result;
    }

    private Value Repeated(Func<Value, Value> invoke, Value? initial = null, int? maximumCalls = null, int minimumCalls = 0)
    {
        // Native iteration can invoke the same callback more than once. Preserve captured-state changes and feed
        // possible reducer results back into its accumulator until the finite ownership graph stops growing.
        var result = initial ?? Value.Local;
        if (maximumCalls is { } bound)
        {
            // A proven bound must not invent another callback. Keep sequential strong updates for these calls,
            // while retaining every possible returned value and exit prefix. Early-stopping methods, holes and
            // callback deletions can all perform fewer calls than the maximum.
            var accumulator = result;
            if (minimumCalls > 0) result = Value.Local;
            State? exits = minimumCalls == 0 ? _state.Copy() : null;
            for (var index = 0; index < bound && !_writes; index++)
            {
                accumulator = invoke(accumulator);
                result = result.Union(accumulator);
                if (index + 1 >= minimumCalls) exits = Join(exits, _state);
            }
            _state = exits ?? _state;
            return result;
        }
        while (!_writes)
        {
            var before = _state.Copy();
            var next = result.Union(invoke(result));
            _state.Join(before);
            if (_state.Same(before) && next.Same(result)) return next;
            result = next;
        }
        return result;
    }

    private static int? NumericMathArity(string? method) => method switch
    {
        "random" => 0,
        "atan2" or "imul" or "pow" => 2,
        "hypot" or "min" or "max" => int.MaxValue,
        "abs" or "acos" or "acosh" or "asin" or "asinh" or "atan" or "atanh" or "cbrt" or "ceil" or "clz32" or "cos" or "cosh" or
            "exp" or "expm1" or "floor" or "fround" or "f16round" or "log" or "log10" or "log1p" or "log2" or "round" or "sign" or "sin" or "sinh" or "sqrt" or "tan" or "tanh" or "trunc" => 1,
        _ => null
    };

    private void CoerceNativeParameters(string? method, Value receiver, IReadOnlyList<Value> arguments)
    {
        // These conversions follow the individual ECMAScript algorithms, rather than assuming every argument of
        // a read-only method is inert (or, conversely, coercing values that are only compared by identity).
        // See tc39.es/ecma262: parseInt, String.prototype.charAt, Array.prototype.slice/join, CompareArrayElements.
        if (method is "charAt" or "charCodeAt" or "codePointAt" or "substring" or "substr" or "trim" or "toUpperCase" or "toLowerCase" or "startsWith" or "endsWith" or "split")
            Coerce([receiver], stringHint: true);
        if (method is "charAt" or "charCodeAt" or "codePointAt" or "at" or "with") Coerce(arguments.Take(1));
        else if (method is "slice" or "splice" or "toSpliced" or "substring" or "substr") Coerce(arguments.Take(2));
        else if (method == "fill") Coerce(arguments.Skip(1).Take(2));
        else if (method == "copyWithin") Coerce(arguments.Take(3));
        else if (method is "includes" or "indexOf" or "lastIndexOf")
        {
            // The array methods compare the searched value without coercion; the string methods use ToString.
            // Unknown receiver types may be strings, so only a proven local array bypasses that conversion.
            if (!KnownArray(receiver)) { Coerce([receiver], stringHint: true); Coerce(arguments.Take(1), stringHint: true); }
            Coerce(arguments.Skip(1).Take(1));
        }
        else if (method is "startsWith" or "endsWith")
        {
            Coerce(arguments.Take(1), stringHint: true);
            Coerce(arguments.Skip(1).Take(1));
        }
        else if (method == "split")
        {
            Coerce(arguments.Take(1), stringHint: true);
            Coerce(arguments.Skip(1).Take(1));
        }
        else if (method == "join")
        {
            Coerce(arguments.Take(1), stringHint: true);
            OwnProperties(receiver, (_, _, element) => Coerce([element], stringHint: true), elementsOnly: true);
        }
        else if (method is "hasOwnProperty" or "propertyIsEnumerable") Coerce(arguments.Take(1), stringHint: true);
    }

    private bool EmptySource(Node source) => source switch
    {
        Literal literal => literal.Value is not string text || text.Length == 0,
        ObjectExpression value => value.Properties.Count == 0,
        ArrayExpression value => value.Elements.All(item => item is null),
        Identifier { Name: "undefined" or "NaN" or "Infinity" } name => Global(name.Name),
        _ => false
    };
    private string Name(MemberExpression member) => !member.Computed && member.Property is Identifier name ? name.Name : Key(member.Property);
    private string PropertyName(Property property) => !property.Computed && property.Key is Identifier name ? name.Name : Key(property.Key);
    private string Key(Node node) => node is Literal literal ? Convert.ToString(literal.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "*" :
        node is MemberExpression { Computed: false, Object: Identifier { Name: "Symbol" }, Property: Identifier symbol } && Global("Symbol") ? "@@" + symbol.Name : "*";
}
