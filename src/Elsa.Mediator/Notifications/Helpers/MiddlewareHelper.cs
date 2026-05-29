using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Mediator.Notifications.Middleware;

/// <summary>
/// Fluent helper for composing middleware into the notification pipeline. Mirrors the ASP.NET
/// Core <c>UseMiddleware&lt;T&gt;()</c> ergonomic.
/// </summary>
public static class MiddlewareHelper
{
    private const string InvokeMethodName = "Invoke";
    private const string InvokeAsyncMethodName = "InvokeAsync";

    public static INotificationPipelineBuilder UseMiddleware<TMiddleware>(
        this INotificationPipelineBuilder builder,
        params object[] args
    )
        where TMiddleware : INotificationMiddleware
    {
        var middlewareType = typeof(TMiddleware);

        return builder.Use(next =>
        {
            var invokeMethod = GetInvokeMethod(middlewareType);
            var ctorParams = new object[] { next }.Concat(args).ToArray();
            var instance = ActivatorUtilities.CreateInstance(builder.ApplicationServices, middlewareType, ctorParams);
            return invokeMethod.CreateDelegate<NotificationMiddlewareDelegate>(instance);
        });
    }

    public static MethodInfo GetInvokeMethod(this Type middleware)
    {
        var invokeMethods = middleware.GetMethods([InvokeMethodName, InvokeAsyncMethodName]);

        switch (invokeMethods.Length)
        {
            case > 1:
                throw new InvalidOperationException("Multiple Invoke methods were found. Use either Invoke or InvokeAsync.");
            case 0:
                throw new InvalidOperationException("No Invoke methods were found. Use either Invoke or InvokeAsync.");
        }

        var methodInfo = invokeMethods[0];

        var returnsTask = typeof(Task).IsAssignableFrom(methodInfo.ReturnType);
        var returnsValueTask = typeof(ValueTask).IsAssignableFrom(methodInfo.ReturnType);
        if (!returnsTask && !returnsValueTask)
            throw new InvalidOperationException($"The {methodInfo.Name} method must return Task or ValueTask.");

        return methodInfo;
    }
}
