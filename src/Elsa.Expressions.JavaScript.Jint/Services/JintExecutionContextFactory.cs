using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Jint;
using JintOptions = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services
{
    internal sealed class JintExecutionContextFactory(
        IPreparedScriptFactory preparedScriptFactory,
        IEnumerable<IJintEngineOptionsConfigurator> jintEngineOptionsConfigurators,
        IEnumerable<IJavaScriptFunctionProvider> functionProviders,
        IEnumerable<IJavaScriptObjectProvider> objectProviders,
        IEnumerable<IJavaScriptTypeDescriptorProvider> typeProviders)

        : IJavaScriptExecutionContextFactory
    {

        public async Task<IJavaScriptExecutionContext> Create(IExpressionExecutionContext expressionExecutionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default)
        {
            var context = CreateJintContext(options);
            await RegisterFunctions(context, expressionExecutionContext, options, cancellationToken);
            await RegisterObjects(context, options, cancellationToken);
            await RegisterTypes(context, cancellationToken);
            return context;
        }

        private JintExecutionContext CreateJintContext(IExpressionEvaluatorOptions? evaluatorOptions)
        {
            var jintOptions = new JintOptions()
            {
                ExperimentalFeatures = ExperimentalFeature.TaskInterop
            };

            foreach (var configurator in jintEngineOptionsConfigurators)
                configurator.Configure(jintOptions, evaluatorOptions);

            var engine = new Engine(jintOptions);
            return new JintExecutionContext(preparedScriptFactory, engine);
        }

        async ValueTask RegisterFunctions(JintExecutionContext executionContext, IExpressionExecutionContext expressionExecutionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
        {
            foreach (var provider in functionProviders)
            {
                var functions = await provider.GetFunctions(expressionExecutionContext, options, cancellationToken);
                foreach (var function in functions)
                {
                    executionContext.RegisterFunction(function);
                }
            }
        }

        async ValueTask RegisterObjects(JintExecutionContext executionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
        {
            foreach (var provider in objectProviders)
            {
                var objects = await provider.GetObjects(executionContext, options, cancellationToken);
                foreach (var obj in objects)
                {
                    executionContext.RegisterObject(obj);
                }
            }
        }

        ValueTask RegisterTypes(JintExecutionContext executionContext, CancellationToken cancellationToken)
        {
            foreach (var provider in typeProviders)
            {
                var descriptors = provider.GetDescriptors();
                foreach (var descriptor in descriptors)
                {
                    if (!descriptor.RegisterType)
                        continue;

                    executionContext.RegisterType(
                        descriptor.GetDescriptorType()
                    );
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
