using Elsa.Expressions.Core;

namespace Elsa.Expressions.Options
{
    /// <summary>
    /// Contains additional options for the expression evaluator.
    /// </summary>
    public sealed class ExpressionEvaluatorOptions : IExpressionEvaluatorOptions
    {
        /// <summary>
        /// An empty set of options.
        /// </summary>
        public static readonly ExpressionEvaluatorOptions Empty = new();

        /// <summary>
        /// An extra set of variables to add to the expression context.
        /// </summary>
        public IDictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();
    }
}
