using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint3.Contracts;
using Jint;
using Jint.Runtime.Interop;

namespace Elsa.Expressions.JavaScript.Jint3.Configurators
{
    internal sealed class ObjectConvertersEngineConfigurator(IEnumerable<IObjectConverter> jintObjectConverters) 
        : IJintEngineOptionsConfigurator
    {
        public void Configure(Options options, IExpressionEvaluatorOptions? evaluatorOptions) 
            => options.Interop.ObjectConverters.AddRange(jintObjectConverters);
    }
}
