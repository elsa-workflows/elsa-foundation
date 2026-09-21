namespace Elsa.Expressions.Core.Contracts;

public interface IExpressionToolingProviderResolver
{
    IExpressionToolingProvider? Find(string expressionType);
}
