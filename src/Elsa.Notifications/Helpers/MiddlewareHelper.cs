using Elsa.Notifications.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using Elsa.Primitives.Extensions;
using System.Reflection;
using System.Text;

namespace Elsa.Notifications.Helpers
{
    public static class MiddlewareHelper
    {
        public const string invokeMethodName = "Invoke";
        public const string invokeAsyncMethodName = "InvokeAsync";

        public static INotificationPipelineBuilder UseMiddleware<TMiddleware>(this INotificationPipelineBuilder builder, params object[] args) where TMiddleware : INotificationMiddleware
        {
            var middleware = typeof(TMiddleware);

            return builder.Use(next =>
            {
                var invokeMethod = GetInvokeMethod(middleware);
                var ctorParams = new[] { next }.Concat(args).Select(x => x).ToArray();
                var instance = ActivatorUtilities.CreateInstance(builder.ApplicationServices, middleware, ctorParams);
                return invokeMethod.CreateDelegate<NotificationMiddlewareDelegate>(instance);
            });
        }

        /// <summary>
        /// Gets the Invoke or InvokeAsync method from the middleware type.
        /// </summary>
        /// <param name="middleware">The middleware type.</param>
        /// <returns>The Invoke or InvokeAsync method.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the Invoke or InvokeAsync method cannot be found or the return type is not Task or ValueTask.</exception>
        public static MethodInfo GetInvokeMethod(this Type middleware)
        {
            var invokeMethods = middleware.GetMethods([invokeMethodName, invokeAsyncMethodName]);

            switch (invokeMethods.Length)
            {
                case > 1:
                    throw new InvalidOperationException("Multiple Invoke methods were found. Use either Invoke or InvokeAsync.");
                case 0:
                    throw new InvalidOperationException("No Invoke methods were found. Use either Invoke or InvokeAsync");
            }

            var methodInfo = invokeMethods[0];

            if (!typeof(Task).IsAssignableFrom(methodInfo.ReturnType) && !typeof(ValueTask).IsAssignableFrom(methodInfo.ReturnType))
                throw new InvalidOperationException($"The {methodInfo.Name} method must return Task or ValueTask");

            return methodInfo;
        }
    }
}
