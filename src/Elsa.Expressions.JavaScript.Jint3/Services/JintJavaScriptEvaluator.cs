using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Jint3.Services
{
    /// <summary>
    /// Provides a JavaScript evaluator using Jint.
    /// </summary>
    internal sealed class JintJavaScriptEvaluator(
        Serialization.Core.IObjectConverter objectConverter,
        IJavaScriptExecutionContextFactory contextFactory,
        IEnumerable<IJavaScriptEvaluationPreProcessor> preProcessors,
        IEnumerable<IJavaScriptEvaluationPostProcessor> postProcessors) : IJavaScriptEvaluator
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
            var executionContext = await contextFactory.Create(context, options, cancellationToken);

            RegisterAdditionalFunctions(executionContext, additionalFunctions);
            await InvokePreProcessors(executionContext, context, expression);

            var result = executionContext.Evaluate(expression);
            await InvokePostProcessors(executionContext, context, expression, result);

            return objectConverter.ConvertTo(result, returnType);
        }

        static void RegisterAdditionalFunctions(IJavaScriptExecutionContext context, IEnumerable<IJavaScriptFunction>? additionalFunctions)
        {
            if (additionalFunctions == null)
                return;

            foreach (var function in additionalFunctions)
            {
                context.RegisterFunction(function);
            }
        }

        async ValueTask InvokePostProcessors(IJavaScriptExecutionContext javascriptExecutionContext, IExpressionExecutionContext context, string expression, object? result)
        {
            foreach (var postProcessor in postProcessors)
            {
                await postProcessor.Process(javascriptExecutionContext, context, expression, result);
            }
        }

        async ValueTask InvokePreProcessors(IJavaScriptExecutionContext javascriptExecutionContext, IExpressionExecutionContext context, string expression)
        {
            foreach (var preProcessor in preProcessors)
            {
                await preProcessor.Process(javascriptExecutionContext, context, expression);
            }
        }
    }
}
