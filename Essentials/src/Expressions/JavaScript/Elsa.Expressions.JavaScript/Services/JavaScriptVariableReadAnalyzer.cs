using Elsa.Expressions.Core.Models;
using Acornima;
using Acornima.Ast;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>
/// Parses JavaScript with Acornima and reads its variable reads (spec 1222 FR-001, FR-008, FR-014):
/// <c>variables.X</c>, <c>variables['X']</c> and <c>getVariable('X')</c> are literal reads; a non-literal index or
/// argument, <c>variables</c> or <c>getVariable</c> used as a value (aliased, passed, enumerated or spread), or a
/// declaration of either name by the script itself makes the script read variables it does not name. So does a call to a
/// host function declared with <see cref="JavaScriptHostFunctionDeclaration.ReadsVariables"/> (T023).
/// <para>
/// It also reads a script body's writes (spec 1231 FR-018 to FR-020a): assignments, compound and update forms and
/// <c>delete</c> rooted at a literally named variable, <c>setVariable('X', v)</c>, the literal names whose value escapes
/// (into a local, an argument, a closure, or a method call on it), and whether the script writes variables it does not
/// name (a computed write, an escaping computed read, or <c>variables</c>, <c>getVariable</c> or <c>setVariable</c> used
/// as a value). Dynamic code and exposure of the engine global object are opaque body access too.
/// </para>
/// </summary>
internal sealed class JavaScriptVariableReadAnalyzer(IEnumerable<JavaScriptHostFunctionDeclaration>? hostFunctions = null) : IScriptVariableReadAnalyzer
{
    private readonly HashSet<string> _readingFunctions = (hostFunctions ?? [])
        .Where(function => function.ReadsVariables)
        .Select(function => function.Name)
        .ToHashSet(StringComparer.Ordinal);

    private const string VariablesRoot = "variables";
    private const string GetVariable = "getVariable";
    private const string SetVariable = "setVariable";

    public string Language => "JavaScript";

    public ScriptVariableReadAnalysis? Analyze(string source, bool scriptBody = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        Node parsed;
        try
        {
            parsed = scriptBody
                ? new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(source)
                : JavaScriptBindingSyntax.IsExpression(source)
                    ? new Parser().ParseExpression(source, strict: true)
                    : new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(source, strict: true);
        }
        catch (ParseErrorException)
        {
            return null;
        }

        // A script that declares its own `variables` or `getVariable` (a variable, parameter, function, class or catch
        // binding) reads that local through the name: none of its reads can be pinned, so all of them go through the
        // recorded run-time lookup.
        // setVariable exists only in a script body, so only there is it a root (an input expression's own local of that
        // name is an ordinary local).
        if (DeclaresRoot(parsed, scriptBody))
            return new ScriptVariableReadAnalysis([], hasComputedRead: true) { HasComputedWrite = true, WorkflowState = AnalyzeState(parsed) };

        var walker = new Walker(_readingFunctions, scriptBody);
        walker.Visit(parsed);
        return new ScriptVariableReadAnalysis(walker.LiteralNames, walker.HasComputedRead, walker.LiteralReads)
        {
            Writes = walker.Writes,
            Escapes = walker.Escapes.Distinct(StringComparer.Ordinal).ToArray(),
            WorkflowState = AnalyzeState(parsed),
            HasComputedWrite = walker.HasComputedWrite
        };
    }

