using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Jint.Services
{
    /// <summary>
    /// Provides a JavaScript evaluator using Jint.
    /// </summary>
    internal sealed class JintJavaScriptEvaluator(
        Serialization.Core.IObjectConverter objectConverter,
        IDomainEventSender mediator,
        IJintEngineFactory jintEngineFactory,
        IPreparedScriptFactory preparedScriptFactory) : IJavaScriptEvaluator
    {
        /// <inheritdoc />
        public async Task<object?> EvaluateAsync(
            string expression,
            Type returnType,
            IExpressionExecutionContext context,
            IExpressionEvaluatorOptions? options = default,
            IEnumerable<IJavaScriptFunction>? additionalFunctions = null,
            CancellationToken cancellationToken = default
        )
        {
            var engine = await jintEngineFactory.Create(options, cancellationToken);
            var evaluationContext = new JintExecutionContext(preparedScriptFactory, engine);

            await PublishOnEvaluating(expression, evaluationContext, context, options, cancellationToken);
            additionalFunctions?.ToList().ForEach(evaluationContext.RegisterFunction);

            var result = evaluationContext.Evaluate(expression);

            return objectConverter.ConvertTo(result, returnType);
        }

        private Task PublishOnEvaluating(string script, JintExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
        {
            var notification = new OnEvaluatingScript(
                script, executionContext, expressionContext, options
            );
            return mediator.Send(notification, cancellationToken);
        }
    }
}
