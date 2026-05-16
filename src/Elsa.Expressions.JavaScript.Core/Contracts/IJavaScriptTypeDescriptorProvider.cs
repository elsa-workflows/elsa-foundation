using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptTypeDescriptorProvider
    {
        IEnumerable<JavaScriptTypeDescriptor> GetDescriptors();
    }
}
