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
/// as a value).
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
        if (DeclaresRoot(parsed))
            return new ScriptVariableReadAnalysis([], hasComputedRead: true) { HasComputedWrite = true };

        var walker = new Walker(_readingFunctions);
        walker.Visit(parsed);
        return new ScriptVariableReadAnalysis(walker.LiteralNames, walker.HasComputedRead, walker.LiteralReads)
        {
            Writes = walker.Writes,
            Escapes = walker.Escapes.Distinct(StringComparer.Ordinal).ToArray(),
            HasComputedWrite = walker.HasComputedWrite
        };
    }

    private static bool DeclaresRoot(Node node)
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
        if (bindings.Any(BindsRoot))
            return true;

        foreach (var child in node.ChildNodes)
        {
            if (DeclaresRoot(child))
                return true;
        }

        return false;
    }

    private static bool BindsRoot(Node binding) => binding switch
    {
        Identifier { Name: VariablesRoot or GetVariable or SetVariable } => true,
        Identifier => false,
        ObjectPattern pattern => pattern.Properties.Any(property => property switch
        {
            Property { Value: Node value } => BindsRoot(value),
            RestElement rest => BindsRoot(rest.Argument),
            _ => false
        }),
        ArrayPattern pattern => pattern.Elements.Any(element => element is not null && BindsRoot(element)),
        AssignmentPattern assignment => BindsRoot(assignment.Left),
        RestElement rest => BindsRoot(rest.Argument),
        _ => false
    };

    private sealed class Walker(IReadOnlySet<string> readingFunctions)
    {
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
        private void RecordWrite(Node target)
        {
            var segments = new List<string>();
            var node = target;
            while (node is MemberExpression member)
            {
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
                    case AssignmentExpression assignment when ReferenceEquals(assignment.Left, current):
                    case UpdateExpression or UnaryExpression or BinaryExpression or TemplateLiteral or ExpressionStatement:
                    case IfStatement or WhileStatement or DoWhileStatement or ForStatement or SwitchStatement or SwitchCase:
                    case ForInStatement:
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
                    RecordWrite(assignment.Left);
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
                case MemberExpression { Computed: false, Property: Identifier { Name: VariablesRoot or GetVariable or SetVariable } } global
                    when global.Object is ThisExpression or Identifier { Name: "globalThis" or "self" or "window" }:
                    // globalThis.variables, this.getVariable(...): the root reached through another object.
                    HasComputedRead = true;
                    HasComputedWrite = true;
                    Visit(global.Object);
                    return;
                case MemberExpression { Computed: true, Property: Literal { Value: VariablesRoot or GetVariable or SetVariable } } bracketed
                    when bracketed.Object is ThisExpression or Identifier { Name: "globalThis" or "self" or "window" }:
                    // globalThis['variables']: the root reached through another object by a literal key.
                    HasComputedRead = true;
                    HasComputedWrite = true;
                    Visit(bracketed.Object);
                    return;
                case MemberExpression member:
                    Visit(member.Object);
                    if (member.Computed)
                        Visit(member.Property);
                    return;
                case CallExpression { Callee: Identifier callee } readingCall when readingFunctions.Contains(callee.Name):
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
                case CallExpression { Callee: Identifier { Name: SetVariable } } setter:
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
                case Identifier { Name: VariablesRoot or GetVariable or SetVariable }:
                    // Any other appearance is a value use, or the script's own declaration of the name: reads and
                    // writes through it cannot be named (FR-020a).
                    HasComputedRead = true;
                    HasComputedWrite = true;
                    return;
            }

            foreach (var child in node.ChildNodes)
                Visit(child);
        }
    }
}
