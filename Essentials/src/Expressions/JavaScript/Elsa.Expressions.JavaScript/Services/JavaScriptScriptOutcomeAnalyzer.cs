using Acornima;
using Acornima.Ast;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>
/// Parses a script-activity program (top-level <c>return</c> allowed, as the script evaluator runs it) and reads the
/// literal outcomes of its <c>complete(result, outcomes)</c> calls, its top-level returned string literals and its
/// declarations of the reserved name <c>complete</c>. It never evaluates the source.
/// </summary>
internal sealed class JavaScriptScriptOutcomeAnalyzer : IJavaScriptScriptOutcomeAnalyzer
{
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
            JavaScriptReservedNames.CollectCompleteDeclarations(parsed).Select(JavaScriptReservedNames.RangeOf).ToArray());
    }

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
