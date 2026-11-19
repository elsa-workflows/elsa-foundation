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
/// </summary>
internal sealed class JavaScriptVariableReadAnalyzer(IEnumerable<JavaScriptHostFunctionDeclaration>? hostFunctions = null) : IScriptVariableReadAnalyzer
{
    private readonly HashSet<string> _readingFunctions = (hostFunctions ?? [])
        .Where(function => function.ReadsVariables)
        .Select(function => function.Name)
        .ToHashSet(StringComparer.Ordinal);

    private const string VariablesRoot = "variables";
    private const string GetVariable = "getVariable";

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
            return new ScriptVariableReadAnalysis([], hasComputedRead: true);

        var walker = new Walker(_readingFunctions);
        walker.Visit(parsed);
        return new ScriptVariableReadAnalysis(walker.LiteralNames, walker.HasComputedRead, walker.LiteralReads);
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
        Identifier { Name: VariablesRoot or GetVariable } => true,
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

        private void AddLiteral(string name, Node token, bool isMemberName)
        {
            LiteralNames.Add(name);
            LiteralReads.Add(new ScriptVariableLiteralRead(name, token.Start, token.End, isMemberName));
        }

        public void Visit(Node? node)
        {
            switch (node)
            {
                case null:
                    return;
                case MemberExpression { Object: Identifier { Name: VariablesRoot } } member:
                    if (!member.Computed && member.Property is Identifier name)
                        AddLiteral(name.Name, name, isMemberName: true);
                    else if (member.Computed && member.Property is Literal { Value: string literal } literalNode)
                        AddLiteral(literal, literalNode, isMemberName: false);
                    else
                    {
                        HasComputedRead = true;
                        if (member.Computed)
                            Visit(member.Property);
                    }

                    return;
                case MemberExpression { Computed: false, Property: Identifier { Name: VariablesRoot or GetVariable } } global
                    when global.Object is ThisExpression or Identifier { Name: "globalThis" or "self" or "window" }:
                    // globalThis.variables, this.getVariable(...): the root reached through another object.
                    HasComputedRead = true;
                    Visit(global.Object);
                    return;
                case MemberExpression { Computed: true, Property: Literal { Value: VariablesRoot or GetVariable } } bracketed
                    when bracketed.Object is ThisExpression or Identifier { Name: "globalThis" or "self" or "window" }:
                    // globalThis['variables']: the root reached through another object by a literal key.
                    HasComputedRead = true;
                    Visit(bracketed.Object);
                    return;
                case MemberExpression member:
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
                        AddLiteral(literalName, argumentNode, isMemberName: false);
                    else if (call.Arguments.Count >= 1)
                        HasComputedRead = true;
                    foreach (var argument in call.Arguments)
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
                case Identifier { Name: VariablesRoot or GetVariable }:
                    // Any other appearance is a value use, or the script's own declaration of the name.
                    HasComputedRead = true;
                    return;
            }

            foreach (var child in node.ChildNodes)
                Visit(child);
        }
    }
}
