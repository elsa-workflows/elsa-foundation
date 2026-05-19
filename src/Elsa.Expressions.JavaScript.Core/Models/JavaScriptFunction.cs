using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Exceptions;

namespace Elsa.Expressions.JavaScript.Core.Models
{
    public class JavaScriptFunction(string name, Func<object[], object?> @delegate) : IJavaScriptFunction
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

        public JavaScriptFunction(string name, Action<object[]> @delegate) 
            : this(name, CreateVoidAction((p) => @delegate(p)))
        {
        }


        public JavaScriptFunction(string name, Action @delegate) 
            : this(name, CreateVoidAction((_) => @delegate()))
        {
        }

        public string Name { get; } = name;

        static private Func<object[], object?> CreateVoidAction(Action<object[]> action)
        {
            return (parameters) =>
            {
                action(parameters);
                return null;
            };
        }


        public object? Execute(object[] parameters)
        {
            ValidateParameters(parameters);
            return @delegate.Invoke(parameters);
        }

        private void ValidateParameters(object[] parameters)
        {
            var type = GetType();
            if (!type.IsGenericType)
            {
                return;
            }

            var genericArguments = type.GetGenericArguments();

            if (genericArguments.Length != parameters.Length)
            {
                throw new JavaScriptFunctionExecutionException(
                    $"Java script function expects a total of {genericArguments.Length} parameters, but received: {parameters.Length}"
                );
            }

            for (var i = 0; i < genericArguments.Length; i++)
            {
                var argument = genericArguments[i];
                var parameter = parameters[i];

                if (argument != parameter.GetType())
                {
                    throw new JavaScriptFunctionExecutionException(
                        $"Parameter {i} type '{parameter.GetType().Name}' does not match expected type: '{argument.Name}'"
                    );
                }
            }
        }
    }
}
