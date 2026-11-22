using System.Collections.Immutable;
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
        public static readonly Value Local = new(false, [], []);
        public static readonly Value Owned = new(true, [], []);
        public static Value Object(Node node) => new(false, [node], []);
        public static Value Function(Node node) => new(false, [], [node]);
        public Value Union(Value other) => new(Session || other.Session, Objects.Union(other.Objects), Functions.Union(other.Functions)) { UnknownSpread = UnknownSpread || other.UnknownSpread };
        public bool Same(Value other) => Session == other.Session && UnknownSpread == other.UnknownSpread && Objects.SetEquals(other.Objects) && Functions.SetEquals(other.Functions);
    }

    private sealed class State
    {
        public Dictionary<Binding, Value> Bindings { get; } = [];
        public Dictionary<(Node Object, string Member), Value> Members { get; } = [];
        public Dictionary<Node, ImmutableList<string>> PropertyOrder { get; } = new(ReferenceEqualityComparer.Instance);
        public HashSet<Node> Unordered { get; } = new(ReferenceEqualityComparer.Instance);
        public State Copy()
        {
            var copy = new State();
            foreach (var item in Bindings) copy.Bindings[item.Key] = item.Value;
            foreach (var item in Members) copy.Members[item.Key] = item.Value;
            foreach (var item in PropertyOrder) copy.PropertyOrder[item.Key] = item.Value;
            copy.Unordered.UnionWith(Unordered);
            return copy;
        }
        public void Join(State other)
        {
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
        }
        public bool Same(State other) => Bindings.Count == other.Bindings.Count && Members.Count == other.Members.Count &&
            Bindings.All(item => other.Bindings.TryGetValue(item.Key, out var value) && item.Value.Same(value)) &&
            Members.All(item => other.Members.TryGetValue(item.Key, out var value) && item.Value.Same(value)) &&
            PropertyOrder.Count == other.PropertyOrder.Count && PropertyOrder.All(item => other.PropertyOrder.TryGetValue(item.Key, out var order) && item.Value.SequenceEqual(order)) &&
            Unordered.SetEquals(other.Unordered);
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
    private sealed record Closure(HashSet<string>[] Scopes, Dictionary<Binding, Value> Captured, Value This, Value Arguments);
    private sealed record ClassInfo(ClassBody Body, Value Super);
    private readonly List<HashSet<string>> _scopes = [];
    private readonly Dictionary<Node, Closure> _closures = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> _calls = new(ReferenceEqualityComparer.Instance);
    private readonly List<State> _exceptionStates = [];
    private readonly List<Value> _receivers = [];
    private readonly List<Value> _arguments = [];
    private readonly Dictionary<(Node Object, string Member), Value> _getters = [];
    private readonly Dictionary<(Node Object, string Member), Value> _setters = [];
    private readonly HashSet<Node> _allocated = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> _summaryObjects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Value> _intrinsics = new(StringComparer.Ordinal);
    private readonly List<(string Name, Node Target)> _labels = [];
    private readonly Dictionary<Node, ClassInfo> _classes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Node> _stringifyingArrays = new(ReferenceEqualityComparer.Instance);
    private State _state = new();
    private bool _writes;

    public bool MayWrite(Script script)
    {
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

    private Flow Execute(Node node)
    {
        if (_writes) return Flow.Next;
        // A definition is a value; its body runs only at a call site with that call's actual parameters.
        if (node is IFunction) return Flow.Next;
        var scope = scopeOf(node);
        if (scope is not null) EnterScope(node, scope);
        try { return ExecuteCore(node); }
        finally
        {
            foreach (var exceptional in _exceptionStates) exceptional.Join(_state);
            if (scope is not null) _scopes.RemoveAt(_scopes.Count - 1);
        }
    }

    private void EnterScope(Node node, HashSet<string> scope)
    {
        _scopes.Add(scope);
        foreach (var name in scope) _state.Bindings[new Binding(scope, name)] = Value.Local;
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
                foreach (var item in declaration.Declarations) Bind(item.Id, Evaluate(item.Init));
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
        if (node is null || _writes) return Value.Local;
        Value result;
        switch (node)
        {
            case Identifier name:
                result = Global(name.Name) ? name.Name == "variables" ? Value.Owned : name.Name == "arguments" ? _arguments.LastOrDefault() ?? Value.Local : NativeNamespace(name.Name) ? Intrinsic(name) : Value.Local : Read(name.Name);
                break;
            case ThisExpression: result = _receivers.Count > 0 ? _receivers[^1] : Value.Local; break;
            case Literal: result = Value.Local; break;
            case IFunction: result = FunctionValue(node); break;
            case ClassExpression type: result = ClassValue(type, type.Id, type.Body, type.SuperClass); break;
            case MemberExpression member:
                var memberReceiver = Evaluate(member.Object);
                if (member.Computed) EvaluateKey(member.Property);
                result = Member(memberReceiver, Name(member));
                break;
            case ChainExpression chain: result = Evaluate(chain.Expression); break;
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
                    var existing = logicalReceiver is null ? Evaluate(assignment.Left) : Member(logicalReceiver, Name((MemberExpression)assignment.Left));
                    var beforeAssignment = _state.Copy();
                    result = Evaluate(assignment.Right);
                    if (logicalReceiver is null) Bind(assignment.Left, result);
                    else AssignMember(logicalReceiver, Name((MemberExpression)assignment.Left), result);
                    _state.Join(beforeAssignment);
                    result = existing.Union(result);
                    break;
                }
                if (assignment.Left is MemberExpression target)
                {
                    // JavaScript evaluates the reference before the RHS can reassign the receiver's local binding.
                    var targetValue = Evaluate(target.Object);
                    if (target.Computed) EvaluateKey(target.Property);
                    if (assignment.Operator != Operator.Assignment) Coerce([Member(targetValue, Name(target))]);
                    result = Evaluate(assignment.Right);
                    if (assignment.Operator != Operator.Assignment) Coerce([result]);
                    AssignMember(targetValue, Name(target), assignment.Operator == Operator.Assignment ? result : Value.Local);
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
                    if (updated.Computed) EvaluateKey(updated.Property);
                    Coerce([Member(receiver, Name(updated))]);
                    AssignMember(receiver, Name(updated), Value.Local);
                }
                else { Coerce([Evaluate(update.Argument)]); Bind(update.Argument, Value.Local); }
                result = Value.Local; break;
            case UnaryExpression unary:
                if (unary.Operator == Operator.Delete && unary.Argument is MemberExpression deleted)
                {
                    var receiver = Evaluate(deleted.Object);
                    if (deleted.Computed) EvaluateKey(deleted.Property);
                    _writes |= receiver.Session;
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
                result = logical.Operator == Operator.LogicalAnd ? last : first.Union(last); break;
            case ObjectExpression value:
                result = Allocate(node);
                foreach (var property in value.Properties)
                {
                    if (property is Property item)
                    {
                        if (item.Computed) EvaluateKey(item.Key);
                        var propertyValue = Evaluate(item.Value);
                        if (item.Kind is PropertyKind.Get or PropertyKind.Set)
                        {
                            RegisterProperty(node, PropertyName(item));
                            (item.Kind == PropertyKind.Get ? _getters : _setters)[(node, PropertyName(item))] = propertyValue;
                        }
                        else Put(result, PropertyName(item), propertyValue);
                    }
                    else if (property is SpreadElement spread) CopyMembers(result, Evaluate(spread.Argument));
                }
                break;
            case ArrayExpression array:
                result = Allocate(node);
                for (var index = 0; index < array.Elements.Count; index++)
                    if (array.Elements[index] is { } item)
                        Put(result, item is SpreadElement ? "*" : index.ToString(System.Globalization.CultureInfo.InvariantCulture), item is SpreadElement spread ? Iterated(Evaluate(spread.Argument)) : Evaluate(item));
                break;
            case CallExpression call: result = Call(call); break;
            case TemplateLiteral template:
                // Untagged substitutions use ToString; tagged template arguments do not.
                foreach (var expression in template.Expressions) Coerce([Evaluate(expression)], stringHint: true);
                result = Value.Local; break;
            case TaggedTemplateExpression tagged:
                result = Apply(Evaluate(tagged.Tag), [Value.Object(tagged.Quasi), .. tagged.Quasi.Expressions.Select(Evaluate)], Value.Local);
                break;
            case NewExpression construction:
                var constructor = Evaluate(construction.Callee);
                var constructorArguments = Arguments(construction.Arguments, constructor);
                result = Allocate(node);
                if (constructor.Functions.Count > 0)
                {
                    var explicitResult = Apply(constructor, constructorArguments, result);
                    result = result.Union(explicitResult);
                }
                else if (construction.Callee is Identifier native && Global(native.Name) && native.Name is "Date" or "RegExp" or "String" or "Number" or "Boolean")
                {
                    if (native.Name is "String" or "RegExp") Coerce(native.Name == "String" ? constructorArguments.Take(1) : constructorArguments.Take(2), stringHint: true);
                    else if (native.Name == "Number") Coerce(constructorArguments.Take(1));
                    else if (native.Name == "Date") Coerce(constructorArguments);
                    // Boolean conversion tests truthiness and never calls object conversion hooks.
                }
                else if (construction.Callee is Identifier { Name: "Object" } && Global("Object")) result = constructorArguments.FirstOrDefault() ?? result;
                else if (construction.Callee is Identifier { Name: "Array" } && Global("Array"))
                    foreach (var argument in constructorArguments) Put(result, "*", Member(result, "*").Union(argument));
                else if (construction.Callee is Identifier collection && Global(collection.Name) && collection.Name is "Set" or "Map" or "WeakSet" or "WeakMap")
                {
                    if (constructorArguments.Length > 0) Put(result, "*", Iterated(constructorArguments[0]));
                }
                else _writes |= constructorArguments.Any(argument => OpaqueOwnership(argument));
                break;
            default:
                foreach (var child in node.ChildNodes)
                    if (child is Statement) Execute(child); else Evaluate(child);
                result = Value.Local; break;
        }
        foreach (var exceptional in _exceptionStates) exceptional.Join(_state);
        return result;
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
                            var remainder = LocalCopy(pattern, value);
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
        AssignMember(receiver, Name(member), value);
    }
    private void AssignMember(Value receiver, string name, Value value)
    {
        if (receiver.Session) _writes = true;
        if (receiver.Objects.Any(item => _intrinsics.Values.Any(intrinsic => intrinsic.Objects.Contains(item)))) _writes = true;
        foreach (var item in receiver.Objects)
            foreach (var (key, setter) in _setters.Where(entry => ReferenceEquals(entry.Key.Object, item) && (name == "*" || entry.Key.Member == name || entry.Key.Member == "*")).ToArray())
                Apply(setter, [value], receiver);
        Put(receiver, name, value);
    }
    private Value Member(Value receiver, string name, Value? getterReceiver = null)
    {
        var result = StoredMember(receiver, name);
        foreach (var item in receiver.Objects)
        {
            foreach (var (key, getter) in _getters.Where(entry => ReferenceEquals(entry.Key.Object, item) && (name == "*" || entry.Key.Member == name || entry.Key.Member == "*")).ToArray())
                result = result.Union(Apply(getter, [], getterReceiver ?? receiver));
        }
        return result;
    }
    private Value StoredMember(Value receiver, string name)
    {
        var result = receiver.Session ? Value.Owned : Value.Local;
        if (receiver.Objects.Any(item => _intrinsics.Values.Any(intrinsic => intrinsic.Objects.Contains(item)))) result = result.Union(receiver);
        foreach (var item in receiver.Objects)
        {
            result = result.Union(_state.Members.GetValueOrDefault((item, name), Value.Local));
            result = result.Union(_state.Members.GetValueOrDefault((item, "*"), Value.Local));
            if (name == "*")
                foreach (var (key, value) in _state.Members.Where(entry => ReferenceEquals(entry.Key.Object, item))) result = result.Union(value);
        }
        return result;
    }
    private void Put(Value receiver, string name, Value value)
    {
        foreach (var item in receiver.Objects)
        {
            RegisterProperty(item, name);
            _state.Members[(item, name)] = receiver.Objects.Count != 1 || name == "*" || _summaryObjects.Contains(item)
                ? _state.Members.GetValueOrDefault((item, name), Value.Local).Union(value) : value;
        }
    }

    private void RegisterProperty(Node item, string name)
    {
        var order = _state.PropertyOrder.GetValueOrDefault(item, []);
        if (!order.Contains(name)) _state.PropertyOrder[item] = order.Add(name);
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
                .Where(name => (symbols || !name.StartsWith("@@", StringComparison.Ordinal)) && (!elementsOnly || name == "*" || ArrayIndex(name) is not null))
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
    private Value LocalCopy(Node identity, Value source)
    {
        var copy = Allocate(identity);
        CopyMembers(copy, source);
        return copy;
    }
    private void CopyMembers(Value target, Value source, bool assign = false)
    {
        if (source.Session) Put(target, "*", Value.Owned);
        OwnProperties(source, (_, name, value) => { if (assign) AssignMember(target, name, value); else Put(target, name, value); }, symbols: true);
    }
    private bool ContainsSession(Value value, HashSet<Node>? seen = null)
    {
        if (value.Session) return true;
        seen ??= new(ReferenceEqualityComparer.Instance);
        return value.Objects.Any(item => seen.Add(item) && _state.Members.Where(entry => ReferenceEquals(entry.Key.Object, item)).Any(entry => ContainsSession(entry.Value, seen))) ||
               value.Functions.Any(item => seen.Add(item) && _closures.TryGetValue(item, out var closure) && closure.Captured.Values.Any(captured => ContainsSession(captured, seen)));
    }

    private Value FunctionValue(Node function)
    {
        if (!_closures.ContainsKey(function))
            _closures[function] = new Closure(_scopes.ToArray(), new Dictionary<Binding, Value>(_state.Bindings), _receivers.LastOrDefault() ?? Value.Local, _arguments.LastOrDefault() ?? Value.Local);
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
            _classes[node] = new ClassInfo(body, Evaluate(superClass));
            _receivers.Add(value);
            try
            {
                foreach (var element in body.Body)
                {
                    if (element is MethodDefinition method)
                    {
                        if (method.Computed) EvaluateKey(method.Key);
                        var function = FunctionValue(method.Value);
                        if (method.Static) DefineMethod(node, method, function);
                    }
                    else if (element is PropertyDefinition field)
                    {
                        if (field.Computed) EvaluateKey(field.Key);
                        if (field.Static) Put(value, ClassMemberName(field.Key, field.Computed), Evaluate(field.Value));
                    }
                    else if (element is StaticBlock block) Execute(block);
                }
            }
            finally { _receivers.RemoveAt(_receivers.Count - 1); }
            return value;
        }
        finally { if (local is not null) _scopes.RemoveAt(_scopes.Count - 1); }
    }

    private void DefineMethod(Node target, MethodDefinition method, Value function)
    {
        var name = ClassMemberName(method.Key, method.Computed);
        if (method.Kind is PropertyKind.Get or PropertyKind.Set)
        {
            RegisterProperty(target, name);
            (method.Kind == PropertyKind.Get ? _getters : _setters)[(target, name)] = function;
        }
        else Put(Value.Object(target), name, function);
    }

    private Value ConstructClass(Node node, ClassInfo type, IReadOnlyList<Value> arguments, Value receiver)
    {
        if (!_closures.TryGetValue(node, out var closure)) return Value.Local;
        if (!_calls.Add(node)) { _writes |= arguments.Any(argument => OpaqueOwnership(argument)); return Value.Local; }
        var callerScopes = _scopes.ToArray();
        _scopes.Clear(); _scopes.AddRange(closure.Scopes);
        foreach (var (binding, value) in closure.Captured) _state.Bindings.TryAdd(binding, value);
        _receivers.Add(receiver);
        try
        {
            if (type.Super.Functions.Count > 0) Apply(type.Super, arguments, receiver);
            else if (type.Super.Objects.Count > 0) _writes |= arguments.Any(argument => OpaqueOwnership(argument));
            foreach (var element in type.Body.Body)
            {
                if (element is MethodDefinition { Static: false } method)
                    foreach (var target in receiver.Objects) DefineMethod(target, method, FunctionValue(method.Value));
                else if (element is PropertyDefinition { Static: false } field)
                    Put(receiver, ClassMemberName(field.Key, field.Computed), Evaluate(field.Value));
            }
            var constructor = type.Body.Body.OfType<MethodDefinition>().FirstOrDefault(method => method.Kind == PropertyKind.Constructor);
            return constructor is null ? Value.Local : Invoke(constructor.Value, arguments, receiver);
        }
        finally
        {
            _receivers.RemoveAt(_receivers.Count - 1);
            _scopes.Clear(); _scopes.AddRange(callerScopes);
            _calls.Remove(node);
        }
    }

    private string ClassMemberName(Node key, bool computed) => !computed && key is Identifier name ? name.Name : Key(key);

    private Value Invoke(Node node, IReadOnlyList<Value> arguments, Value? receiver = null)
    {
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
        try
        {
            for (var index = 0; index < function.Params.Count; index++)
            {
                if (function.Params[index] is RestElement rest)
                {
                    var restArray = Allocate(rest);
                    for (var position = index; position < arguments.Count; position++)
                        Put(restArray, (position - index).ToString(System.Globalization.CultureInfo.InvariantCulture), arguments[position]);
                    foreach (var value in arguments.Skip(index).Where(value => value.UnknownSpread)) Put(restArray, "*", value);
                    Bind(rest.Argument, restArray);
                    break;
                }
                Bind(function.Params[index], index < arguments.Count ? arguments[index] : Value.Local);
            }
            var flow = function.Body is BlockStatement ? Execute(function.Body) : new Flow(false, Evaluate(function.Body), Returns: _state.Copy());
            var exits = Join(flow.Normal ? _state : null, flow.Returns);
            if (exits is not null) _state = exits;
            if (flow.Throws is not null) foreach (var exceptional in _exceptionStates) exceptional.Join(flow.Throws);
            // Hoisted nested functions were created before this invocation's parameters were bound. Retain the
            // actual lexical cells before restoring the caller, including a returned closure's parameter aliases.
            if (local is not null)
                foreach (var nested in _closures.Values.Where(item => item.Scopes.Contains(local)))
                    foreach (var (binding, value) in _state.Bindings.Where(item => nested.Scopes.Contains(item.Key.Scope)))
                        nested.Captured[binding] = nested.Captured.GetValueOrDefault(binding, Value.Local).Union(value);
            return flow.Returned;
        }
        finally
        {
            foreach (var binding in _state.Bindings.Keys.Where(binding => !closure.Scopes.Contains(binding.Scope)).ToArray())
                if (previous.TryGetValue(binding, out var value)) _state.Bindings[binding] = value; else _state.Bindings.Remove(binding);
            _scopes.Clear(); _scopes.AddRange(callerScopes);
            _receivers.RemoveAt(_receivers.Count - 1);
            _arguments.RemoveAt(_arguments.Count - 1);
            _calls.Remove(node);
        }
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
        if (ContainsSession(value)) return true;
        foreach (var function in value.Functions) if (ContainsSession(Invoke(function, []))) return true;
        foreach (var item in value.Objects)
            foreach (var (key, getter) in _getters.Where(entry => ReferenceEquals(entry.Key.Object, item)))
                if (ContainsSession(Apply(getter, [], value))) return true;
        return _writes;
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
            if (!seen.Add(item)) continue;
            try { OwnProperties(Value.Object(item), (receiver, _, member) => Serialize(member, replacer, receiver, seen)); }
            // JSON's cycle stack is ancestry, not a cache: a shared reference is visited again in its later position
            // after intervening getters/toJSON calls may have changed the aliases captured by its accessors.
            finally { seen.Remove(item); }
            serialized ??= _state.Copy(); serialized.Join(_state);
        }
        _state = serialized ?? serializationEntry;
    }

    private static bool IsReference(Value value) => !value.Session && (value.Objects.Count > 0 || value.Functions.Count > 0);
    private static bool KnownArray(Value value) => !value.Session && value.Objects.Count > 0 && value.Objects.All(item => item is ArrayExpression or RestElement ||
        item is NewExpression { Callee: Identifier { Name: "Array" } } || item is CallExpression { Callee: MemberExpression member } &&
        (member.Property is Identifier { Name: "map" or "filter" or "flatMap" or "concat" or "slice" or "splice" or "toReversed" or "toSorted" or "toSpliced" or "with" } ||
         member is { Object: Identifier { Name: "Array" }, Property: Identifier { Name: "from" or "of" } }));

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
                if (receiver.Objects.All(item => _state.Members.ContainsKey((item, "join")) || _getters.ContainsKey((item, "join"))))
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
            if (member.Computed) EvaluateKey(member.Property);
            function = Member(receiver, Name(member));
        }
        var arguments = Arguments(call.Arguments, function);
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
                Coerce(arguments.Skip(1).Take(1), stringHint: true);
                var key = call.Arguments.Count >= 2 ? Key(call.Arguments[1]) : "*";
                Put(descriptor, "value", StoredMember(arguments[0], key));
                foreach (var item in arguments[0].Objects)
                {
                    if (_getters.TryGetValue((item, key), out var getter)) Put(descriptor, "get", getter);
                    if (_setters.TryGetValue((item, key), out var setter)) Put(descriptor, "set", setter);
                }
                return descriptor;
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
                return arguments.Length > 1 && arguments[1].Functions.Count > 0
                    ? Apply(arguments[1], [Value.Local, Value.Local], Value.Local) : Value.Local;
            if (global.Name == "Object" && method is "values" or "entries" && arguments.Length > 0)
            {
                var listed = Allocate(call);
                var value = arguments[0].Session ? Value.Owned : Value.Local;
                OwnProperties(arguments[0], (_, _, memberValue) => value = value.Union(memberValue));
                if (method == "entries")
                {
                    // Entry rows are new arrays; only their value column shares the source value.
                    var row = Allocate(call.Callee);
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
                Put(mapped, "*", arguments.Length <= 1 || arguments[1].Functions.Count == 0 ? elements :
                    Apply(arguments[1], [elements, Value.Local], arguments.Length > 2 ? arguments[2] : Value.Local));
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
        if (method is "sort" or "toSorted")
        {
            if (arguments.FirstOrDefault()?.Functions.Count > 0)
                foreach (var callback in arguments[0].Functions)
                    Coerce([Invoke(callback, [Member(receiver, "*"), Member(receiver, "*")])]);
            else Coerce([Member(receiver, "*")], stringHint: true);
        }
        if (method is "map" or "filter" or "find" or "findIndex" or "findLast" or "findLastIndex" or "some" or "every" or "forEach" or "flatMap" or "reduce" or "reduceRight")
            foreach (var callback in arguments.FirstOrDefault()?.Functions ?? [])
                callbackResult = callbackResult.Union(Invoke(callback, method is "reduce" or "reduceRight"
                    ? [arguments.Length > 1 ? arguments[1] : Member(receiver, "*"), Member(receiver, "*"), Value.Local, receiver]
                    : [Member(receiver, "*"), Value.Local, receiver],
                    method is not ("reduce" or "reduceRight") && arguments.Length > 1 ? arguments[1] : Value.Local));
        if (method is "replace" or "replaceAll")
        {
            Coerce([receiver], stringHint: true);
            Coerce(arguments.Take(1), stringHint: true);
            if (arguments.Length > 1 && arguments[1].Functions.Count > 0)
                Coerce([Apply(arguments[1], [Value.Local, Value.Local, Value.Local], Value.Local)], stringHint: true);
            else if (arguments.Length > 1) Coerce([arguments[1]], stringHint: true);
            return Value.Local;
        }
        if (method is "reduce" or "reduceRight") return callbackResult.Union(arguments.Length > 1 ? arguments[1] : Member(receiver, "*"));
        if (method is "map" or "flatMap")
        {
            var mapped = Value.Object(call);
            Put(mapped, "*", method == "flatMap" ? Member(callbackResult, "*") : callbackResult);
            return mapped;
        }
        if (method == "concat")
        {
            var concatenated = LocalCopy(call, receiver);
            foreach (var argument in arguments) Put(concatenated, "*", Member(concatenated, "*").Union(argument).Union(Member(argument, "*")));
            return concatenated;
        }
        if (method is "pop" or "shift") return Member(receiver, "*");
        if (method is "sort" or "reverse" or "fill" or "copyWithin") return receiver;
        if (method is "slice" or "splice" or "filter" or "toReversed" or "toSorted" or "toSpliced" or "with") return LocalCopy(call, receiver);
        if (method is "at" or "find" or "findLast") return Member(receiver, "*");
        if (method is "push" or "unshift" or "splice" or "copyWithin" or "pop" or "shift" or "sort" or "reverse" or "fill" or
            "join" or "includes" or "indexOf" or "lastIndexOf" or "findIndex" or "findLastIndex" or "some" or "every" or "forEach" or "reduce" or "reduceRight" or
            "toString" or "toLocaleString" or "valueOf" or "hasOwnProperty" or "isPrototypeOf" or "propertyIsEnumerable" or
            "charAt" or "charCodeAt" or "codePointAt" or "substring" or "substr" or "split" or "replace" or "replaceAll" or "trim" or "toUpperCase" or "toLowerCase" or "startsWith" or "endsWith") return Value.Local;
        if (call.Callee is Identifier pure && Global(pure.Name) && pure.Name is "String" or "Number" or "Boolean" or "parseInt" or "parseFloat" or "isNaN" or "isFinite")
        {
            if (pure.Name != "Boolean") Coerce(arguments.Take(1), stringHint: pure.Name is "String" or "parseInt" or "parseFloat");
            if (pure.Name == "parseInt") Coerce(arguments.Skip(1).Take(1));
            return Value.Local;
        }
        if (call.Callee is Identifier { Name: "Object" } && Global("Object")) return arguments.FirstOrDefault() ?? Value.Object(call);
        // An opaque helper receiving a workflow-owned object (including inside a local wrapper or closure) could
        // change it. Unlike a known local helper, its read-only behavior cannot be established here.
        _writes |= OpaqueOwnership(receiver) || arguments.Any(argument => OpaqueOwnership(argument));
        return Value.Local;
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
