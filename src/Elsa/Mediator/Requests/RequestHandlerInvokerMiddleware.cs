using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Mediator.Requests;

/// <summary>
/// A middleware component that invokes request handlers.
/// </summary>
public sealed class RequestHandlerInvokerMiddleware(RequestMiddlewareDelegate next) : IRequestMiddleware
{
    /// <inheritdoc />
    public async ValueTask Invoke(IRequestContext context)
    {
        // Find all handlers for the specified request.
        var request = context.Request;
        var requestType = request.GetType();
        var responseType = context.ResponseType;
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);
        var requestHandlers = context.ServiceProvider.GetServices<IRequestHandler>();
        var handlers = requestHandlers
            .DistinctBy(x => x.GetType())
            .Where(handlerType.IsInstanceOfType)
            .ToArray();

        if (handlers.Length == 0)
            throw new InvalidOperationException($"There is no handler to handle the {requestType.FullName} request");

        if (handlers.Length > 1)
            throw new InvalidOperationException($"Multiple handlers were found to handle the {requestType.FullName} request");

        var handler = handlers.First();
        var handleMethod = handlerType.GetMethod(nameof(IRequestHandler<,>.Handle))
            ?? throw new InvalidProgramException($"Could not get method {nameof(IRequestHandler<,>.Handle)} from type '{handlerType.FullName}'");

        var task = (Task?)handleMethod.Invoke(handler, [request, context.CancellationToken])
            ?? throw new InvalidProgramException($"Handle method on request handler '{handlerType.FullName}' returned null");

        await task;

        // Get result of task.
        var taskWithReturnType = typeof(Task<>).MakeGenericType(responseType);
        var resultProperty = taskWithReturnType.GetProperty(nameof(Task<>.Result));
        context.Response = resultProperty?.GetValue(task);

        // Invoke next middleware.
        await next(context);
    }
}