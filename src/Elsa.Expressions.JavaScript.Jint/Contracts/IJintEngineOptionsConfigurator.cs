using Elsa.Expressions.Core.Contracts;
using Jint;

namespace Elsa.Expressions.JavaScript.Jint.Contracts
{
    public interface IJintEngineOptionsConfigurator
    {
        void Configure(Options options, IExpressionEvaluatorOptions? evaluatorOptions);
    }
}
