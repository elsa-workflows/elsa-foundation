namespace Elsa.Expressions.JavaScript.Core.Models
{
    public sealed class JavaScriptFunction<TParam1>
        : JavaScriptFunction
            where TParam1 : notnull
    {
        public JavaScriptFunction(string name, Func<TParam1, object?> @delegate)
            : base(name, @delegate)
        {
        }

        public JavaScriptFunction(string name, Action<TParam1?> @delegate)
            : base(name,@delegate)
        {
        }

    }

    public sealed class JavaScriptFunction<TParam1, TParam2>
      : JavaScriptFunction
            where TParam1 : notnull
            where TParam2 : notnull
    {
        public JavaScriptFunction(string name, Func<TParam1, TParam2, object?> @delegate)
            : base(name, @delegate)
        {

        }


        public JavaScriptFunction(string name, Action<TParam1, TParam2> @delegate)
           : base(name, @delegate)
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
            : base(name, @delegate)
        {

        }


        public JavaScriptFunction(string name, Action<TParam1, TParam2, TParam3> @delegate)
            : base(name, @delegate)
        {

        }
    }
}
