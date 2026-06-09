using Elsa.Expressions.Core.Contracts;
using Jint;

namespace Elsa.Expressions.JavaScript.Jint.Contracts
{
    public interface IJintEngineFactory
    {
        ValueTask<Engine> Create(IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default);
    }
}
