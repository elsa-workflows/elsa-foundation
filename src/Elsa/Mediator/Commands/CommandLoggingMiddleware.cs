using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Middleware;
using Elsa.Mediator.Core.Models;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace Elsa.Mediator.Commands;

/// <summary>
/// A command middleware that logs the command being invoked.
/// </summary>
[UsedImplicitly]
public sealed class CommandLoggingMiddleware(CommandMiddlewareDelegate next, ILogger<CommandLoggingMiddleware> logger) : ICommandMiddleware
{
    /// <inheritdoc />
    public async ValueTask Invoke(ICommandContext context)
    {
        var commandType = context.Command.GetType();

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Invoking {CommandName}", commandType.Name);

        await next(context);

        if (context.Result is null or Unit && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("{CommandName} completed with no result", commandType.Name);
        }
        else if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("{CommandName} completed with result {CommandResult}", commandType.Name, context.Result);
        }
    }
}