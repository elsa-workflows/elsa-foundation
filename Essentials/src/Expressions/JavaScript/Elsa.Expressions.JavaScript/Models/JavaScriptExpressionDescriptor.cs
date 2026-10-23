using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.JavaScript.Models;

internal sealed class JavaScriptExpressionDescriptor : ExpressionDescriptor
{
    public const string JavaScriptExpressionTypeName = "JavaScript";
    public const string MonacoLanguagePropertyKey = "MonacoLanguage";

    public JavaScriptExpressionDescriptor() : base(JavaScriptExpressionTypeName, ExpressionEditingMode.Text)
    {
        Properties = new Dictionary<string, object>
        {
            [MonacoLanguagePropertyKey] = JavaScriptExpressionTypeName.ToLower()
        };
    }
}
