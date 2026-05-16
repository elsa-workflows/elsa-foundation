using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Core.Exceptions
{
    public sealed class JavaScriptExpressionException : Exception
    {
        public JavaScriptExpressionException()
        {
        }

        public JavaScriptExpressionException(string? message) : base(message)
        {
        }
    }
}
