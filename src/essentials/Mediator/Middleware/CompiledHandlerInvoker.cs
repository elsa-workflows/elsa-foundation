using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Elsa.Mediator.Middleware;

/// <summary>
/// Builds and caches a compiled invoker per closed handler type, replacing the per-dispatch
/// <see cref="MethodBase.Invoke(object, object[])"/> reflection the old command/request invokers used.
/// The compiled delegate calls <c>Handle</c> directly, so handler exceptions propagate raw (no
/// <see cref="TargetInvocationException"/> wrapper). The cache holds only <see cref="Type"/> keys and
/// stateless delegates — never a handler instance or a scope — so a scoped handler is still resolved
/// from the caller's scope on every dispatch.
/// </summary>
internal static class CompiledHandlerInvoker
{
    private static readonly ConcurrentDictionary<Type, Func<object, object, CancellationToken, Task<object?>>> Cache = new();

    private static readonly MethodInfo CoerceMethod =
        typeof(CompiledHandlerInvoker).GetMethod(nameof(Coerce), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Gets (compiling on first use) the invoker for the given closed handler interface type.</summary>
    public static Func<object, object, CancellationToken, Task<object?>> Get(Type closedHandlerType)
        => Cache.GetOrAdd(closedHandlerType, Build);

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The Handle method is part of the ICommandHandler<,>/IRequestHandler<,> contracts whose implementations are registered in DI.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Expression.Compile mirrors the reflection-based dispatch this domain already relies on; the mediator is not used in an AOT-trimmed configuration.")]
    private static Func<object, object, CancellationToken, Task<object?>> Build(Type closedHandlerType)
    {
        var handleMethod = closedHandlerType.GetMethod("Handle")
            ?? throw new InvalidProgramException($"Cannot find method 'Handle' on '{closedHandlerType}'");

        var messageType = handleMethod.GetParameters()[0].ParameterType;
        var resultType = handleMethod.ReturnType.GetGenericArguments()[0]; // Task<TResult> -> TResult

        var handlerParam = Expression.Parameter(typeof(object), "handler");
        var messageParam = Expression.Parameter(typeof(object), "message");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        var call = Expression.Call(
            Expression.Convert(handlerParam, closedHandlerType),
            handleMethod,
            Expression.Convert(messageParam, messageType),
            ctParam);

        var coerce = Expression.Call(CoerceMethod.MakeGenericMethod(resultType), call);

        return Expression
            .Lambda<Func<object, object, CancellationToken, Task<object?>>>(coerce, handlerParam, messageParam, ctParam)
            .Compile();
    }

    private static async Task<object?> Coerce<TResult>(Task<TResult> task) => await task;
}
