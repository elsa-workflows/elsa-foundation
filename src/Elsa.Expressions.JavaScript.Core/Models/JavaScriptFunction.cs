using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Exceptions;

namespace Elsa.Expressions.JavaScript.Core.Models
{
    public class JavaScriptFunction(string name, Func<IJavaScriptExecutionContext, object[], object?> @delegate) : IJavaScriptFunction
    {
        public JavaScriptFunction(string name, Func<object[], object?> @delegate)
            : this(name, (_, p) => @delegate(p))
        {
        }

        public JavaScriptFunction(string name, Action<IJavaScriptExecutionContext, object[]> @delegate) : this(name, CreateVoidAction(@delegate))
        {
        }

        public JavaScriptFunction(string name, Action<object[]> @delegate) : this(name, CreateVoidAction((_, p) => @delegate(p)))
        {
        }

        public JavaScriptFunction(string name, Action<IJavaScriptExecutionContext> @delegate) : this(name, CreateVoidAction((c, _) => @delegate(c)))
        {
        }

        public JavaScriptFunction(string name, Action @delegate) : this(name, CreateVoidAction((_, _) => @delegate()))
        {
        }
        public string Name => name;

        static private Func<IJavaScriptExecutionContext, object[], object?> CreateVoidAction(Action<IJavaScriptExecutionContext, object[]> action)
        {
            return (context, parameters) =>
            {
                action(context, parameters);
                return null;
            };
        }


        public object? Execute(IJavaScriptExecutionContext context, object[] parameters)
        {
            ValidateParameters(parameters);
            return @delegate.Invoke(context, parameters);
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
