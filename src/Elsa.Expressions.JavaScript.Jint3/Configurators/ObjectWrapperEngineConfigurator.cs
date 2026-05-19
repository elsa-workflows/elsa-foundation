using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint3.Contracts;
using Jint;
using Jint.Runtime.Interop;
using System.Collections;
using Options = Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint3.Configurators
{
    internal sealed class ObjectWrapperEngineConfigurator : IJintEngineOptionsConfigurator
    {
        public void Configure(Options options, IExpressionEvaluatorOptions? evaluatorOptions)
        {
            options.SetWrapObjectHandler((engine, target, type) =>
            {
                var instance = ObjectWrapper.Create(engine, target);

                if (IsObjectArrayLikeClrCollection(target.GetType()))
                    instance.Prototype = engine.Intrinsics.Array.PrototypeObject;

                return instance;
            });
        }

        static bool IsObjectArrayLikeClrCollection(Type type)
        {
            var isDictionary = typeof(IDictionary).IsAssignableFrom(type);

            if (isDictionary)
                return false;

            if (typeof(ICollection).IsAssignableFrom(type))
                return true;

            foreach (var interfaceType in type.GetInterfaces())
            {
                if (!interfaceType.IsGenericType)
                {
                    continue;
                }

                if (interfaceType.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)
                    || interfaceType.GetGenericTypeDefinition() == typeof(ICollection<>))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
