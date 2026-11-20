using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.JavaScript.Services;

internal sealed class JavaScriptPortableExpressionSourceValidator : IPortableExpressionSourceValidator
{
    public string Language => "JavaScript";

    public IEnumerable<string> Validate(PortableExpressionSource source)
    {
        if (source.UsesReferenceKeys && string.IsNullOrWhiteSpace(source.Text))
            yield return "Enter a JavaScript expression for this output before publishing.";
    }
}
