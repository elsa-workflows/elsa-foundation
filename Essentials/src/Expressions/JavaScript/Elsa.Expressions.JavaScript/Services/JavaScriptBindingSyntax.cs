using Acornima;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>Shared, parse-only grammar selection for value bindings and their authoring diagnostics.</summary>
internal static class JavaScriptBindingSyntax
{
    public static bool IsExpression(string source)
    {
        try
        {
            // Expression precedence preserves existing bare object literals and anonymous functions.
            new Parser().ParseExpression(source, strict: true);
            return true;
        }
        catch (ParseErrorException)
        {
            // Only syntax failure selects a statement body. Never retry after executing authored code.
            new Parser(new ParserOptions { AllowReturnOutsideFunction = true }).ParseScript(source, strict: true);
            return false;
        }
    }
}
