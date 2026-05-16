using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Core.Exceptions
{
    public sealed class JavaScriptFunctionExecutionException : Exception
    {
        public JavaScriptFunctionExecutionException()
        {
        }

        public JavaScriptFunctionExecutionException(string? message) : base(message)
        {
        }
    }
}
