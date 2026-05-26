using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Core.Models
{
    public class JavaScriptFunction(string name, Delegate @delegate) : IJavaScriptFunction
    {
        public static IJavaScriptFunction Build<TParam1>(string name, Func<TParam1, object?> execute)
            where TParam1 : notnull
            => new JavaScriptFunction<TParam1>(name, execute);

        public static IJavaScriptFunction Build<TParam1, TParam2>(string name, Func<TParam1, TParam2, object?> execute)
            where TParam1 : notnull
            where TParam2 : notnull
            => new JavaScriptFunction<TParam1, TParam2>(name, execute);

        public static IJavaScriptFunction Build<TParam1, TParam2, TParam3>(string name, Func<TParam1, TParam2, TParam3, object?> execute)
            where TParam1 : notnull
            where TParam2 : notnull
            where TParam3 : notnull
            => new JavaScriptFunction<TParam1, TParam2, TParam3>(name, execute);

        public string Name { get; } = name;

        public Delegate Delegate => @delegate;
    }
}
