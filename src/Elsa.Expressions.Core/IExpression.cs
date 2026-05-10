using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.Core
{
    public interface IExpression
    {
        /// <summary>
        /// Gets or sets the expression type.
        /// </summary>
        string Type { get; set; }

        /// <summary>
        /// Gets or sets the expression.
        /// </summary>
        object? Value { get; set; }

        TValue GetValue<TValue>();
    }
}