    private static ScriptWorkflowStateEffects AnalyzeState(Node parsed)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var access = false;
        var opaque = false;
        var correlation = false;
        var instanceName = false;
        void Visit(Node node, Node? parent)
        {
            if (node is Identifier { Name: "workflowState" })
            {
                access = true;
                if (parent is not MemberExpression { Object: Identifier { Name: "workflowState" }, Computed: false })
                    opaque = true;
            }
            if (node is Identifier { Name: "eval" or "Function" or "globalThis" } || node is ThisExpression)
            { access = true; opaque = true; }
            if (node is MemberExpression dynamicMember &&
                dynamicMember.Object is not Identifier { Name: "variables" or "args" } &&
                dynamicMember.Object is not CallExpression { Callee: Identifier { Name: "getVariable" } } &&
                (IsOpaqueStateMember(dynamicMember.Property, dynamicMember.Computed) || dynamicMember.Object is Identifier { Name: "Reflect" }))
            { access = true; opaque = true; }
            if (node is Identifier { Name: "Object" or "Reflect" } intrinsic &&
                !(parent is MemberExpression accessMember && ReferenceEquals(accessMember.Object, intrinsic) ||
                  parent is CallExpression invocation && ReferenceEquals(invocation.Callee, intrinsic) || parent is NewExpression))
            { access = true; opaque = true; }
            if (node is MemberExpression { Object: Identifier { Name: "workflowState" } } member)
            {
                if (member.Computed || member.Property is not Identifier method || parent is not CallExpression call || !ReferenceEquals(call.Callee, member))
                    opaque = true;
                else
                {
                    switch (method.Name)
                    {
                        case "setOutput":
                            if (call.Arguments.Count == 2 && call.Arguments[0] is Literal { Value: string name } && !string.IsNullOrWhiteSpace(name)) names.Add(name);
                            else opaque = true;
                            break;
                        case "setCorrelationId": correlation = true; break;
                        case "setInstanceName": instanceName = true; break;
                        case "getOutput" or "getCorrelationId" or "getInstanceName" or "requireExactIdentity": break;
                        default: opaque = true; break;
                    }
                }
            }
            foreach (var child in node.ChildNodes) Visit(child, node);
        }
        Visit(parsed, null);
        return new ScriptWorkflowStateEffects { AccessesState = access, OutputNames = names.Order(StringComparer.Ordinal).ToArray(),
            WritesCorrelationId = correlation, WritesInstanceName = instanceName, Conservative = opaque };
    }

    private static bool IsOpaqueStateMember(Node key, bool computed) => key switch
    {
        Identifier name when !computed => name.Name is "constructor" or "getOwnPropertyDescriptor" or "getOwnPropertyDescriptors",
        Literal { Value: string name } => name is "constructor" or "getOwnPropertyDescriptor" or "getOwnPropertyDescriptors",
        Literal => false,
        _ => computed
    };

    private static bool DeclaresRoot(Node node, bool scriptBody)
    {
        var bindings = node switch
        {
            VariableDeclarator declarator => [declarator.Id],
            IFunction function => function.Params.Cast<Node>().Prepend(function.Id).OfType<Node>(),
            CatchClause { Param: { } param } => [param],
            ClassDeclaration { Id: { } id } => [id],
            ClassExpression { Id: { } id } => [id],
            _ => (IEnumerable<Node>)[]
        };
        if (bindings.Any(binding => BindsRoot(binding, scriptBody)))
            return true;

        foreach (var child in node.ChildNodes)
        {
            if (DeclaresRoot(child, scriptBody))
                return true;
        }

        return false;
    }

    private static bool BindsRoot(Node binding, bool scriptBody) => binding switch
    {
        Identifier { Name: VariablesRoot or GetVariable } => true,
        Identifier { Name: SetVariable } => scriptBody,
        Identifier => false,
        ObjectPattern pattern => pattern.Properties.Any(property => property switch
        {
            Property { Value: Node value } => BindsRoot(value, scriptBody),
            RestElement rest => BindsRoot(rest.Argument, scriptBody),
            _ => false
        }),
        ArrayPattern pattern => pattern.Elements.Any(element => element is not null && BindsRoot(element, scriptBody)),
        AssignmentPattern assignment => BindsRoot(assignment.Left, scriptBody),
        RestElement rest => BindsRoot(rest.Argument, scriptBody),
        _ => false
    };

    private sealed class Walker(IReadOnlySet<string> readingFunctions, bool scriptBody)
    {
        // A built-in is called by its own name, a host function as namespace.member (spec 1223 FR-027).
        private static string? CalleeName(Node callee) => callee switch
        {
            Identifier identifier => identifier.Name,
            MemberExpression { Computed: false, Object: Identifier root, Property: Identifier member } => $"{root.Name}.{member.Name}",
            _ => null
        };

        public List<string> LiteralNames { get; } = [];
        public List<ScriptVariableLiteralRead> LiteralReads { get; } = [];
        public bool HasComputedRead { get; private set; }
        public List<ScriptVariableWrite> Writes { get; } = [];
        public List<string> Escapes { get; } = [];
        public bool HasComputedWrite { get; private set; }

        // The nodes from the root to the one being visited, so a read can tell what consumes its value.
        private readonly List<Node> _path = [];

        /// <summary>
        /// Records a write whose target is rooted at a variable named literally (spec 1228 FR-006a): <c>variables.X…</c>,
        /// <c>variables['X']…</c> or <c>getVariable('X')…</c>.
        /// </summary>
        private void RecordWrite(Node target) => RecordWrite(target, target, []);

        /// <summary>
        /// Walks a write target from its outermost member to the value it changes. A member of a parenthesized value
        /// (<c>(variables.order || {}).total = 5</c>, <c>(flag ? variables.a : variables.b).n++</c>) changes whichever
        /// value the wrapper yields, so each branch is followed.
        /// </summary>
        private void RecordWrite(Node target, Node start, List<string> outer)
        {
            var segments = new List<string>(outer);
            var node = start;
            while (true)
            {
                switch (node)
                {
                    case LogicalExpression logical when segments.Count > 0:
                        RecordWrite(target, logical.Left, segments);
                        RecordWrite(target, logical.Right, segments);
                        return;
                    case ConditionalExpression conditional when segments.Count > 0:
                        RecordWrite(target, conditional.Consequent, segments);
                        RecordWrite(target, conditional.Alternate, segments);
                        return;
                    case SequenceExpression sequence when segments.Count > 0:
                        node = sequence.Expressions[^1];
                        continue;
                    case AwaitExpression awaited when segments.Count > 0:
                        node = awaited.Argument;
                        continue;
                    case ChainExpression chain:
                        node = chain.Expression;
                        continue;
                }

                // A wrapper yielded getVariable(…) itself: (getVariable('o') || {}).m = v changes o.
                if (node is CallExpression { Callee: Identifier { Name: GetVariable } } wrapped && segments.Count > 0)
                {
                    if (wrapped.Arguments.Count >= 1 && wrapped.Arguments[0] is Literal { Value: string named })
                    {
                        segments.Add(Segment(named));
                        segments.Reverse();
                        Writes.Add(new ScriptVariableWrite(named, VariablesRoot + string.Concat(segments)) { Start = target.Start });
                    }
                    else
                        HasComputedWrite = true;
                    return;
                }

                if (node is not MemberExpression member)
                    return;

                if (member.Object is Identifier { Name: VariablesRoot })
                {
                    var name = !member.Computed && member.Property is Identifier identifier ? identifier.Name
                        : member.Computed && member.Property is Literal { Value: string literal } ? literal
                        : null;
                    if (name is not null)
                    {
                        segments.Add(Segment(name));
                        segments.Reverse();
                        Writes.Add(new ScriptVariableWrite(name, VariablesRoot + string.Concat(segments)) { Start = target.Start });
                    }
                    else
                        // variables[k] = v, variables[k].m = v: a write the source does not name (FR-020a).
                        HasComputedWrite = true;

                    return;
                }

                segments.Add(!member.Computed && member.Property is Identifier property ? Segment(property.Name)
                    : member.Property is Literal { Value: string key } ? Segment(key)
                    : member.Property is Literal { Value: double index } ? $"[{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}]"
                    : "[…]");
                if (member.Object is CallExpression { Callee: Identifier { Name: GetVariable } } call &&
                    call.Arguments.Count >= 1 && call.Arguments[0] is Literal { Value: string variable })
                {
                    segments.Add(Segment(variable));
                    segments.Reverse();
                    Writes.Add(new ScriptVariableWrite(variable, VariablesRoot + string.Concat(segments)) { Start = target.Start });
                    return;
                }

                if (member.Object is CallExpression { Callee: Identifier { Name: GetVariable } })
                {
                    // getVariable(k).m = v: a member change of a variable the source does not name.
                    HasComputedWrite = true;
                    return;
                }

                node = member.Object;
            }
        }

        /// <summary>
        /// Records the writes of an assignment or loop-head target: a member target, or each member target inside a
        /// destructuring pattern (<c>[variables.a, variables.b] = …</c>, <c>({ x: variables.total } = o)</c>,
        /// <c>for (variables.key in o)</c>).
        /// </summary>
        private void RecordTargetWrites(Node? target)
        {
            switch (target)
            {
                case ArrayPattern array:
                    foreach (var element in array.Elements)
                        RecordTargetWrites(element);
                    return;
                case ObjectPattern objectPattern:
                    foreach (var property in objectPattern.Properties)
                        RecordTargetWrites(property switch
                        {
                            Property { Value: Node value } => value,
                            RestElement rest => rest.Argument,
                            _ => null
                        });
                    return;
                case AssignmentPattern assignment:
                    RecordTargetWrites(assignment.Left);
                    return;
                case RestElement rest:
                    RecordTargetWrites(rest.Argument);
                    return;
                case null:
                    return;
                default:
                    RecordWrite(target);
                    return;
            }
        }

        /// <summary>
        /// Whether the value read at the end of <see cref="_path"/> can be changed by the script later (FR-019): it
        /// escapes into a binding, an argument, a container, a closure's return, or has a method called on it. A value
        /// that is only compared, combined into a new value, tested, discarded, or returned from the script itself does
        /// not escape. A member read of the value escapes when the member's value does.
        /// </summary>
        private bool ValueEscapes()
        {
            var index = _path.Count - 1;
            var current = _path[index];
            while (index > 0)
            {
                var parent = _path[index - 1];
                switch (parent)
                {
                    case MemberExpression member when ReferenceEquals(member.Object, current):
                        // A method call on the value can change it (push, sort, splice).
                        if (index > 1 && _path[index - 2] is CallExpression call && ReferenceEquals(call.Callee, parent))
                            return true;
                        break;
                    case MemberExpression:
                        // The value is a computed key: only its text is used.
                        return false;
                    case ChainExpression or AwaitExpression or LogicalExpression or SequenceExpression:
                        break;
                    case ConditionalExpression conditional:
                        if (ReferenceEquals(conditional.Test, current))
                            return false;
                        break;
                    case TemplateLiteral when index > 1 && _path[index - 2] is TaggedTemplateExpression:
                        // A tagged template passes its substitutions to the tag function as arguments.
                        return true;
                    case AssignmentExpression assignment when ReferenceEquals(assignment.Left, current):
                    case UpdateExpression or UnaryExpression or BinaryExpression or TemplateLiteral or ExpressionStatement:
                    case IfStatement or WhileStatement or DoWhileStatement or ForStatement or SwitchStatement or SwitchCase:
                    case ForInStatement:
                        // for…in reads only its object's keys; its head is a write, recorded as one.
                        return false;
                    case ReturnStatement:
                        // The script's own returned value leaves the script; a nested function's goes back into it.
                        return _path.Take(index - 1).Any(node => node is IFunction);
                    default:
                        return true;
                }

                current = parent;
                index--;
            }

            return false;
        }

        private static string Segment(string name) =>
            name.Length > 0 && (char.IsLetter(name[0]) || name[0] is '_' or '$') && name.All(character => char.IsLetterOrDigit(character) || character is '_' or '$')
                ? "." + name
                : "[" + System.Text.Json.JsonSerializer.Serialize(name) + "]";

        private void AddLiteral(string name, Node token, bool isMemberName, bool escapes)
        {
            LiteralNames.Add(name);
            LiteralReads.Add(new ScriptVariableLiteralRead(name, token.Start, token.End, isMemberName));
            if (escapes)
                Escapes.Add(name);
        }

        public void Visit(Node? node)
        {
            if (node is null)
                return;
            _path.Add(node);
            try
            {
                VisitNode(node);
            }
            finally
            {
                _path.RemoveAt(_path.Count - 1);
            }
        }

        private void VisitNode(Node node)
        {
            switch (node)
            {
                case AssignmentExpression assignment:
                    RecordTargetWrites(assignment.Left);
                    break;
                case ForInStatement { Left: not VariableDeclaration } forIn:
                    RecordTargetWrites(forIn.Left);
                    break;
                case ForOfStatement { Left: not VariableDeclaration } forOf:
                    RecordTargetWrites(forOf.Left);
                    break;
                case UpdateExpression update:
                    RecordWrite(update.Argument);
                    break;
                case UnaryExpression { Operator: Operator.Delete } deletion:
                    RecordWrite(deletion.Argument);
                    break;
                case MemberExpression { Object: Identifier { Name: VariablesRoot } } member:
                    if (!member.Computed && member.Property is Identifier name)
                        AddLiteral(name.Name, name, isMemberName: true, escapes: ValueEscapes());
                    else if (member.Computed && member.Property is Literal { Value: string literal } literalNode)
                        AddLiteral(literal, literalNode, isMemberName: false, escapes: ValueEscapes());
                    else
                    {
                        HasComputedRead = true;
                        if (ValueEscapes())
                            HasComputedWrite = true;
                        if (member.Computed)
                            Visit(member.Property);
                    }

                    return;
                case MemberExpression { Computed: false, Property: Identifier { Name: VariablesRoot or GetVariable or SetVariable } property } global
                    when global.Object is ThisExpression or Identifier { Name: "globalThis" or "self" or "window" } &&
                         (scriptBody || property.Name != SetVariable):
                    // globalThis.variables, this.getVariable(...): the root reached through another object.
                    HasComputedRead = true;
                    HasComputedWrite = true;
                    Visit(global.Object);
                    return;
                case MemberExpression { Computed: true, Property: Literal { Value: VariablesRoot or GetVariable or SetVariable } key } bracketed
                    when bracketed.Object is ThisExpression or Identifier { Name: "globalThis" or "self" or "window" } &&
                         (scriptBody || (string?)key.Value != SetVariable):
                    // globalThis['variables']: the root reached through another object by a literal key.
                    HasComputedRead = true;
                    HasComputedWrite = true;
                    Visit(bracketed.Object);
                    return;
                case MemberExpression member:
                    if (scriptBody && (OpaqueMemberKey(member.Property, member.Computed) ||
                        member.Object is Identifier { Name: "Reflect" }))
                    {
                        HasComputedRead = true;
                        HasComputedWrite = true;
                    }
                    Visit(member.Object);
                    if (member.Computed)
                        Visit(member.Property);
                    return;
                case CallExpression readingCall when CalleeName(readingCall.Callee) is { } calleeName && readingFunctions.Contains(calleeName):
                    // An opaque read: the host function reads variables the script does not name.
                    HasComputedRead = true;
                    foreach (var argument in readingCall.Arguments)
                        Visit(argument);
                    return;
                case CallExpression { Callee: Identifier { Name: GetVariable } } call:
                    if (call.Arguments.Count >= 1 && call.Arguments[0] is Literal { Value: string literalName } argumentNode)
                        AddLiteral(literalName, argumentNode, isMemberName: false, escapes: ValueEscapes());
                    else if (call.Arguments.Count >= 1)
                    {
                        HasComputedRead = true;
                        if (ValueEscapes())
                            HasComputedWrite = true;
                    }
                    foreach (var argument in call.Arguments)
                        Visit(argument);
                    return;
                case CallExpression { Callee: Identifier { Name: SetVariable } } setter when scriptBody:
                    // setVariable('X', v) is a whole assignment of X (spec 1231 FR-005); a computed name is a computed write.
                    if (setter.Arguments.Count >= 1 && setter.Arguments[0] is Literal { Value: string written } writtenNode)
                    {
                        AddLiteral(written, writtenNode, isMemberName: false, escapes: false);
                        Writes.Add(new ScriptVariableWrite(written, VariablesRoot + Segment(written)) { Start = setter.Start });
                    }
                    else
                        HasComputedWrite = true;
                    foreach (var argument in setter.Arguments)
                        Visit(argument);
                    return;
                case Property property:
                    if (scriptBody && _path.Count > 1 && _path[^2] is ObjectPattern &&
                        OpaqueMemberKey(property.Key, property.Computed))
                    {
                        HasComputedRead = true;
                        HasComputedWrite = true;
                    }
                    // A non-computed key is a name, not a read; a shorthand { variables } is a value use.
                    if (property.Computed || property.Shorthand)
                        Visit(property.Key);
                    if (!property.Shorthand)
                        Visit(property.Value);
                    return;
                case MethodDefinition method:
                    if (method.Computed)
                        Visit(method.Key);
                    Visit(method.Value);
                    return;
                case PropertyDefinition field:
                    if (field.Computed)
                        Visit(field.Key);
                    Visit(field.Value);
                    return;
                case AccessorProperty accessor:
                    if (accessor.Computed)
                        Visit(accessor.Key);
                    Visit(accessor.Value);
                    return;
                case LabeledStatement labeled:
                    Visit(labeled.Body);
                    return;
                case BreakStatement or ContinueStatement:
                    return;
                case Identifier { Name: VariablesRoot or GetVariable }:
                case Identifier { Name: SetVariable } when scriptBody:
                    // Any other appearance is a value use, or the script's own declaration of the name: reads and
                    // writes through it cannot be named (FR-020a).
                    HasComputedRead = true;
                    HasComputedWrite = true;
                    return;
                case Identifier { Name: "eval" or "Function" or "globalThis" } when scriptBody:
                    // Dynamic code and an aliased engine global can reach the body setter without a syntactic
                    // variable root. Keep the runtime lookup available and pin the conservative may-write set.
                    // As with declared/escaped roots, locally shadowing one of these names is conservative too.
                    HasComputedRead = true;
                    HasComputedWrite = true;
                    return;
                case ThisExpression when scriptBody:
                    HasComputedRead = true;
                    HasComputedWrite = true;
                    return;
                case Identifier { Name: "Object" or "Reflect" } intrinsic when scriptBody:
                    // A namespace passed around as a value can expose dynamic constructors through reflection.
                    // Direct member access is classified above; ordinary Object.keys remains a precise read.
                    if (_path.Count > 1 && (_path[^2] is MemberExpression access && ReferenceEquals(access.Object, intrinsic) ||
                        _path[^2] is CallExpression invocation && ReferenceEquals(invocation.Callee, intrinsic)))
                        return;
                    HasComputedRead = true;
                    HasComputedWrite = true;
                    return;
            }

            foreach (var child in node.ChildNodes)
                Visit(child);
        }

        private static bool OpaqueMemberKey(Node key, bool computed) => key switch
        {
            Identifier name when !computed => name.Name is "constructor" or "getOwnPropertyDescriptor" or "getOwnPropertyDescriptors",
            Literal { Value: string name } => name is "constructor" or "getOwnPropertyDescriptor" or "getOwnPropertyDescriptors",
            Literal => false,
            _ => computed
        };
    }
}
