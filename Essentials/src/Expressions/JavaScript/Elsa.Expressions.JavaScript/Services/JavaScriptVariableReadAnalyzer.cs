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

        var walker = new Walker(_readingFunctions);
        walker.Visit(parsed);
        return new ScriptVariableReadAnalysis(walker.LiteralNames, walker.HasComputedRead, walker.LiteralReads);
    }

    private sealed class Walker(IReadOnlySet<string> readingFunctions)
    {
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
                case MethodDefinition or PropertyDefinition or AccessorProperty:
                    foreach (var child in node.ChildNodes)
                    {
                        if (child is not Identifier && child is not PrivateIdentifier)
                            Visit(child);
                    }

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
