namespace Elsa.Expressions.Core.Contracts
{
    public interface IExpressionEvaluatorOptions
    {
        IDictionary<string, object> Arguments { get; }
    }
}
