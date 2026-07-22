using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
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
        catch (EntityNotFoundException e)
        {
            ThrowError(e.Message, 404);
        }
        catch (ArgumentException e)
        {
            // A 400 here can be a genuine bad request OR a server-side invariant that a request happened to
            // trip (e.g. a required-but-unsupplied value): the exception is otherwise folded into
            // ProblemDetails with no trace, making such regressions undiagnosable. Log at Debug so the stack
            // is available when investigating without spamming Error for ordinary client input faults.
            logger.LogDebug(e, "Request '{type}' rejected with 400 ({message})", typeof(TCommand), e.Message);
            ThrowError(e, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unexpected error occurred when handling request '{type}'", typeof(TCommand));

            ThrowError("Unexpected error occurred", 500);
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
        catch (EntityNotFoundException e)
        {
            ThrowError(e.Message, 404);
        }
        catch (ArgumentException e)
        {
            logger.LogDebug(e, "Request '{type}' rejected with 400 ({message})", typeof(TCommand), e.Message);
            ThrowError(e, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unexpected error occurred when handling request '{type}'", typeof(TCommand));

            ThrowError("Unexpected error occurred", 500);
        }
    }
}
