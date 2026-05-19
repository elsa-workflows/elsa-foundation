using Elsa.Expressions.Core.Contracts;
using Jint;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Jint.Contracts
{
    public interface IJintEngineFactory
    {
        ValueTask<Engine> Create(IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default);
    }
}
