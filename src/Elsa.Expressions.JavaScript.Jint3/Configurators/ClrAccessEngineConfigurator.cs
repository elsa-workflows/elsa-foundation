using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint3.Contracts;
using Jint;
using Microsoft.Extensions.Options;
using Options = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint3.Configurators
{
    internal sealed class ClrAccessEngineConfigurator(IOptions<JintFeatureOptions> featureOptions) : IJintEngineOptionsConfigurator
    {
        public void Configure(Options options, IExpressionEvaluatorOptions? evaluatorOptions)
        {
            if(featureOptions.Value.AllowClrAccess)
            {
                options.AllowClr();
            }
        }
    }
}
