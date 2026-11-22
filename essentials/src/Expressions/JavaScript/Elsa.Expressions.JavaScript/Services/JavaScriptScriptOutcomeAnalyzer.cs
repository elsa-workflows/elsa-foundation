using Acornima;
using Acornima.Ast;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>
/// Parses a script-activity program (top-level <c>return</c> allowed, as the script evaluator runs it) and reads the
/// literal outcomes of its <c>complete(result, outcomes)</c> calls, its top-level returned string literals, its
/// declarations of the reserved name <c>complete</c>, and the platform names it declares (spec 1223 FR-027). It never
/// evaluates the source.
/// </summary>
internal sealed class JavaScriptScriptOutcomeAnalyzer(IEnumerable<JavaScriptHostFunctionDeclaration>? hostFunctions = null)
    : IJavaScriptScriptOutcomeAnalyzer
{
    private const string VariablesRoot = "variables";

    // input, activities and workflow are reserved read roots a script does not use yet; a declaration only shadows them.
    private readonly HashSet<string> _shadowable = new HashSet<string>(["input", "activities", "workflow"], StringComparer.Ordinal)
        .Concat(JavaScriptBuiltInFunctions.Names)
        .Concat((hostFunctions ?? [])
            .Select(function => function.Name)
            .Where(name => name.Contains('.', StringComparison.Ordinal))
            .Select(name => name[..name.IndexOf('.', StringComparison.Ordinal)]))
        .ToHashSet(StringComparer.Ordinal);

    public JavaScriptScriptOutcomeAnalysis? Analyze(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        Script parsed;
        try
        {
            parsed = new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(script);
        }
        catch (ParseErrorException)
        {
            return null;
        }

        var literalOutcomes = new List<string>();
        var returnedStrings = new List<string>();
        Visit(parsed, insideFunction: false, literalOutcomes, returnedStrings);
        return new JavaScriptScriptOutcomeAnalysis(
            literalOutcomes,
            returnedStrings,
            JavaScriptReservedNames.CollectCompleteDeclarations(parsed).Select(JavaScriptReservedNames.RangeOf).ToArray(),
            DeclaredNames(parsed, name => StringComparer.Ordinal.Equals(name, VariablesRoot)),
            DeclaredNames(parsed, _shadowable.Contains));
    }

    private static string[] DeclaredNames(Node parsed, Func<string, bool> matches) =>
        JavaScriptReservedNames.CollectDeclarations(parsed, matches)
            .Select(identifier => identifier.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void Visit(Node node, bool insideFunction, List<string> literalOutcomes, List<string> returnedStrings)
    {
        switch (node)
        {
            case CallExpression { Callee: Identifier { Name: JavaScriptScriptBuiltIns.Complete } } call when call.Arguments.Count >= 2:
                CollectLiteralOutcomes(call.Arguments[1], literalOutcomes);
                break;
            case ReturnStatement { Argument: Literal { Value: string returned } } when !insideFunction:
                returnedStrings.Add(returned);
                break;
        }

        var nested = insideFunction || node is IFunction;
        foreach (var child in node.ChildNodes)
            Visit(child, nested, literalOutcomes, returnedStrings);
    }

    private static void CollectLiteralOutcomes(Node outcomes, List<string> literalOutcomes)
    {
        switch (outcomes)
        {
            case Literal { Value: string name }:
                literalOutcomes.Add(name);
                break;
            case ArrayExpression array:
                literalOutcomes.AddRange(array.Elements.OfType<Literal>().Select(element => element.Value).OfType<string>());
                break;
        }
    }
}
