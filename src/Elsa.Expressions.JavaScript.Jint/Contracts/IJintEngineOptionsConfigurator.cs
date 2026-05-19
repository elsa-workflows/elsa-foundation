using Elsa.Expressions.Core.Contracts;
using JintOptions = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Contracts
{
    public interface IJintEngineOptionsConfigurator
    {
        void Configure(JintOptions options, IExpressionEvaluatorOptions? evaluatorOptions);
    }
}
