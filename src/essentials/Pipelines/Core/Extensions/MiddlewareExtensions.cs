using Elsa.Pipelines.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Elsa.Pipelines.Core.Extensions;

/// <summary>
/// Message-agnostic helpers for composing a middleware pipeline. Shared by every per-message
/// pipeline (commands, requests, events): each supplies its own strongly-typed delegate, this
/// engine instantiates the middleware and binds its Invoke method to that delegate.
/// </summary>
public static class MiddlewareExtensions
{
    /// <summary>
    /// Appends <typeparamref name="TMiddleware"/> to the pipeline. The middleware is activated with
    /// the composed <c>next</c> delegate prepended to its constructor, its <c>Invoke</c>/<c>InvokeAsync</c>
    /// method bound to the canonical <see cref="PipelineDelegate{TContext}"/>. This is the one
    /// registration path for command, request, and event middleware.
    /// </summary>
    public static IPipelineBuilder<TContext> UseMiddleware<TContext, TMiddleware>(this IPipelineBuilder<TContext> builder, params object[] args)
        where TMiddleware : IMiddleware
    {
        return builder.Use(next => BuildMiddlewareDelegate<TMiddleware, PipelineDelegate<TContext>>(builder.ApplicationServices, next, args));
    }

    /// <summary>
    /// Gets the Invoke or InvokeAsync method from the middleware type.
    /// </summary>
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
    /// Instantiates <typeparamref name="TMiddleware"/> (resolving constructor args from
    /// <paramref name="services"/>, with <paramref name="next"/> and <paramref name="args"/>
    /// prepended) and binds its Invoke method to a <typeparamref name="TDelegate"/>.
    /// </summary>
    public static TDelegate BuildMiddlewareDelegate<TMiddleware, TDelegate>(
        IServiceProvider services,
        TDelegate next,
        object[] args)
        where TMiddleware : IMiddleware
        where TDelegate : Delegate
    {
        var middlewareType = typeof(TMiddleware);
        var invokeMethod = GetInvokeMethod<TMiddleware>();

        var ctorParams = new object[] { next }
            .Concat(args)
            .Select(x => x!)
            .ToArray();

        var instance = ActivatorUtilities.CreateInstance(services, middlewareType, ctorParams);
        return invokeMethod.CreateDelegate<TDelegate>(instance);
    }
}
