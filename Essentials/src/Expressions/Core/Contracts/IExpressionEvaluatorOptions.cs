namespace Elsa.Expressions.Core.Contracts;

internal interface IExpressionEvaluatorOptions
{
    IDictionary<string, object> Arguments { get; }
}