namespace Elsa.Expressions.Core.Contracts;

public interface IExpressionFactory
{
    // I am not sure how the models look like: Expression models will be shipped over from elsa-core
    // and the parameters on this factory should be representable to what in general the concrete models need
    IExpression Create(string type, Func<object> valueFactory);

    IExpression Create(string type, object value);
}
