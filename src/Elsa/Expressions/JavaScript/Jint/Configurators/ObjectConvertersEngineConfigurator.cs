using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Jint.Runtime.Interop;
using JintOptions = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Configurators
{
    internal sealed class ObjectConvertersEngineConfigurator(IEnumerable<IObjectConverter> jintObjectConverters)
        : IJintEngineOptionsConfigurator
    {
        public void Configure(JintOptions options, IExpressionEvaluatorOptions? evaluatorOptions)
            => options.Interop.ObjectConverters.AddRange(jintObjectConverters);
    }
}
