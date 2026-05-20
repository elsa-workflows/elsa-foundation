using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.JavaScript.Services
{
    internal class JavaScriptExpressionDescriptorProvider : IExpressionDescriptorProvider
    {
        public IEnumerable<IExpressionDescriptor> GetDescriptors()
        {
            yield return new JavaScriptExpressionDescriptor()
            {
                DisplayName = "JavaScript",
                HandlerFactory = ActivatorUtilities.GetServiceOrCreateInstance<JavaScriptExpressionHandler>
            };
        }
    }
}
