using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Mediator.Core;
using Elsa.Primitives.Extensions;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using System.Collections;
using System.Dynamic;

namespace Elsa.Expressions.JavaScript.Jint.Services
{
    /// <summary>
    /// Provides a JavaScript evaluator using Jint.
    /// </summary>
    internal sealed class JintJavaScriptEvaluator(
        Serialization.Core.IObjectConverter objectConverter,
        IJintValueNormalizer valueNormalizer,
        IMediator mediator,
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
            var evaluationContext = new JintEvaluationContext();

            await PublishOnEvaluating(expression, evaluationContext, context, options, cancellationToken);
            additionalFunctions?.ToList().ForEach(evaluationContext.AddFunction);

            ConfigureEngine(engine, evaluationContext);

            var preparedScript = preparedScriptFactory.Create(expression);
            var result = engine.Execute(preparedScript);            

            return objectConverter.ConvertTo(result, returnType);
        }             
        
        void ConfigureEngine(Engine engine, JintEvaluationContext context)
        {
            foreach(var libraryScript in context.Libraries)
            {
                var preparedScript = preparedScriptFactory.Create(libraryScript);
                engine.Evaluate(preparedScript);
            }

            foreach(var (name, value) in context.Values)
            {
                var normalizedValue = valueNormalizer.Normalize(engine, value);
                engine.SetValue(name, normalizedValue);
            }

            foreach (var function in context.Functions)
            {                
                engine.SetValue(function.Name, function.Execute);
            }

            foreach (var type in context.Types)
            {
                engine.SetValue(type.Name, TypeReference.CreateTypeReference(engine, type));
            }
        }

        Task PublishOnEvaluating(string script, IJavaScriptEvaluationContext evaluationContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
        {
            var notification = new OnEvaluatingScript(
                script, evaluationContext, expressionContext, options
            );
            return mediator.Publish(notification, cancellationToken);
        }
    }
}
