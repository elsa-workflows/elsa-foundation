using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Middleware;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace Elsa.Mediator.Commands;

/// <summary>
/// A command middleware that invokes the command.
/// </summary>
[UsedImplicitly]
public sealed class CommandHandlerInvokerMiddleware(CommandMiddlewareDelegate next) : ICommandMiddleware
{
    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2060:Call to MakeGenericMethod can not be statically analyzed", Justification = "The result type is determined at runtime from command types and handlers are registered in DI.")]
    public async ValueTask Invoke(ICommandContext context)
    {
        // Find all handlers for the specified command.
        var command = context.Command;
        var commandType = command.GetType();
        var resultType = context.ResultType;
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, resultType);
        var serviceProvider = context.ServiceProvider;
        var commandHandlers = serviceProvider.GetServices<ICommandHandler>();
        var handlers = commandHandlers
            .DistinctBy(x => x.GetType())
            .Where(handlerType.IsInstanceOfType)
            .ToArray();

        if (handlers.Length == 0)
            throw new InvalidOperationException($"There is no handler to handle the {commandType.FullName} command");

        if (handlers.Length > 1)
            throw new InvalidOperationException($"Multiple handlers were found to handle the {commandType.FullName} command");

        var handler = handlers.First();
        //var strategyContext = new CommandStrategyContext(context, handler, serviceProvider, context.CancellationToken);
        //var strategy = context.CommandStrategy;
        //var executeMethod = strategy.GetType().GetMethod(nameof(ICommandStrategy.ExecuteAsync))!;
        //var executeMethodWithReturnType = executeMethod.MakeGenericMethod(resultType);

        var executeMethod = handler
            .GetType()
            .GetMethod(nameof(ICommandHandler<,>.Handle))
            ?? throw new InvalidProgramException($"Cannot find method 'Handle' on '{handlerType}'");

        // Execute command.
        var task = (Task)executeMethod.Invoke(handler, [command, context.CancellationToken])!
            ?? throw new InvalidProgramException("'Handle' returned null");

        // Wait for completion.
        if (task is not null)
            await task;

        // Get the result of the task.
        var taskWithReturnType = typeof(Task<>).MakeGenericType(resultType);
        var resultProperty = taskWithReturnType.GetProperty(nameof(Task<>.Result));
        context.Result = resultProperty?.GetValue(task);

        // Invoke next middleware.
        await next(context);
    }
}