namespace Elsa.Expressions.Core
{
    public interface IExpressionEvaluatorOptions
    {
        IDictionary<string, object> Arguments { get; }
    }
}
