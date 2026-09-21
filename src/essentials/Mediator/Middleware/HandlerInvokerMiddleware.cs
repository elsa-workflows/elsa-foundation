using Elsa.Mediator.Core.Contracts;
using Elsa.Pipelines.Core.Contracts;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Elsa.Mediator.Middleware;

/// <summary>
/// Resolves the single handler for a command or request through its <em>closed generic</em> interface
/// and invokes it via a cached compiled delegate. Resolving the closed generic (rather than the
/// non-generic marker, then filtering) means only the one relevant handler is constructed per
/// dispatch — the same disciplined pattern the events pipeline documents — instead of materialising
/// every handler in the container on every dispatch.
/// </summary>
[UsedImplicitly]
public sealed class HandlerInvokerMiddleware(PipelineDelegate<IMessageContext> next) : IMessageMiddleware
{
    // The closed IHandler<TMessage, TResult> type is stable per (message, result, family) key; cache it
    // so a hot dispatch path doesn't reconstruct it via reflection on every call.
    private static readonly ConcurrentDictionary<(Type MessageType, Type ResultType, Type HandlerDefinition), Type> HandlerTypeCache = new();

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2055:MakeGenericType", Justification = "The closed handler type is formed from message and result types whose handlers are registered in DI.")]
    public async ValueTask Invoke(IMessageContext context)
    {
        var messageType = context.Message.GetType();
        var handlerType = HandlerTypeCache.GetOrAdd(
            (messageType, context.ResultType, context.HandlerServiceTypeDefinition),
            static key => key.HandlerDefinition.MakeGenericType(key.MessageType, key.ResultType));

        // Resolve only the handlers registered for this closed generic. Registration is idempotent per
        // (message, handler), so DistinctBy is defence-in-depth against a hand-rolled duplicate, not the
        // correctness guarantee.
        var handlers = context.ServiceProvider.GetServices(handlerType)
            .Where(h => h is not null)
            .DistinctBy(h => h!.GetType())
            .ToArray();

        if (handlers.Length == 0)
            throw new InvalidOperationException($"There is no handler to handle the {messageType.FullName} {context.MessageKind}");

        if (handlers.Length > 1)
            throw new InvalidOperationException($"Multiple handlers were found to handle the {messageType.FullName} {context.MessageKind}");

        var handler = handlers[0]!;
        var invoker = CompiledHandlerInvoker.Get(handlerType);

        context.Result = await invoker(handler, context.Message, context.CancellationToken);

        await next(context);
    }
}
