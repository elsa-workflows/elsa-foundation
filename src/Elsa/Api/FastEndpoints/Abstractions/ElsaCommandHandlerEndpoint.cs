using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Api.FastEndpoints.Abstractions;

public abstract class ElsaCommandHandlerEndpoint<TCommand, TResponse>(ICommandSender commandSender, ILogger logger) : ElsaEndpoint<TCommand, TResponse>
    where TResponse : notnull
     where TCommand : ICommand<TResponse>
{
    public override async Task HandleAsync(TCommand req, CancellationToken ct)
    {
        try
        {
            var result = await commandSender.Send(req, ct);
            await Send.OkAsync(result, ct);
        }
        catch (ArgumentException e)
        {
            ThrowError(e, 400);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unexpected error occurred when handling request '{type}'", typeof(TCommand));

            ThrowError("Unexpected error occurred", 400);
        }
    }
}

public abstract class ElsaCommandHandlerEndpoint<TCommand>(ICommandSender commandSender, ILogger logger) : ElsaEndpoint<TCommand>
     where TCommand : ICommand
{
    public override async Task HandleAsync(TCommand req, CancellationToken ct)
    {
        try
        {
            await commandSender.Send(req, ct);
            await Send.OkAsync(cancellation: ct);
        }
        catch (ArgumentException e)
        {
            ThrowError(e, 400);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unexpected error occurred when handling request '{type}'", typeof(TCommand));

            ThrowError("Unexpected error occurred", 400);
        }
    }
}
