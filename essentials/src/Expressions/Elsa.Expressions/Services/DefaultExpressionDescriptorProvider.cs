using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Services;

internal sealed class DefaultExpressionDescriptorProvider : IExpressionDescriptorProvider
{
    public IEnumerable<IExpressionDescriptor> GetDescriptors()
    {
        yield return new VariableExpressionDescriptor();
    }
}
