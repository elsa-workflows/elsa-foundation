using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Core.Models
{
    public sealed class JavaScriptFunction<TParam1>
        : JavaScriptFunction
            where TParam1 : notnull
    {
        public JavaScriptFunction(string name, Func<TParam1, object?> @delegate)
            : base(name, parameters => @delegate((TParam1)parameters[0]))
        {
        }

        public JavaScriptFunction(string name, Func<IJavaScriptExecutionContext, TParam1, object?> @delegate)
            : base(name, (context, parameters) => @delegate(context, (TParam1)parameters[0]))
        {
        }

        public JavaScriptFunction(string name, Action<TParam1?> @delegate)
            : base(name, parameters => @delegate((TParam1)parameters[0]))
        {
        }

        public JavaScriptFunction(string name, Action<IJavaScriptExecutionContext, TParam1> @delegate)
            : base(name, (context, parameters) => @delegate(context, (TParam1)parameters[0]))
        {
        }
    }

    public sealed class JavaScriptFunction<TParam1, TParam2>
      : JavaScriptFunction
            where TParam1 : notnull
            where TParam2 : notnull
    {
        public JavaScriptFunction(string name, Func<TParam1, TParam2, object?> @delegate)
            : base(name, (context, parameters) => @delegate((TParam1)parameters[0], (TParam2)parameters[1]))
        {

        }

        public JavaScriptFunction(string name, Func<IJavaScriptExecutionContext, TParam1, TParam2, object?> @delegate)
            : base(name, (context, parameters) => @delegate(context, (TParam1)parameters[0], (TParam2)parameters[1]))
        {
        }

        public JavaScriptFunction(string name, Action<TParam1, TParam2> @delegate)
           : base(name, (context, parameters) => @delegate((TParam1)parameters[0], (TParam2)parameters[1]))
        {

        }

        public JavaScriptFunction(string name, Action<IJavaScriptExecutionContext, TParam1, TParam2> @delegate)
            : base(name, (context, parameters) => @delegate(context, (TParam1)parameters[0], (TParam2)parameters[1]))
        {
        }
    }

    public sealed class JavaScriptFunction<TParam1, TParam2, TParam3>
      : JavaScriptFunction
            where TParam1 : notnull
            where TParam2 : notnull
            where TParam3 : notnull
    {
        public JavaScriptFunction(string name, Func<TParam1, TParam2, TParam3, object?> @delegate)
            : base(name, (context, parameters) => @delegate((TParam1)parameters[0], (TParam2)parameters[1], (TParam3)parameters[2]))
        {

        }

        public JavaScriptFunction(string name, Func<IJavaScriptExecutionContext, TParam1, TParam2, TParam3, object?> @delegate)
            : base(name, (context, parameters) => @delegate(context, (TParam1)parameters[0], (TParam2)parameters[1], (TParam3)parameters[2]))
        {
        }

        public JavaScriptFunction(string name, Action<TParam1, TParam2, TParam3> @delegate)
            : base(name, (context, parameters) => @delegate((TParam1)parameters[0], (TParam2)parameters[1], (TParam3)parameters[2]))
        {

        }

        public JavaScriptFunction(string name, Action<IJavaScriptExecutionContext, TParam1, TParam2, TParam3> @delegate)
            : base(name, (context, parameters) => @delegate(context, (TParam1)parameters[0], (TParam2)parameters[1], (TParam3)parameters[2]))
        {
        }
    }
}
