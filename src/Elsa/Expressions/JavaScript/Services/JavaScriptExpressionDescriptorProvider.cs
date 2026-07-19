using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Services;

internal class JavaScriptExpressionDescriptorProvider : IExpressionDescriptorProvider
{
    public IEnumerable<IExpressionDescriptor> GetDescriptors()
    {
        yield return new JavaScriptExpressionDescriptor()
        {
            DisplayName = "JavaScript"
        };
    }
}
