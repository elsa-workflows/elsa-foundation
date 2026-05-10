using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.Core
{
    public interface IExpressionEvaluatorOptions
    {
        IDictionary<string, object> Arguments { get; }
    }
}
