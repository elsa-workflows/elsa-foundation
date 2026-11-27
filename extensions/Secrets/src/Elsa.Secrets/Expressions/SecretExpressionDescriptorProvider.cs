using Elsa.Expressions.Core.Contracts;

namespace Elsa.Secrets.Expressions;

public sealed class SecretExpressionDescriptorProvider : IExpressionDescriptorProvider
{
    public IEnumerable<IExpressionDescriptor> GetDescriptors()
    {
        yield return new SecretExpressionDescriptor();
    }
}
