using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Pipelines.Core.Contracts;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace Elsa.Mediator.Middleware;

/// <summary>
/// Logs the message being dispatched and its completion. Applied to the command pipeline (preserving
/// the prior command-logging behaviour); the request pipeline does not compose it.
/// </summary>
[UsedImplicitly]
public sealed class LoggingMiddleware(PipelineDelegate<IMessageContext> next, ILogger<LoggingMiddleware> logger) : IMessageMiddleware
{
    /// <inheritdoc />
    public async ValueTask Invoke(IMessageContext context)
    {
        var messageType = context.Message.GetType();

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Invoking {MessageName}", messageType.Name);

        await next(context);

        if (context.Result is null or Unit && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("{MessageName} completed with no result", messageType.Name);
        }
        else if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("{MessageName} completed with result {MessageResult}", messageType.Name, context.Result);
        }
    }
}
