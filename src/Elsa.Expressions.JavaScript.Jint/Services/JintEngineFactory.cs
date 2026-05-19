using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Jint;
using JintOptions = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services
{
    internal sealed class JintEngineFactory(IEnumerable<IJintEngineOptionsConfigurator> jintEngineOptionsConfigurators)
        : IJintEngineFactory
    {

        public async ValueTask<Engine> Create(IExpressionEvaluatorOptions? options, CancellationToken _)
        {
            var jintOptions = new JintOptions()
            {
                ExperimentalFeatures = ExperimentalFeature.TaskInterop
            };

            foreach (var configurator in jintEngineOptionsConfigurators)
                configurator.Configure(jintOptions, options);

            var engine = new Engine(jintOptions);
            return engine;
        }
    }
}
