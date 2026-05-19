using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Microsoft.Extensions.Options;
using JintOptions = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Configurators
{
    internal sealed class ClrAccessEngineConfigurator(IOptions<FeatureOptions> featureOptions) : IJintEngineOptionsConfigurator
    {
        public void Configure(JintOptions options, IExpressionEvaluatorOptions? evaluatorOptions)
        {
            if(featureOptions.Value.AllowClrAccess)
            {
                options.AllowClr();
            }
        }
    }
}
