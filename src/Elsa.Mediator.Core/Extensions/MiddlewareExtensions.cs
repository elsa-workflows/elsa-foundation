using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Mediator.Core.Extensions;

public static class MiddlewareExtensions
{
    /// <summary>
    /// Adds middleware to the pipeline.
    /// </summary>
    public static ICommandPipelineBuilder UseMiddleware<TMiddleware>(this ICommandPipelineBuilder builder, params object[] args) where TMiddleware : ICommandMiddleware
    {
        return builder.Use(next => BuildMiddlewareDelegate<TMiddleware>(builder, next, args));
    }

    /// <summary>
    /// Adds middleware to the pipeline.
    /// </summary>
    public static IRequestPipelineBuilder UseMiddleware<TMiddleware>(this IRequestPipelineBuilder builder, params object[] args) where TMiddleware : IRequestMiddleware
    {
        return builder.Use(next => BuildMiddlewareDelegate<TMiddleware>(builder, next, args));
    }

    /// <summary>
    /// Adds middleware to the pipeline.
    /// </summary>
    public static IDomainEventPipelineBuilder UseMiddleware<TMiddleware>(this IDomainEventPipelineBuilder builder, params object[] args) where TMiddleware : IDomainEventMiddleware
    {
        return builder.Use(next => BuildMiddlewareDelegate<TMiddleware>(builder, next, args));
    }

    /// <summary>
    /// Gets the Invoke or InvokeAsync method from the middleware type.
    /// </summary>
    /// <param name="middleware">The middleware type.</param>
    /// <returns>The Invoke or InvokeAsync method.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the Invoke or InvokeAsync method cannot be found or the return type is not Task or ValueTask.</exception>
    public static MethodInfo GetInvokeMethod<TMiddleware>()
        where TMiddleware : IMiddleware
    {
        const string invokeMethodName = "Invoke";
        const string invokeAsyncMethodName = "InvokeAsync";
        var methods = typeof(TMiddleware).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        var invokeMethods = methods.Where(m => string.Equals(m.Name, invokeMethodName, StringComparison.Ordinal) || string.Equals(m.Name, invokeAsyncMethodName, StringComparison.Ordinal)).ToArray();

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

    /// <summary>
    /// Builds a delegate for the middleware type.
    /// </summary>
    private static CommandMiddlewareDelegate BuildMiddlewareDelegate<TMiddleware>(
        ICommandPipelineBuilder builder,
        CommandMiddlewareDelegate next,
        object[] args)

        where TMiddleware : ICommandMiddleware
    {
        var middlewareType = typeof(TMiddleware);
        var invokeMethod = GetInvokeMethod<TMiddleware>();

        var ctorParams = new[] { next }
            .Concat(args)
            .Select(x => x!)
            .ToArray();

        var instance = ActivatorUtilities.CreateInstance(builder.ApplicationServices, middlewareType, ctorParams);
        return invokeMethod.CreateDelegate<CommandMiddlewareDelegate>(instance);
    }


    /// <summary>
    /// Builds a delegate for the middleware type.
    /// </summary>
    private static DomainEventMiddlewareDelegate BuildMiddlewareDelegate<TMiddleware>(
        IDomainEventPipelineBuilder builder,
        DomainEventMiddlewareDelegate next,
        object[] args)

        where TMiddleware : IDomainEventMiddleware
    {
        var middlewareType = typeof(TMiddleware);
        var invokeMethod = GetInvokeMethod<TMiddleware>();

        var ctorParams = new[] { next }
            .Concat(args)
            .Select(x => x!)
            .ToArray();

        var instance = ActivatorUtilities.CreateInstance(builder.ApplicationServices, middlewareType, ctorParams);
        return invokeMethod.CreateDelegate<DomainEventMiddlewareDelegate>(instance);
    }

    /// <summary>
    /// Builds a delegate for the middleware type.
    /// </summary>
    private static RequestMiddlewareDelegate BuildMiddlewareDelegate<TMiddleware>(
        IRequestPipelineBuilder builder,
        RequestMiddlewareDelegate next,
        object[] args)

        where TMiddleware : IRequestMiddleware
    {
        var middlewareType = typeof(TMiddleware);
        var invokeMethod = GetInvokeMethod<TMiddleware>();

        var ctorParams = new[] { next }
            .Concat(args)
            .Select(x => x!)
            .ToArray();

        var instance = ActivatorUtilities.CreateInstance(builder.ApplicationServices, middlewareType, ctorParams);
        return invokeMethod.CreateDelegate<RequestMiddlewareDelegate>(instance);
    }
}
